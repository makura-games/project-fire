using Content.Client._Scp.Shaders.Common;
using Content.Shared._Scp.Blinking;
using Content.Shared.Alert;
using Content.Shared.Eye.Blinding.Systems;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Scp.Blinking;

public sealed partial class BlinkingSystem : SharedBlinkingSystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private CompatibilityModeActiveWarningSystem _compatibility = default!;
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IOverlayManager _overlayMan = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly SoundSpecifier EyeOpenSound = new SoundCollectionSpecifier("EyeOpen");
    private static readonly SoundSpecifier EyeCloseSound = new SoundCollectionSpecifier("EyeClose");

    private static readonly SoundSpecifier BlinkSound = new SoundPathSpecifier("/Audio/_Scp/Effects/Blinking/blink.ogg");
    private static readonly SoundSpecifier SpawnBlindSound = new SoundCollectionSpecifier("BlinkingSpawnSound", AudioParams.Default.WithVolume(-5));

    private BlinkingOverlay _overlay = default!;
    private const float DefaultAnimationDuration = 0.4f;
    private ProtoId<AlertPrototype>? _localBlinkingAlert;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BlinkableComponent, LocalPlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<BlinkableComponent, LocalPlayerDetachedEvent>(OnDetached);
        SubscribeLocalEvent<BlinkableComponent, EntityEyesStateChanged>(OnPredictedEyesStateChanged);

        SubscribeNetworkEvent<EntityEyesStateChanged>(OnEyesStateChanged);
        SubscribeNetworkEvent<PlayerOpenEyesAnimation>(OnOpenEyesAnimation);

        _overlay = new BlinkingOverlay();

        SetDefaultAnimationDuration();
        _overlay.OnAnimationFinished += SetDefaultAnimationDuration;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlay.Dispose();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_player.LocalEntity is not { Valid: true } localEntity)
            return;

        if (!BlinkableQuery.TryComp(localEntity, out var blinkable))
        {
            if (_localBlinkingAlert.HasValue)
                _alerts.ClearAlert(localEntity, _localBlinkingAlert.Value);

            _localBlinkingAlert = null;
            return;
        }

        if (_localBlinkingAlert.HasValue && _localBlinkingAlert.Value != blinkable.BlinkingAlert)
            _alerts.ClearAlert(localEntity, _localBlinkingAlert.Value);

        _localBlinkingAlert = blinkable.BlinkingAlert;
        UpdateAlert((localEntity, blinkable));
    }

    protected override void OnOpenedEyes(Entity<BlinkableComponent> ent, ref EntityOpenedEyesEvent args)
    {
        base.OnOpenedEyes(ent, ref args);

        OpenEyes(ent, args.Mode, args.UseEffects);
    }

    protected override void OnClosedEyes(Entity<BlinkableComponent> ent, ref EntityClosedEyesEvent args)
    {
        base.OnClosedEyes(ent, ref args);

        CloseEyes(ent, args.Mode, args.UseEffects);
    }

    private void OnAttached(Entity<BlinkableComponent> ent, ref LocalPlayerAttachedEvent args)
    {
        if (_overlayMan.HasOverlay<BlinkingOverlay>())
            return;

        _overlayMan.AddOverlay(_overlay);
    }

    private void OnDetached(Entity<BlinkableComponent> ent, ref LocalPlayerDetachedEvent args)
    {
        if (_localBlinkingAlert.HasValue)
            _alerts.ClearAlert(ent.Owner, _localBlinkingAlert.Value);

        _localBlinkingAlert = null;

        if (!_overlayMan.HasOverlay<BlinkingOverlay>())
            return;

        _overlayMan.RemoveOverlay(_overlay);
    }

    private void OnPredictedEyesStateChanged(Entity<BlinkableComponent> ent, ref EntityEyesStateChanged args)
    {
        ToggleEyesState(ent, ref args);
    }

    /// <summary>
    /// Метод, обрабатывающий сетевой ивент смены состояния глаз.
    /// Используется для не предугадываемых со стороны клиента изменений состояний глаз, требующих эффектов.
    /// Закрывает или открывает глаза на экране в зависимости от содержимого ивента.
    /// </summary>
    private void OnEyesStateChanged(EntityEyesStateChanged ev)
    {
        if (!ev.NetEntity.HasValue)
            return;

        var ent = GetEntity(ev.NetEntity);

        if (!BlinkableQuery.TryComp(ent, out var blinkable))
            return;

        ToggleEyesState((ent.Value, blinkable), ref ev);
    }

    private void ToggleEyesState(Entity<BlinkableComponent> ent, ref EntityEyesStateChanged ev)
    {
        if (ev.NewState == EyesState.Closed)
            CloseEyes(ent, ev.Mode, ev.UseEffects);
        else
            OpenEyes(ent, ev.Mode, ev.UseEffects);
    }

    private void OnOpenEyesAnimation(PlayerOpenEyesAnimation ev)
    {
        var ent = GetEntity(ev.NetEntity);

        if (_player.LocalEntity != ent)
            return;

        _overlay.AnimationDuration = 0.01f;
        _overlay.OnAnimationFinished += AnimationOpenEyes;
        _overlay.CloseEyes();
    }

    /// <summary>
    /// Открывает глаза персонажа, проигрывает специфичный звук открытия глаз.
    /// Сама анимация открытия происходит в оверлее.
    /// </summary>
    private void OpenEyes(Entity<BlinkableComponent> ent, EyeCloseReason mode = EyeCloseReason.None, bool useEffects = false)
    {
        if (!TryEyes(ent))
            return;

        if (!_overlay.AreEyesClosed())
            return;

        _overlay.OpenEyes();
        _audio.PlayGlobal(EyeOpenSound, ent);
    }

    /// <summary>
    /// Закрывает глаза персонажа, проигрывает специфичный звук закрытия глаз.
    /// Сама анимация закрытия происходит в оверлее.
    /// </summary>
    private void CloseEyes(Entity<BlinkableComponent> ent, EyeCloseReason mode = EyeCloseReason.None, bool useEffects = false)
    {
        if (!TryEyes(ent))
            return;

        // Основная проверка, которая определяет наличие эффектов.
        // Если ничего из этого не выполняется, значит эффекты не нужны
        if (!RequiresExplicitOpen(mode) && !IsScpNearby(ent) && !useEffects)
            return;

        _overlay.CloseEyes();
        _audio.PlayGlobal(EyeCloseSound, ent);
    }

    /// <summary>
    /// Клиентский метод проверки на возможность включить эффекты смены закрытия или открытия глаз.
    /// Содержит общие одинаковые проверки для закрытия и открытия глаз.
    /// </summary>
    private bool TryEyes(Entity<BlinkableComponent> ent)
    {
        if (!_timing.IsFirstTimePredicted)
            return false;

        if (_player.LocalEntity != ent)
            return false;

        // Странная конструкция, которая исправляет странную проблему.
        // Почему-то ивенты смены состояния глаз, закрытия, открытия на одного ентити вызываются несколько раз несколько тиков подряд.
        // Это не перекрывается IsFirstTimePredicted, так как все происходит в разных тиках. Как на сервере, так и на клиенте.
        // Причины этого я понять не смог, поэтому сделал этот костыль.
        // Он предотвращает спам звуками и анимацией при смене состояния глаз. Эта логика(пока) используется только на клиенте.
        // В других местах проблем с этим не возникает.
        if (ent.Comp.LastClientSideVisualsAttemptTick >= _timing.CurTick - 1)
        {
            ent.Comp.LastClientSideVisualsAttemptTick = _timing.CurTick;
            return false;
        }

        ent.Comp.LastClientSideVisualsAttemptTick = _timing.CurTick;

        return true;
    }

    private void AnimationOpenEyes()
    {
        _overlay.OnAnimationFinished -= AnimationOpenEyes;

        if (_player.LocalSession == null)
            return;

        _overlay.AnimationDuration = 4f;
        _overlay.OpenEyes();

        _audio.PlayGlobal(SpawnBlindSound, _player.LocalSession);
    }

    private void SetDefaultAnimationDuration()
    {
        _overlay.AnimationDuration = _compatibility.ShouldUseShaders ? DefaultAnimationDuration : 0f;
    }

    /// <summary>
    /// Актуализирует иконку моргания справа у панели чата игрока
    /// </summary>
    private void UpdateAlert(Entity<BlinkableComponent> ent)
    {
        if (IsBlind(ent.AsNullable()))
        {
            _alerts.ShowAlert(ent.Owner, ent.Comp.BlinkingAlert, 4);
            return;
        }

        var timeToNextBlink = ent.Comp.NextBlink - _timing.CurTime;
        var denom = MathF.Max(0.001f, (float) (ent.Comp.BlinkingInterval.TotalSeconds - ent.Comp.BlinkingDuration.TotalSeconds));
        var severity = (short) Math.Clamp(4 - (float) timeToNextBlink.TotalSeconds / denom * 4, 0, 4);

        _alerts.ShowAlert(ent.Owner, ent.Comp.BlinkingAlert, severity);
    }
}

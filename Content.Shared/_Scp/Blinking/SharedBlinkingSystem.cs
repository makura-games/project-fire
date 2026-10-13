using System.Linq;
using Content.Shared._Scp.Blinking.ReducedBlinking;
using Content.Shared._Scp.Helpers;
using Content.Shared._Scp.Scp173;
using Content.Shared._Scp.Watching;
using Content.Shared._Sunrise.Random;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared._Scp.Blinking;

// TODO: Анхардкод: Перенос значений в компоненты
// TODO: Избавиться от членения на EyeClosing и Blinking.
// Они слишком сильно переплетаются, чтобы их так разделять.
// Вместо этого разделить систему на апдейт + обработку ивентов | API + хелперы + ивенты
public abstract partial class SharedBlinkingSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private EyeWatchingSystem _watching = default!;
    [Dependency] private RandomPredictedSystem _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;

    private const float ReducedBlinkingToleranceDecayPerSecond = 0.05f;

    protected EntityQuery<BlinkableComponent> BlinkableQuery;
    protected EntityQuery<ActiveReducedBlinkingUserComponent> ActiveReducedBlinkingQuery;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BlinkableComponent, EntityOpenedEyesEvent>(OnOpenedEyes);
        SubscribeLocalEvent<BlinkableComponent, EntityClosedEyesEvent>(OnClosedEyes);

        SubscribeLocalEvent<BlinkableComponent, MobStateChangedEvent>(OnMobStateChanged);

        InitializeEyeClosing();

        BlinkableQuery = GetEntityQuery<BlinkableComponent>();
        ActiveReducedBlinkingQuery = GetEntityQuery<ActiveReducedBlinkingUserComponent>();
    }

    #region Event handlers

    /// <summary>
    /// Происходит при закрытии глаз.
    /// Устанавливает время, когда глаза будут открыты
    /// </summary>
    protected virtual void OnClosedEyes(Entity<BlinkableComponent> ent, ref EntityClosedEyesEvent args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        var duration = args.CustomBlinkDuration ?? ent.Comp.BlinkingDuration;
        ent.Comp.BlinkEndTime = _timing.CurTime + duration;

        _actions.SetCooldown(ent.Comp.EyeToggleActionEntity, duration);

        // Если глаза были закрыты в режиме, требующем явного открытия,
        // то нам не нужно, чтобы они были автоматически открыты.
        if (RequiresExplicitOpen(ent.Comp.CloseMode))
            ent.Comp.BlinkEndTime = _timing.CurTime + TimeSpan.FromDays(3);

        // Так как персонажи моргают на протяжении всего времени, то для удобства игрока мы
        // не добавляем никакие эффекты, если рядом нет SCP использующего механику зрения.
        var shouldUpdateBlindState =
            args.Mode == EyeCloseReason.Force ||
            RequiresExplicitOpen(args.Mode) ||
            IsScpNearby(ent.Owner);

        if (shouldUpdateBlindState)
            _blindable.UpdateIsBlind(ent.Owner);

        if (_net.IsServer)
            DirtyField(ent.AsNullable(), nameof(BlinkableComponent.BlinkEndTime));
    }

    protected virtual void OnOpenedEyes(Entity<BlinkableComponent> ent, ref EntityOpenedEyesEvent args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        // Если мы закрывали глаза в режиме без авто-открытия, то после открытия у нас до следующего
        // автоматического моргания будет сломан алерт. Поэтому после открытия глаз записываем текущее время.
        ent.Comp.BlinkEndTime = _timing.CurTime;
        if (_net.IsServer)
            DirtyField(ent.AsNullable(), nameof(BlinkableComponent.BlinkEndTime));

        // Задаем время следующего моргания
        var variance = GetBlinkVariance(ent);
        SetNextBlink(ent.AsNullable(), args.CustomNextTimeBlinkInterval ?? ent.Comp.BlinkingInterval, variance);

        // Как только глаза открыты, мы проверяем, слепы ли мы.
        _blindable.UpdateIsBlind(ent.Owner);
    }

    private void OnMobStateChanged(Entity<BlinkableComponent> ent, ref MobStateChangedEvent args)
    {
        CloseEyesIfIncapacitated(ent, ref args);
    }

    #endregion

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        var query = EntityQueryEnumerator<BlinkableComponent>();
        while (query.MoveNext(out var uid, out var blinkableComponent))
        {
            if (blinkableComponent.ReducedBlinkingTolerance > 0f)
            {
                if (!ActiveReducedBlinkingQuery.HasComp(uid))
                {
                    blinkableComponent.ReducedBlinkingTolerance = MathF.Max(
                        0f,
                        blinkableComponent.ReducedBlinkingTolerance - ReducedBlinkingToleranceDecayPerSecond * frameTime
                    );

                    DirtyField(uid, blinkableComponent, nameof(BlinkableComponent.ReducedBlinkingTolerance));
                }
            }

            var blinkableEntity = (uid, blinkableComponent);

            if (TryOpenEyes(blinkableEntity))
                continue;

            TryBlink(blinkableEntity);
        }
    }

    #region Blink logic

    private bool TryBlink(Entity<BlinkableComponent?> ent, TimeSpan? customDuration = null)
    {
        if (!BlinkableQuery.Resolve(ent.Owner, ref ent.Comp))
            return false;

        if (_timing.CurTime < ent.Comp.NextBlink)
            return false;

        if (!TrySetEyelids(ent, EyesState.Closed, customBlinkDuration: customDuration, closeMode: EyeCloseReason.Blink))
            return false;

        return true;
    }

    /// <summary>
    /// Задает время следующего моргания персонажа
    /// </summary>
    /// <remarks>Выделил в отдельный метод, чтобы манипулировать этим извне системы</remarks>
    /// <param name="ent">Моргающий</param>
    /// <param name="interval">Через сколько будет следующее моргание</param>
    /// <param name="variance">Плюс-минус время следующего моргания, чтобы вся станция не моргала в один такт</param>
    /// <param name="predicted">Предугадывается ли клиентом этот вызов метода? Если нет, отправляет клиенту стейт с сервера.</param>
    public void SetNextBlink(Entity<BlinkableComponent?> ent, TimeSpan interval, TimeSpan? variance = null, bool predicted = true)
    {
        if (!BlinkableQuery.Resolve(ent, ref ent.Comp))
            return;

        if (!variance.HasValue)
            variance = TimeSpan.Zero;

        DebugTools.Assert(interval >= TimeSpan.Zero, $"Blink interval must be >= 0, got {interval}");

        if (interval < TimeSpan.Zero)
            interval = TimeSpan.Zero;

        var nextBlinkDelay = interval + variance.Value + ent.Comp.BlinkingIntervalBonus + ent.Comp.AdditionalBlinkingTime;
        if (nextBlinkDelay < TimeSpan.Zero)
            nextBlinkDelay = TimeSpan.Zero;

        ent.Comp.NextBlink = _timing.CurTime + nextBlinkDelay;
        ent.Comp.AdditionalBlinkingTime = TimeSpan.Zero;

        if (!predicted)
            DirtyFields(ent, null, nameof(BlinkableComponent.NextBlink), nameof(BlinkableComponent.AdditionalBlinkingTime));
    }

    public void ResetBlink(Entity<BlinkableComponent?> ent, bool useVariance = true, bool predicted = true)
    {
        if (!BlinkableQuery.Resolve(ent, ref ent.Comp))
            return;

        // Если useVariance == false, то variance = 0
        var variance = useVariance ? GetBlinkVariance((ent.Owner, ent.Comp)) : TimeSpan.Zero;
        SetNextBlink(ent, ent.Comp.BlinkingInterval, variance, predicted);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Проверяет, слеп ли человек в данный момент
    /// <remarks>
    /// Это не то же самое, что и проверка на закрыты ли глаза
    /// Здесь используется проверка по времени до конца моргания и метод компенсации времени
    /// </remarks>
    /// </summary>
    public bool IsBlind(Entity<BlinkableComponent?> ent, bool useTimeCompensation = false)
    {
        if (!BlinkableQuery.Resolve(ent, ref ent.Comp, false))
            return false;

        // Специально для сцп173. Он должен начинать остановку незадолго до того, как у людей откроются глаза
        // Это поможет избежать эффекта "скольжения", когда игрок не может двигаться, но тело все еще летит вперед на инерции
        if (useTimeCompensation)
            return _timing.CurTime + TimeSpan.FromSeconds(0.7) < ent.Comp.BlinkEndTime;

        return _timing.CurTime < ent.Comp.BlinkEndTime;
    }

    public void ForceBlind(Entity<BlinkableComponent?> ent, TimeSpan duration, bool predicted = true)
    {
        if (_mobState.IsIncapacitated(ent))
            return;

        if (!BlinkableQuery.Resolve(ent, ref ent.Comp))
            return;

        // Если у персонажа уже закрыты глаза, то обновляем время.
        // Режим закрытия при этом не меняем, чтобы сохранить текущее поведение.
        if (ent.Comp.State == EyesState.Closed)
        {
            ent.Comp.BlinkEndTime = _timing.CurTime + duration;

            DirtyField(ent, nameof(BlinkableComponent.BlinkEndTime));

            _actions.SetCooldown(ent.Comp.EyeToggleActionEntity, duration);

            return;
        }

        TrySetEyelids(ent.Owner,
            EyesState.Closed,
            predicted: predicted,
            customBlinkDuration: duration,
            closeMode: EyeCloseReason.Force);
    }

    protected TimeSpan GetBlinkVariance(Entity<BlinkableComponent> ent)
    {
        var time = _random.NextFloatForEntity(ent, 0, (float) ent.Comp.BlinkingIntervalVariance.TotalSeconds);
        return TimeSpan.FromSeconds(time);
    }

    #endregion

    /// <summary>
    /// Проверяет, есть ли рядом с игроком Scp, использующий механики зрения
    /// <remarks>
    /// На данный момент это SCP-173 и SCP-096
    /// </remarks>
    /// </summary>
    /// <param name="player">Игрок, которого мы проверяем</param>
    protected bool IsScpNearby(EntityUid player)
    {
        // Получаем всех Scp с механиками зрения, которые видят игрока
        using var scp173List = ListPoolEntity<Scp173Component>.Rent();
        if (!_watching.TryGetAllEntitiesVisibleTo(player, scp173List.Value, flags: LookupFlags.Dynamic | LookupFlags.Approximate))
            return false;

        return scp173List.Value.Any(e => _watching.CanBeWatched(player, e));
    }
}

[ByRefEvent]
public readonly record struct EntityOpenedEyesEvent(
    EyeCloseReason Mode = EyeCloseReason.None,
    bool UseEffects = false,
    TimeSpan? CustomNextTimeBlinkInterval = null);

[ByRefEvent]
public readonly record struct EntityClosedEyesEvent(
    EyeCloseReason Mode = EyeCloseReason.Blink,
    bool UseEffects = false,
    TimeSpan? CustomBlinkDuration = null);

[Serializable, NetSerializable]
public sealed class EntityEyesStateChanged(
    EyesState oldState,
    EyesState newState,
    EyeCloseReason mode = EyeCloseReason.None,
    bool useEffects = false,
    NetEntity? netEntity = null) : EntityEventArgs
{
    public readonly EyesState OldState = oldState;
    public readonly EyesState NewState = newState;
    public readonly EyeCloseReason Mode = mode;
    // TODO: УДАЛИТЬ ЭТО ПОЛЕ
    public readonly bool UseEffects = useEffects;
    public readonly NetEntity? NetEntity = netEntity;
}

[Serializable, NetSerializable]
public sealed class PlayerOpenEyesAnimation(NetEntity netEntity) : EntityEventArgs
{
    public readonly NetEntity NetEntity = netEntity;
}

using Content.Shared._Scp.Fear.Components;
using Content.Shared._Scp.Fear.Systems;
using Content.Shared._Sunrise.Heartbeat;
using Robust.Client.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._Scp.Fear;

public sealed partial class FearSystem : SharedFearSystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private ISharedPlayerManager _player = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly SoundSpecifier HeartbeatSound =
        new SoundPathSpecifier("/Audio/_Sunrise/Effects/heartbeat.ogg");

    private EntityQuery<FearActiveSoundEffectsComponent> _activeEffects;

    /// <summary>
    /// Выключен ли звук сердцебиения у игрока в настройках.
    /// Некоторые игроки имеют фобию подобных звуков, поэтому это опционально.
    /// </summary>
    private bool _isClientSideDisabledHeartbeat;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<HeartbeatOptionsChangedEvent>(OnOptionsChanged);

        _activeEffects = GetEntityQuery<FearActiveSoundEffectsComponent>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_isClientSideDisabledHeartbeat)
            return;

        // Обработка звука сердцебиения, который происходит, когда игрок рядом с источником страха.
        // Чем ближе источник, чем сильнее будут параметры звука.
        // Звук проигрывается раз в некоторое указанное время и создает эффект постоянного сердцебиения.

        var player = _player.LocalEntity;

        if (!_activeEffects.TryComp(player, out var effectsComponent))
            return;

        if (!effectsComponent.PlayHeartbeatSound)
            return;

        if (_timing.CurTime < effectsComponent.NextHeartbeatTime)
            return;

        var audioParams = AudioParams.Default
            .WithPitchScale(effectsComponent.Pitch)
            .AddVolume(effectsComponent.AdditionalVolume);

        _audio.PlayGlobal(HeartbeatSound, player.Value, audioParams);

        SetNextTime(effectsComponent);
    }

    /// <summary>
    /// Задает параметры для работы сердцебиения.
    /// </summary>
    protected override void StartHeartBeat(Entity<FearActiveSoundEffectsComponent> ent)
    {
        base.StartHeartBeat(ent);

        SetNextTime(ent);
    }

    private void OnOptionsChanged(HeartbeatOptionsChangedEvent ev)
    {
        _isClientSideDisabledHeartbeat = !ev.Enabled;
    }

    /// <summary>
    /// Устанавливает время следующего удара сердца
    /// </summary>
    private void SetNextTime(FearActiveSoundEffectsComponent component)
    {
        component.NextHeartbeatTime = _timing.CurTime + component.NextHeartbeatCooldown;
    }
}

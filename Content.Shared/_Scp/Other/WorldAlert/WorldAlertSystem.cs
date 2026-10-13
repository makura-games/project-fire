using Content.Shared.Coordinates;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Spawners;

namespace Content.Shared._Scp.Other.WorldAlert;

public sealed partial class WorldAlertSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private INetManager _net = default!;

    private const float DefaultLifetimeSeconds = 1f;

    public bool TrySpawnAlert(EntityUid target, WorldAlertSettings settings, EntityUid? soundReceiver = null)
    {
        if (settings.Prototype == null)
            return false;

        var alert = PredictedSpawnAttachedTo(settings.Prototype, target.ToCoordinates());
        EnsureTimedDespawn(alert, settings.Lifetime);

        soundReceiver ??= target;
        if (settings.DirectSound)
        {
            if (_net.IsServer)
                _audio.PlayEntity(settings.Sound, target, soundReceiver.Value);
        }
        else
        {
            _audio.PlayPredicted(settings.Sound, target, soundReceiver.Value);
        }

        return true;
    }

    private void EnsureTimedDespawn(EntityUid uid, TimeSpan? lifetime)
    {
        var despawn = EnsureComp<TimedDespawnComponent>(uid);

        if (lifetime.HasValue)
            despawn.Lifetime = (float)lifetime.Value.TotalSeconds;
        else if (despawn.Lifetime < DefaultLifetimeSeconds)
            despawn.Lifetime = DefaultLifetimeSeconds;
    }
}

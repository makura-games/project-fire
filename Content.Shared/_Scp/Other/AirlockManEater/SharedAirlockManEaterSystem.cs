using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Shared._Scp.Other.AirlockManEater;

public abstract partial class SharedAirlockManEaterSystem : EntitySystem
{
    [Dependency] private SharedDoorSystem _door = default!;
    [Dependency] private MobStateSystem _mob = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const float VictimSearchRadiusOpen = 5f;
    private const float VictimSearchRadiusClose = 2.3f;

    private static readonly TimeSpan VictimSearchDelay = TimeSpan.FromSeconds(0.3f);
    private static TimeSpan _nextVictimSearchTime = TimeSpan.Zero;

    private readonly List<Entity<HumanoidProfileComponent>> _midRangeEntities = [];
    private readonly HashSet<Entity<HumanoidProfileComponent>> _closeEntities = [];

    // Возможно это не самый производительный способ
    // Но зато смешно. Ловушка шлюзера
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextVictimSearchTime)
            return;

        var query = AllEntityQuery<AirlockManEaterComponent, DoorComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out _, out var door, out var xform))
        {
            ProcessNearbyEntities(uid, door, xform.Coordinates);
        }

        _nextVictimSearchTime = _timing.CurTime + VictimSearchDelay;
    }

    private void ProcessNearbyEntities(EntityUid airlockUid, DoorComponent door, EntityCoordinates coords)
    {
        _midRangeEntities.Clear();
        _closeEntities.Clear();

        foreach (var ent in _lookup.GetEntitiesInRange<HumanoidProfileComponent>(coords, VictimSearchRadiusOpen))
        {
            if (!IsProperVictim(airlockUid, ent, VictimSearchRadiusOpen))
                continue;

            var distance = (Transform(ent).Coordinates.Position - coords.Position).Length();

            if (distance <= VictimSearchRadiusClose)
                _closeEntities.Add(ent);
            else
                _midRangeEntities.Add(ent);
        }

        if (ShouldClose(door))
        {
            _door.TryClose(airlockUid, door);
            return;
        }

        if (ShouldOpen(door))
            _door.TryOpen(airlockUid, door);
    }

    private bool ShouldClose(DoorComponent door)
    {
        return _closeEntities.Count > 0 && door.State != DoorState.Closed && door.State != DoorState.Closing;
    }

    private bool ShouldOpen(DoorComponent door)
    {
        return _midRangeEntities.Count > 0 && door.State != DoorState.Open && door.State != DoorState.Opening;
    }

    private bool IsProperVictim(EntityUid airlock, EntityUid human, float range)
    {
        return (_mob.IsAlive(human) || _mob.IsCritical(human)) &&
               _interaction.InRangeUnobstructed(airlock, Transform(human).Coordinates, range);
    }
}

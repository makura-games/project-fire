using Content.Shared.Popups;
using Content.Shared._Scp.Other.ScpClog;
using Content.Shared._Scp.Watching;
using Content.Shared.Storage.Components;
using Content.Shared.Whitelist;
using Robust.Server.Containers;
using Content.Server.Popups;
using Content.Server.Fluids.EntitySystems;
using Robust.Server.Audio;
using System.Diagnostics.CodeAnalysis;
using Content.Shared.Chemistry.Components;
using Content.Shared._Scp.Helpers;
using Content.Shared._Scp.Proximity;
using System.Linq;
using Content.Server.Examine;
using Content.Shared.Doors.Components;
using Content.Server.Doors.Systems;
using Content.Shared.Lock;
using Content.Shared._Scp.Other.BunkerMarker;
using Content.Server.Explosion.EntitySystems;
using Robust.Server.GameObjects;
using Content.Shared.Explosion.EntitySystems;

namespace Content.Server._Scp.Other.ScpClog;

public sealed class ScpClogSystem : EntitySystem
{
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly EyeWatchingSystem _watching = default!;
    [Dependency] private readonly ContainerSystem _containerSystem = default!;
    [Dependency] private readonly PuddleSystem _puddle = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly ExamineSystem _examine = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly DoorSystem _door = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly ScpHelpers _helpers = default!;
    [Dependency] private readonly LockSystem _lock = default!;

    private EntityQuery<InsideEntityStorageComponent> _insideQuery;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpClogComponent, ScpClogActionEvent>(OnClog);

        _insideQuery = GetEntityQuery<InsideEntityStorageComponent>();
    }

    private void OnClog(Entity<ScpClogComponent> ent, ref ScpClogActionEvent args)
    {
        if (args.Handled)
            return;

        if (IsInContainer(ent, out var storage))
        {
            var message = Loc.GetString("scp-cage-suppress-ability", ("container", Name(storage.Value)));
            _popup.PopupEntity(message, ent, ent, PopupType.LargeCaution);

            return;
        }

        if (_watching.IsWatchedByAny(ent))
        {
            var message = Loc.GetString("scp173-fast-movement-too-many-watchers");
            _popup.PopupEntity(message, ent, ent, PopupType.LargeCaution);
            return;
        }

        var coords = Transform(ent).Coordinates;

        var tempSol = new Solution();
        tempSol.AddReagent(ent.Comp.Reagent, 25);
        _puddle.TrySpillAt(coords, tempSol, out _, false);

        _audio.PlayPvs(ent.Comp.ClogSound, ent);

        var total = _helpers.GetAroundSolutionVolume(ent, ent.Comp.Reagent, LineOfSightBlockerLevel.None);

        if (total >= ent.Comp.MinTotalSolutionVolume)
        {
            var lookup = _lookup.GetEntitiesInRange(coords, ent.Comp.ClogDeconstructEffectRadius, flags: LookupFlags.Dynamic | LookupFlags.Static)
                .Where(target => _examine.InRangeUnOccluded(ent, target, ent.Comp.ClogDeconstructEffectRadius));

            foreach (var target in lookup)
            {
                if (TryComp<DoorBoltComponent>(target, out var doorBoltComp) && doorBoltComp.BoltsDown)
                    _door.SetBoltsDown((target, doorBoltComp), false, predicted: true);

                if (TryComp<LockComponent>(target, out var lockComp) && lockComp.Locked)
                    _lock.Unlock(target, args.Performer, lockComp);

                if (TryComp<DoorComponent>(target, out var doorComp) && doorComp.State is not DoorState.Open && !HasComp<BunkerMarkerComponent>(target))
                    _door.StartOpening(target);
            }
        }
        if (total >= ent.Comp.ExtraMinTotalSolutionVolume)
        {
            _explosion.QueueExplosion(_transform.GetMapCoordinates(ent), SharedExplosionSystem.DefaultExplosionPrototypeId, 300f, 0.6f, 50f, ent);
        }

        args.Handled = true;
    }

    public bool IsInContainer(Entity<ScpClogComponent> ent, [NotNullWhen(true)] out EntityUid? storage)
    {
        storage = null;

        if (!_insideQuery.TryComp(ent, out var insideEntityStorageComponent))
            return false;

        if (!_containerSystem.TryGetContainingContainer(ent.Owner, out var container))
            return false;

        if (!_whitelist.CheckBoth(container.Owner, ent.Comp.InContainersClogBlacklist, ent.Comp.InContainersClogWhitelist))
            return false;

        storage = insideEntityStorageComponent.Storage;
        return true;
    }
}

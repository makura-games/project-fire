using System.Linq;
using Content.Shared._Scp.Scp096.Main.Components;
using Content.Shared._Scp.Scp106.Components;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Physics;
using Content.Shared.Prying.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._Scp.Other.BunkerMarker;

public sealed partial class BunkerMarkerSystem : EntitySystem
{
    [Dependency] private FixtureSystem _fixture = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    private EntityQuery<FixturesComponent> _fixturesQuery;
    private EntityQuery<Scp106PhantomComponent> _phantomQuery;
    private EntityQuery<Scp106Component> _scp106Query;
    private EntityQuery<ActiveScp096WithoutFaceComponent> _scp096Query;
    private EntityQuery<DoorComponent> _doorQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;

    public override void Initialize()
    {
        base.Initialize();

        _fixturesQuery = GetEntityQuery<FixturesComponent>();
        _phantomQuery = GetEntityQuery<Scp106PhantomComponent>();
        _scp106Query = GetEntityQuery<Scp106Component>();
        _scp096Query = GetEntityQuery<ActiveScp096WithoutFaceComponent>();
        _doorQuery = GetEntityQuery<DoorComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();

        SubscribeLocalEvent<BunkerMarkerComponent, BeforePryEvent>(OnPryingBunkerDoor);
        SubscribeLocalEvent<BunkerMarkerComponent, MapInitEvent>(OnBunkerMarkerInit);
        SubscribeLocalEvent<BunkerMarkerComponent, DoorStateChangedEvent>(OnDoorStateChanged);
        SubscribeLocalEvent<BunkerMarkerComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<BunkerMarkerComponent, EndCollideEvent>(OnEndCollide);
        SubscribeLocalEvent<BunkerMarkerComponent, PreventCollideEvent> (OnPreventCollide);
    }

    private void OnStartCollide(Entity<BunkerMarkerComponent> ent, ref StartCollideEvent args)
    {
        if (args.OurFixtureId != ent.Comp.BunkerSensorFixtureId)
            return;

        var other = args.OtherEntity;

        if (!_fixturesQuery.TryComp(other, out var fixtures))
            return;

        if (_phantomQuery.HasComp(other))
        {
            if (IsPassThroughActive(fixtures))
                return;

            SetFixturesCollision(other, fixtures, (int)(CollisionGroup.MobMask | CollisionGroup.GhostImpassable), (int)CollisionGroup.MobLayer);
        }
        else if (_scp106Query.HasComp(other))
        {
            SetFixturesCollision(other, fixtures, (int)CollisionGroup.MobMask, (int)CollisionGroup.MobLayer);
        }
    }

    private void OnEndCollide(Entity<BunkerMarkerComponent> ent, ref EndCollideEvent args)
    {
        if (args.OurFixtureId != ent.Comp.BunkerSensorFixtureId)
            return;

        var other = args.OtherEntity;

        if (!_fixturesQuery.TryGetComponent(other, out var fixtures))
            return;

        if (_phantomQuery.HasComp(other))
        {
            if (IsPassThroughActive(fixtures))
                return;

            SetFixturesCollision(other, fixtures, (int)(CollisionGroup.SmallMobMask | CollisionGroup.GhostImpassable), (int)CollisionGroup.MobLayer);
        }
        else if (_scp106Query.HasComp(other))
        {
            SetFixturesCollision(other, fixtures, (int)CollisionGroup.SmallMobMask, (int)CollisionGroup.MobLayer);
        }
    }

    private void OnPryingBunkerDoor(Entity<BunkerMarkerComponent> ent, ref BeforePryEvent args)
    {
        if (args.Cancelled)
            return;

        if (_scp096Query.HasComp(args.User))
            args.Cancelled = true;
    }

    private void OnBunkerMarkerInit(Entity<BunkerMarkerComponent> ent, ref MapInitEvent args)
    {
        // Hard blocker. Full-tile square to match the bunker wall marker footprint.
        // Blocks SCP-106 and its phantom while PreventCollide lets everyone else through.
        var blockShape = new PolygonShape();
        blockShape.SetAsBox(
            ent.Comp.BunkerBlockFixtureHalfExtent,
            ent.Comp.BunkerBlockFixtureHalfExtent);

        _fixture.TryCreateFixture(
            ent,
            shape: blockShape,
            ent.Comp.BunkerBlockFixtureId,
            hard: true,
            collisionMask: ent.Comp.BunkerBlockCollision,
            collisionLayer: ent.Comp.BunkerBlockCollision);

        // Sensor. Fires StartCollide/EndCollide.
        //
        // Mask includes MobLayer so SCP-106 and the normal phantom state hit the sensor via their layer,
        // while GhostImpassable keeps pass-through phantom / incorporeals detectable.
        //
        _fixture.TryCreateFixture(
            ent,
            shape: new PhysShapeCircle(ent.Comp.Radius),
            ent.Comp.BunkerSensorFixtureId,
            hard: false,
            collisionMask: ent.Comp.BunkerSensorCollisionMask,
            collisionLayer: ent.Comp.BunkerSensorCollisionLayer);

        if (_doorQuery.TryComp(ent, out var door))
            SetBunkerFixtureCollision(ent, door.State != DoorState.Open);

        if (_fixturesQuery.TryComp(ent, out var fixtures) && _physicsQuery.TryComp(ent, out var physics))
        {
            // Runtime-only fixtures leave the body non-collidable after prototype init if the
            // Fixtures component starts empty. Re-enable collision once our fixtures exist.
            _physics.SetCanCollide(ent, true, manager: fixtures, body: physics);
        }
    }

    private void OnDoorStateChanged(Entity<BunkerMarkerComponent> ent, ref DoorStateChangedEvent args)
    {
        SetBunkerFixtureCollision(ent, args.State != DoorState.Open);
    }

    private void SetBunkerFixtureCollision(Entity<BunkerMarkerComponent> ent, bool canCollide)
    {
        if (!_fixturesQuery.TryComp(ent, out var fixtures))
            return;

        if (!fixtures.Fixtures.TryGetValue(ent.Comp.BunkerBlockFixtureId, out var fixture))
            return;

        var value = canCollide ? ent.Comp.BunkerBlockCollision : 0;

        _physics.SetCollisionMask(ent, ent.Comp.BunkerBlockFixtureId, fixture, value);
        _physics.SetCollisionLayer(ent, ent.Comp.BunkerBlockFixtureId, fixture, value);
    }

    private void SetFixturesCollision(EntityUid uid, FixturesComponent fixtures, int mask, int layer)
    {
        foreach (var (id, fixture) in fixtures.Fixtures)
        {
            _physics.SetCollisionMask(uid, id, fixture, mask);
            _physics.SetCollisionLayer(uid, id, fixture, layer);
        }
    }

    // Pass-through sets every fixture's layer to GhostImpassable as its marker
    private static bool IsPassThroughActive(FixturesComponent fixtures)
    {
        return fixtures.Fixtures.Values.All(f => f.CollisionLayer == (int)CollisionGroup.GhostImpassable);
    }

    private bool ShouldBunkerBlock(EntityUid uid)
    {
        return _phantomQuery.HasComp(uid) || _scp106Query.HasComp(uid);
    }

    private void OnPreventCollide(Entity<BunkerMarkerComponent> ent, ref PreventCollideEvent args)
    {
        if (args.Cancelled)
            return;

        if (!_fixturesQuery.TryGetComponent(ent.Owner, out var fixtures))
            return;

        if (!fixtures.Fixtures.TryGetValue(ent.Comp.BunkerBlockFixtureId, out var bunkerBlockFixture))
            return;

        if (!ReferenceEquals(args.OurFixture, bunkerBlockFixture))
            return;

        // Bunkers should only stop SCP-106 and its phantom.
        if (!ShouldBunkerBlock(args.OtherEntity))
            args.Cancelled = true;
    }
}

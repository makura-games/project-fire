using Content.Shared._Scp.Scp106.Components;
using Content.Shared._Scp.Watching;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Mobs;
using Content.Shared.Movement.Systems;
using Content.Shared.Physics;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._Scp.Scp106.Systems;

public abstract partial class SharedScp106System
{
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private EyeWatchingSystem _watching = default!;

    private void InitializePhantom()
    {
        SubscribeLocalEvent<Scp106PhantomComponent, Scp106ReverseAction>(OnScp106ReverseAction);
        SubscribeLocalEvent<Scp106PhantomComponent, Scp106LeavePhantomAction>(OnScp106LeavePhantomAction);
        SubscribeLocalEvent<Scp106PhantomComponent, Scp106PassThroughAction>(OnScp106PassThroughAction);

        SubscribeLocalEvent<Scp106PhantomComponent, Scp106PassThroughActionEvent>(OnScp106PassThroughActionEvent);

        SubscribeLocalEvent<Scp106PhantomComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<Scp106PhantomComponent, AttemptMobCollideEvent>(OnCollideAttempt);
    }

    private void OnScp106ReverseAction(Entity<Scp106PhantomComponent> ent, ref Scp106ReverseAction args)
    {
        if (args.Handled)
            return;

        if (!_mob.IsDead(args.Target))
            return;

        var doAfter = new DoAfterArgs(EntityManager, ent, args.Delay, new Scp106ReverseActionEvent(), eventTarget: ent, target: args.Target)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
        };

        args.Handled = _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnScp106LeavePhantomAction(Entity<Scp106PhantomComponent> ent, ref Scp106LeavePhantomAction args)
    {
        if (args.Handled)
            return;

        _mob.ChangeMobState(ent, MobState.Dead);
        args.Handled = true;
    }

    private void OnScp106PassThroughAction(Entity<Scp106PhantomComponent> ent, ref Scp106PassThroughAction args)
    {
        if (args.Handled)
            return;

        if (!TryComp<FixturesComponent>(ent, out var fixturesComponent))
            return;

        foreach (var (id, fixture) in fixturesComponent.Fixtures)
        {
            _physics.SetCollisionMask(ent, id, fixture, (int) CollisionGroup.GhostImpassable);
            _physics.SetCollisionLayer(ent, id, fixture, (int) CollisionGroup.GhostImpassable);
        }

        var doAfterEventArgs = new DoAfterArgs(EntityManager, ent, TimeSpan.FromSeconds(args.Delay), new Scp106PassThroughActionEvent(),ent)
        {
            BreakOnDropItem = false,
            BreakOnMove = false,
            BreakOnDamage = false,
            BreakOnHandChange = false,
            BreakOnWeightlessMove = false,
        };

        args.Handled = _doAfter.TryStartDoAfter(doAfterEventArgs);
    }

    private void OnScp106PassThroughActionEvent(Entity<Scp106PhantomComponent> ent, ref Scp106PassThroughActionEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<FixturesComponent>(ent, out var fixturesComponent))
            return;

        foreach (var (id, fixture) in fixturesComponent.Fixtures)
        {
            _physics.SetCollisionMask(ent, id, fixture, (int)(CollisionGroup.SmallMobMask | CollisionGroup.GhostImpassable));
            _physics.SetCollisionLayer(ent, id, fixture, (int)CollisionGroup.MobLayer);
        }

        args.Handled = true;
    }

    private void OnExamined(Entity<Scp106PhantomComponent> ent, ref ExaminedEvent args)
    {
        if (!_mob.IsAlive(args.Examiner))
            return;

        if (!_watching.IsWatchedBy(ent.Owner, args.Examiner))
            return;

        // Ликвидируйся
        _mob.ChangeMobState(ent, MobState.Dead, origin: args.Examiner);
    }

    private static void OnCollideAttempt(Entity<Scp106PhantomComponent> ent, ref AttemptMobCollideEvent args)
    {
        args.Cancelled = true;
    }
}

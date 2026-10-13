using Content.Shared._Scp.Mobs.Components;
using Content.Shared._Scp.Holding.Systems;
using Content.Shared._Scp.ScpMask;
using Content.Shared._Sunrise.Movement.Carrying;
using Content.Shared.Actions.Events;
using Content.Shared.Bed.Sleep;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage.Events;
using Content.Shared.DragDrop;
using Content.Shared.Electrocution;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Pulling.Events;
using Content.Shared.Slippery;
using Content.Shared.Standing;

namespace Content.Shared._Scp.Mobs.Systems;

public sealed partial class ScpRestrictionSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedScpHoldingSystem _scpHolding = default!;
    [Dependency] private ScpMaskSystem _scpMask = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpRestrictionComponent, DisarmAttemptEvent>(OnDisarmAttempt);
        SubscribeLocalEvent<ScpRestrictionComponent, StandAttemptEvent>(OnStandingState);
        SubscribeLocalEvent<ScpRestrictionComponent, DownAttemptEvent>(OnStandingState);
        SubscribeLocalEvent<ScpRestrictionComponent, ElectrocutionAttemptEvent>((_, _, args) => args.Cancel());
        SubscribeLocalEvent<ScpRestrictionComponent, TryingToSleepEvent>((_, _, args) => args.Cancelled = true);
        SubscribeLocalEvent<ScpRestrictionComponent, PullAttemptEvent>(OnPullAttempt);
        SubscribeLocalEvent<ScpRestrictionComponent, BeingPulledAttemptEvent>(OnBeingPulled);
        SubscribeLocalEvent<ScpRestrictionComponent, SlipAttemptEvent>((_, _, args) => args.NoSlip = true);
        SubscribeLocalEvent<ScpRestrictionComponent, BuckleAttemptEvent>((_, _, args) => args.Cancelled = true);
        SubscribeLocalEvent<ScpRestrictionComponent, CanDragEvent>((_, _, args) => args.Handled = false);
        SubscribeLocalEvent<ScpRestrictionComponent, BeforeStaminaDamageEvent>(OnStaminaDamage);
        SubscribeLocalEvent<ScpRestrictionComponent, StartCarryAttemptEvent>(OnCarryAttempt);

        SubscribeLocalEvent<ScpRestrictionComponent, AttemptMobCollideEvent>(OnCollideAttempt);
    }

    private static void OnDisarmAttempt(Entity<ScpRestrictionComponent> ent, ref DisarmAttemptEvent args)
    {
        if (!ent.Comp.CanBeDisarmed)
            args.Cancelled = true;
    }

    private static void OnStandingState<T>(Entity<ScpRestrictionComponent> ent, ref T args) where T : CancellableEntityEventArgs
    {
        if (!ent.Comp.CanStandingState)
            args.Cancel();
    }

    private static void OnPullAttempt(Entity<ScpRestrictionComponent> ent, ref PullAttemptEvent args)
    {
        if (!ent.Comp.CanPull)
            args.Cancelled = true;
    }

    private void OnBeingPulled(Entity<ScpRestrictionComponent> ent, ref BeingPulledAttemptEvent args)
    {
        // Fire added start - let SCP hold validation pass for restricted SCP hold targets.
        if (_scpHolding.CanRedirectPullToScpHold(args.Puller, ent))
            return;
        // Fire added end

        var canBePulled = _mobState.IsIncapacitated(ent)
                          || HasComp<SleepingComponent>(ent)
                          || _scpMask.HasScpMask(ent)
                          || ent.Comp.CanBePulled;

        if (!canBePulled)
            args.Cancel();
    }

    private static void OnStaminaDamage(Entity<ScpRestrictionComponent> ent, ref BeforeStaminaDamageEvent args)
    {
        // Отрицательный урон по стамине(т.е. лечение этого урона) не должно блокироваться никогда.
        if (args.Value <= 0)
            return;

        if (!ent.Comp.CanTakeStaminaDamage)
            args.Cancelled = true;
    }

    private static void OnCarryAttempt(Entity<ScpRestrictionComponent> ent, ref StartCarryAttemptEvent args)
    {
        if (!ent.Comp.CanCarry)
            args.Cancelled = true;
    }

    private static void OnCollideAttempt(Entity<ScpRestrictionComponent> ent, ref AttemptMobCollideEvent args)
    {
        if (!ent.Comp.CanMobCollide)
            args.Cancelled = true;
    }
}

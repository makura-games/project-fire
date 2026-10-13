using Content.Shared.Damage.Systems;
using Content.Shared.Sticky.Components;
using Content.Shared.Trigger;
using Robust.Shared.Timing;

namespace Content.Server._Scp.Scp133;

public sealed partial class Scp133System : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private IGameTiming _gameTiming = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Scp133Component, TriggerEvent>(OnTriggered);
    }

    private void OnTriggered(Entity<Scp133Component> ent, ref TriggerEvent args)
    {
        if (!TryComp<StickyComponent>(ent, out var stickyComp) || stickyComp.StuckTo == null)
            return;

        args.Handled = TryActivate(ent, stickyComp.StuckTo.Value);
    }

    private bool TryActivate(Entity<Scp133Component> ent, EntityUid target)
    {
        if (!CanActivate(ent, target))
            return false;

        ent.Comp.DamageTime = _gameTiming.CurTime + ent.Comp.Delay;
        ent.Comp.EntTarget = target;

        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<Scp133Component>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.DamageTime == null || comp.EntTarget == null)
                continue;

            if (_gameTiming.CurTime < comp.DamageTime.Value)
                continue;

            var target = comp.EntTarget.Value;
            comp.DamageTime = null;
            comp.EntTarget = null;

            PerformAction((uid, comp), target);
        }
    }

    public void PerformAction(Entity<Scp133Component> ent, EntityUid target)
    {
        if (!CanActivate(ent, target))
            return;

        _damageable.TryChangeDamage(target, ent.Comp.Damage, ignoreResistances: true);

        if (ent.Comp.DeleteAfter)
            QueueDel(ent);
    }

    private bool CanActivate(Entity<Scp133Component> ent, EntityUid target)
    {
        return Exists(ent) && Exists(target);
    }
}

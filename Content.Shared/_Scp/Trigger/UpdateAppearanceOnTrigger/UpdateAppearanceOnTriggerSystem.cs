using Content.Shared.Trigger;
using Robust.Shared.Timing;

namespace Content.Shared._Scp.Trigger.UpdateAppearanceOnTrigger;

public sealed partial class UpdateAppearanceOnTriggerSystem : XOnTriggerSystem<UpdateAppearanceOnTriggerComponent>
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<UpdateAppearanceOnTriggerComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<UpdateAppearanceOnTriggerComponent, ComponentShutdown>(OnShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ActiveUpdateAppearanceOnTriggerComponent, UpdateAppearanceOnTriggerComponent>();

        while (query.MoveNext(out var uid, out var active, out var component))
        {
            if (_timing.CurTime < active.ResetAt)
                continue;

            ResetAppearance((uid, active), (uid, component));
        }
    }

    private void OnStartup(Entity<UpdateAppearanceOnTriggerComponent> ent, ref ComponentStartup args)
    {
        SetConfiguredAppearance(ent.Owner, ent.Comp.Key, ent.Comp.ResetValue);
    }

    private void OnShutdown(Entity<UpdateAppearanceOnTriggerComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent.Owner))
            return;

        if (TryComp<ActiveUpdateAppearanceOnTriggerComponent>(ent.Owner, out var active))
        {
            ResetAppearance((ent.Owner, active), ent);
            return;
        }

        SetConfiguredAppearance(ent.Owner, ent.Comp.Key, ent.Comp.ResetValue);
    }

    protected override void OnTrigger(Entity<UpdateAppearanceOnTriggerComponent> ent, EntityUid target, ref TriggerEvent args)
    {
        if (!SetConfiguredAppearance(target, ent.Comp.Key, ent.Comp.Value))
            return;

        args.Handled = true;

        if (ent.Comp.Duration <= TimeSpan.Zero)
        {
            RemComp<ActiveUpdateAppearanceOnTriggerComponent>(ent.Owner);
            return;
        }

        var active = EnsureComp<ActiveUpdateAppearanceOnTriggerComponent>(ent.Owner);
        active.ResetAt = _timing.CurTime + ent.Comp.Duration;
        active.ResetTarget = target;
    }

    private void ResetAppearance(Entity<ActiveUpdateAppearanceOnTriggerComponent> activeEnt, Entity<UpdateAppearanceOnTriggerComponent> ent)
    {
        var target = activeEnt.Comp.ResetTarget ?? ent.Owner;
        SetConfiguredAppearance(target, ent.Comp.Key, ent.Comp.ResetValue);
        RemCompDeferred<ActiveUpdateAppearanceOnTriggerComponent>(ent.Owner);
    }

    private bool SetConfiguredAppearance(EntityUid uid, Enum key, UpdateAppearanceOnTriggerValue configuredValue)
    {
        if (!configuredValue.TryGetValue(out var value))
            return false;

        SetAppearance(uid, key, value);

        return true;
    }

    private void SetAppearance(EntityUid uid, Enum key, object value)
    {
        var appearance = EnsureComp<AppearanceComponent>(uid);
        _appearance.SetData(uid, key, value, appearance);
    }
}

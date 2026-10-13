using Content.Shared.Atmos;
using Content.Shared.Trigger.Systems;

namespace Content.Shared._Scp.Other.Triggers.TriggerOnIgnite;

public sealed partial class TriggerOnIgniteSystem : EntitySystem
{
    [Dependency] private TriggerSystem _trigger = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TriggerOnIgniteComponent, IgnitedEvent>(OnIgnited);
    }

    private void OnIgnited(Entity<TriggerOnIgniteComponent> ent, ref IgnitedEvent args)
    {
        _trigger.Trigger(ent);
    }
}

using Content.Shared._Scp.Scp173;
using Content.Shared.Trigger;

namespace Content.Shared._Scp.Trigger.BlindOnTrigger;

public sealed partial class BlindOnTriggerSystem : EntitySystem
{
    [Dependency] private SharedScp173System _scp173 = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BlindOnTriggerComponent, TriggerEvent>(OnTrigger);
    }

    private void OnTrigger(Entity<BlindOnTriggerComponent> ent, ref TriggerEvent args)
    {
        if (args.Key != null && !ent.Comp.KeysIn.Contains(args.Key))
            return;

        _scp173.BlindEveryoneInRange(ent, ent.Comp.Time, false);
    }
}

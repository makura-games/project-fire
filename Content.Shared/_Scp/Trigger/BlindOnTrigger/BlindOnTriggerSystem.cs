using Content.Shared._Scp.Other.ScpBlind;
using Content.Shared.Trigger;

namespace Content.Shared._Scp.Trigger.BlindOnTrigger;

public sealed class BlindOnTriggerSystem : EntitySystem
{
    [Dependency] private readonly SharedScpBlindSystem _scpBlind = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BlindOnTriggerComponent, TriggerEvent>(OnTrigger);
    }

    private void OnTrigger(Entity<BlindOnTriggerComponent> ent, ref TriggerEvent args)
    {
        if (args.Key != null && !ent.Comp.KeysIn.Contains(args.Key))
            return;

        _scpBlind.BlindEveryoneInRange(ent, ent.Comp.Time, false);
    }
}

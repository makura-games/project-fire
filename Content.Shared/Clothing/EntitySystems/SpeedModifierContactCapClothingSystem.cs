using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Movement.Events;

namespace Content.Shared.Clothing.EntitySystems;

public sealed partial class SpeedModifierContactCapClothingSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpeedModifierContactCapClothingComponent, InventoryRelayedEvent<GetSpeedModifierContactCapEvent>>(OnGetMaxSlow);

        // Fire added start
        SubscribeLocalEvent<SpeedModifierContactCapClothingComponent, GetSpeedModifierContactCapEvent>(OnGetMaxSlowDirect);
        // Fire added end
    }

    private void OnGetMaxSlow(Entity<SpeedModifierContactCapClothingComponent> ent, ref InventoryRelayedEvent<GetSpeedModifierContactCapEvent> args)
    {
        args.Args.SetIfMax(ent.Comp.MaxContactSprintSlowdown, ent.Comp.MaxContactWalkSlowdown);
    }

    // Fire added start
    private void OnGetMaxSlowDirect(Entity<SpeedModifierContactCapClothingComponent> ent, ref GetSpeedModifierContactCapEvent args)
    {
        args.SetIfMax(ent.Comp.MaxContactSprintSlowdown, ent.Comp.MaxContactWalkSlowdown);
    }
    // Fire added end
}

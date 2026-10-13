using Content.Shared.Clothing;
using Content.Shared.Drunk;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Prototypes;

namespace Content.Server._Scp.Backrooms.AddDrunkClothing;

public sealed partial class AddDrunkClothingSystem : EntitySystem
{
    [Dependency] private SharedDrunkSystem _drunkSystem = default!;
    [Dependency] private StatusEffectsSystem _effects = default!;

    private static readonly EntProtoId DrunkEffect = "StatusEffectDrunk";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AddDrunkClothingComponent, ClothingGotEquippedEvent>(OnGotEquipped);
        SubscribeLocalEvent<AddDrunkClothingComponent, ClothingGotUnequippedEvent>(OnGotUnequipped);
    }

    private void OnGotEquipped(Entity<AddDrunkClothingComponent> entity, ref ClothingGotEquippedEvent args)
    {
        if (_effects.HasStatusEffect(args.Wearer, DrunkEffect))
            return;

        _drunkSystem.TryApplyDrunkenness(args.Wearer, TimeSpan.FromDays(1));

        entity.Comp.IsActive = true;
    }

    private void OnGotUnequipped(Entity<AddDrunkClothingComponent> entity, ref ClothingGotUnequippedEvent args)
    {
        if (!entity.Comp.IsActive)
            return;

        _drunkSystem.TryRemoveDrunkenness(args.Wearer);

        entity.Comp.IsActive = false;
    }
}

using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Shared._Scp.Weapons.Ranged;

/// <summary>
/// Система, позволяющая задавать параметры стрельбы.
/// Нужна, чтобы реализовать компонент у стрелка, увеличивающий разброса стрельбы.
/// </summary>
public sealed partial class DispersingShotSystem : EntitySystem
{
    [Dependency] private SharedGunSystem _gunSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GunComponent, GunRefreshModifiersEvent>(OnRefreshModifiers);

        SubscribeLocalEvent<GunComponent, HandDeselectedEvent>(OnGunDeselected);

        SubscribeLocalEvent<DispersingShotSourceComponent, ComponentInit>(OnComponentAdd);
        SubscribeLocalEvent<DispersingShotSourceComponent, ComponentShutdown>(OnComponentRemove);
    }

    private void OnRefreshModifiers(Entity<GunComponent> ent, ref GunRefreshModifiersEvent args)
    {
        var user = Transform(ent).ParentUid;
        if (!TryComp<DispersingShotSourceComponent>(user, out var dispersionComp))
            return;

        args.AngleIncrease *= Math.Max(dispersionComp.DefaultAngleIncreaseModifier, dispersionComp.AngleIncreaseMultiplier);
        args.MaxAngle *= Math.Max(dispersionComp.DefaultMaxAngleMultiplier, dispersionComp.MaxAngleMultiplier);
    }

    private void RefreshHeldGun(EntityUid user)
    {
        if (!_handsSystem.TryGetActiveItem(user, out var heldEntity))
            return;

        if (!HasComp<GunComponent>(heldEntity))
            return;

        _gunSystem.RefreshModifiers(heldEntity.Value);
    }

    private void OnGunDeselected(Entity<GunComponent> ent, ref HandDeselectedEvent args)
    {
        RefreshHeldGun(args.User);
    }

    private void OnComponentAdd(Entity<DispersingShotSourceComponent> ent, ref ComponentInit args)
    {
        RefreshHeldGun(ent);
    }

    private void OnComponentRemove(Entity<DispersingShotSourceComponent> ent, ref ComponentShutdown args)
    {
        RefreshHeldGun(ent);
    }
}

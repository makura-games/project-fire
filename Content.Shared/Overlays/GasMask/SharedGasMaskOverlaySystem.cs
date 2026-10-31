using System.Collections.Generic;
using Content.Shared.Clothing;            // ItemMaskToggledEvent
using Content.Shared.Clothing.Components; // MaskComponent
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Robust.Shared.GameObjects;

namespace Content.Shared.Overlays.GasMask;

public abstract class SharedGasMaskOverlaySystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        Log.Info("[GASMASK] Shared Initialize");

        SubscribeLocalEvent<GasMaskOverlayComponent, GotEquippedEvent>(OnCompEquip);
        SubscribeLocalEvent<GasMaskOverlayComponent, GotUnequippedEvent>(OnCompUnequip);
        SubscribeLocalEvent<GasMaskOverlayComponent, InventoryRelayedEvent<RefreshGasMaskOverlayEvent>>(OnRefreshEquipmentHud);
        SubscribeLocalEvent<GasMaskOverlayComponent, RefreshGasMaskOverlayEvent>(OnRefreshComponentHud);

        // Реагируем на стягивание/надевание маски
        SubscribeLocalEvent<GasMaskOverlayComponent, ItemMaskToggledEvent>(OnMaskToggled);
    }

    private void OnCompEquip(Entity<GasMaskOverlayComponent> ent, ref GotEquippedEvent args)
    {
        Log.Info($"[GASMASK] OnCompEquip {ent.Owner} -> {args.EquipTarget}");
        if (!ent.Comp.RelayOverlay)
            return;

        RefreshOverlay(args.EquipTarget);
    }

    private void OnCompUnequip(Entity<GasMaskOverlayComponent> ent, ref GotUnequippedEvent args)
    {
        Log.Info($"[GASMASK] OnCompUnequip {ent.Owner} <- {args.EquipTarget}");
        if (!ent.Comp.RelayOverlay)
            return;

        RefreshOverlay(args.EquipTarget);
    }

    /// <summary>
    /// Реакция на стягивание/надевание маски.
    /// Мы НЕ трогаем <see cref="GasMaskOverlayComponent.Enabled"/>, потому что это
    /// синхронизируемое с сервера поле — изменение на клиенте будет перезаписано
    /// при следующем <c>AfterAutoHandleStateEvent</c>, что и приводило к "миганию"
    /// оверлея. Источником истины служит авторитетное <see cref="MaskComponent.IsToggled"/>.
    /// </summary>
    private void OnMaskToggled(Entity<GasMaskOverlayComponent> ent, ref ItemMaskToggledEvent args)
    {
        Log.Info($"[GASMASK] OnMaskToggled {ent.Owner}, IsToggled={args.Mask.Comp.IsToggled}");

        if (!ent.Comp.RelayOverlay)
            return;

        if (args.Wearer is not { } wearer)
            return;

        RefreshOverlay(wearer);
    }

    protected virtual void OnRefreshEquipmentHud(
        Entity<GasMaskOverlayComponent> ent,
        ref InventoryRelayedEvent<RefreshGasMaskOverlayEvent> args)
    {
        Log.Info($"[GASMASK] OnRefreshEquipmentHud on {ent.Owner}");
        OnRefreshComponentHud(ent, ref args.Args);
    }

    protected virtual void OnRefreshComponentHud(
        Entity<GasMaskOverlayComponent> ent,
        ref RefreshGasMaskOverlayEvent args)
    {
        Log.Info($"[GASMASK] OnRefreshComponentHud on {ent.Owner}, Enabled={ent.Comp.Enabled}");

        // Сначала — авторитетная проверка состояния маски.
        // Если маска стянута, оверлей не показываем, независимо от Enabled.
        if (TryComp<MaskComponent>(ent.Owner, out var mask) && mask.IsToggled)
            return;

        if (!ent.Comp.Enabled)
            return;

        args.Entities.Add(ent);
    }

    protected virtual void RefreshOverlay(EntityUid entity) { }
}

public sealed class RefreshGasMaskOverlayEvent : EntityEventArgs, IInventoryRelayEvent
{
    public SlotFlags TargetSlots => SlotFlags.MASK;
    public List<Entity<GasMaskOverlayComponent>> Entities = new();
}

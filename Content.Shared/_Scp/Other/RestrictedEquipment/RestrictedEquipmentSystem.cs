using Content.Shared.Inventory.Events;
using Content.Shared.Popups;
using Content.Shared.Whitelist;

namespace Content.Shared._Scp.Other.RestrictedEquipment;

public sealed partial class RestrictedEquipmentSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RestrictedEquipmentComponent, IsEquippingAttemptEvent>(OnEquipAttempt);
    }

    private void OnEquipAttempt(Entity<RestrictedEquipmentComponent> ent, ref IsEquippingAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if ((args.SlotFlags & ent.Comp.TargetSlots) == 0)
            return;

        if (!_whitelist.CheckBoth(args.Equipment, ent.Comp.Blacklist, ent.Comp.Whitelist))
        {
            args.Cancel();
            _popup.PopupClient(Loc.GetString(ent.Comp.Reason), ent, ent, PopupType.SmallCaution);
        }
    }
}

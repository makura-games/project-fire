using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;

namespace Content.Server.Clothing.Systems;

public sealed partial class FilterSystem : EntitySystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FilterComponent, ExaminedEvent>(OnFilterExamined);
    }

    /// <summary>
    ///     Пытается отфильтровать реагенты, попадающие в кровоток цели.
    ///     Вызывается из SmokeSystem перед TryAddToBloodstream.
    /// </summary>
    public bool TryFilterReagents(EntityUid target, Solution solution)
    {
        // 1. Ищем надетую маску.
        if (!_inventory.TryGetSlotEntity(target, "mask", out var maskUidNullable))
            return false;

        if (maskUidNullable is not { } maskUid)
            return false;

        // Стянутая маска не фильтрует воздух.
        if (TryComp<MaskComponent>(maskUid, out var mask) && mask.IsToggled)
            return false;


        // 2. Ищем в маске слот "filter".
        if (!TryComp<ItemSlotsComponent>(maskUid, out var itemSlots))
            return false;

        if (!_itemSlots.TryGetSlot(maskUid, "filter", out var slot, itemSlots))
            return false;

        if (slot.Item == null)
            return false;

        // 3. Проверяем, что в слоте лежит предмет с FilterComponent.
        if (!TryComp<FilterComponent>(slot.Item.Value, out var filter))
            return false;

        // 4. Если заряд закончился — фильтр бесполезен.
        if (filter.Charge <= 0)
            return false;

        // 5. Фильтруем реагенты.
        var filteredAnything = false;
        var toRemove = new List<ReagentQuantity>();

        foreach (var reagentQuantity in solution.Contents)
        {
            var reagentId = reagentQuantity.Reagent;
            var quantity = reagentQuantity.Quantity;

            // Пропускаем газы, которые нужны для дыхания (актуально только для фильтра без whitelist).
            if (filter.FilteredReagents.Count == 0 &&
                (reagentId.Prototype == "Oxygen" || reagentId.Prototype == "Nitrogen"))
            {
                continue;
            }

            // Если у фильтра задан whitelist, пропускаем всё, чего в нём нет.
            if (filter.FilteredReagents.Count > 0 &&
                !filter.FilteredReagents.Contains(reagentId.Prototype))
            {
                continue;
            }

            var removeAmount = quantity * filter.Efficiency;
            if (removeAmount <= FixedPoint2.Zero)
                continue;

            toRemove.Add(new ReagentQuantity(reagentId, removeAmount));
            filteredAnything = true;
        }

        foreach (var reagent in toRemove)
        {
            solution.RemoveReagent(reagent);
        }

        if (!filteredAnything)
            return false;

        // 6. Тратим заряд.
        filter.Charge -= filter.UseRate;
        Dirty(slot.Item.Value, filter);

        return true;
    }

    private void OnFilterExamined(EntityUid uid, FilterComponent component, ExaminedEvent args)
    {
        if (component.Charge <= 0)
        {
            args.PushMarkup(Loc.GetString("gas-mask-filter-examine-depleted"));
            return;
        }

        var percent = (int)(component.Charge / component.MaxCharge * 100);
        var color = percent switch
        {
            > 50 => "green",
            > 20 => "yellow",
            _ => "red"
        };

        args.PushMarkup(Loc.GetString("gas-mask-filter-examine-charge", ("color", color), ("percent", percent)));
    }
}

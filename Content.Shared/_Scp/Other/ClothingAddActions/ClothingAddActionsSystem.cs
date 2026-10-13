using Content.Shared.Actions;
using Content.Shared.Inventory.Events;

namespace Content.Shared._Scp.Other.ClothingAddActions;

public sealed partial class ClothingAddActionsSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private ActionContainerSystem _actionContainer = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ClothingAddActionsComponent, GotEquippedEvent>(OnEquip);

        SubscribeLocalEvent<ClothingAddActionsComponent, GotUnequippedEvent>(OnUnequip);
        SubscribeLocalEvent<ClothingAddActionsComponent, ComponentShutdown>(OnShutdown);
    }

    /// <summary>
    /// Добавляем акшены при надевании
    /// </summary>
    private void OnEquip(Entity<ClothingAddActionsComponent> ent, ref GotEquippedEvent args)
    {
        foreach (var action in ent.Comp.Actions)
        {
            EntityUid? actionEnt = null;
            _actions.AddAction(args.Equipment, ref actionEnt, action);

            if (actionEnt != null)
                ent.Comp.ActionEntities.Add(actionEnt.Value);
        }

        if (ent.Comp.ActionEntities.Count == 0)
            return;

        ent.Comp.ActionsOwner = args.Equipment;
    }

    /// <summary>
    /// Убираем акшены при снятии
    /// </summary>
    private void OnUnequip(Entity<ClothingAddActionsComponent> ent, ref GotUnequippedEvent args)
    {
        RemoveActions(ent);
    }

    /// <summary>
    /// Убираем акшены при удалении компонента или целового ентити
    /// </summary>
    private void OnShutdown(Entity<ClothingAddActionsComponent> ent, ref ComponentShutdown args)
    {
        RemoveActions(ent);
    }

    private void RemoveActions(Entity<ClothingAddActionsComponent> ent)
    {
        if (!Exists(ent.Comp.ActionsOwner))
            return;

        foreach (var actionEnt in ent.Comp.ActionEntities)
        {
            _actionContainer.RemoveAction(actionEnt);
        }

        ent.Comp.ActionEntities.Clear();
        ent.Comp.ActionsOwner = null;
    }
}

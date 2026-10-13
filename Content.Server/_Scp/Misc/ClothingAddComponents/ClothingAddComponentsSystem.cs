using Content.Shared.Inventory.Events;

namespace Content.Server._Scp.Misc.ClothingAddComponents;

// TODO: Добавить проверку на наличие аналогичных компонентов у ентити
// Не используйте пожалуйста это для чего-то сложнее, кроме выдачи одного единственного компонента, который нигде по-другому не выдается

public sealed class ClothingAddComponentsSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        // TODO: Использовать релей инвентори вместо это параши
        SubscribeLocalEvent<ClothingAddComponentsComponent, GotEquippedEvent>(OnEquip);
        SubscribeLocalEvent<ClothingAddComponentsComponent, GotUnequippedEvent>(OnUnequip);
    }

    /// <summary>
    /// Выдает компоненты тагрету из списка компонентов ентити
    /// </summary>
    /// <param name="entity">Вещь, которую экипируют</param>
    /// <param name="args">Ивент надевания</param>
    private void OnEquip(Entity<ClothingAddComponentsComponent> entity, ref GotEquippedEvent args)
    {
        var target = args.Equipment;
        EntityManager.AddComponents(target, entity.Comp.Components);
    }

    /// <summary>
    /// Убирает компоненты тагрету из списка компонентов ентити
    /// </summary>
    /// <param name="entity">Вещь, которую сняли</param>
    /// <param name="args">Ивент снятия</param>
    private void OnUnequip(Entity<ClothingAddComponentsComponent> entity, ref GotUnequippedEvent args)
    {
        var target = args.Equipment;
        EntityManager.RemoveComponents(target, entity.Comp.Components);
    }
}

using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Clothing.Components;

/// <summary>
///     Компонент, который вешается на предмет-фильтр.
///     Хранит заряд, скорость расхода и список реагентов, которые фильтр задерживает.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FilterComponent : Component
{
    /// <summary>
    ///     Текущий заряд фильтра. Когда доходит до 0, фильтр перестаёт работать.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 Charge = 100;

    /// <summary>
    ///     Максимальный заряд (для отображения в examine).
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 MaxCharge = 100;

    /// <summary>
    ///     Сколько заряда тратится за одно срабатывание (один «вдох» дыма).
    /// </summary>
    [DataField]
    public FixedPoint2 UseRate = 1;

    /// <summary>
    ///     Эффективность фильтрации: 1.0 = 100% реагента блокируется, 0.95 = 95% и т.д.
    /// </summary>
    [DataField]
    public float Efficiency = 0.95f;

    /// <summary>
    ///     Список реагентов, которые фильтр способен задерживать.
    ///     Если список пуст — фильтр пытается задержать любые реагенты (кроме газов, см. систему).
    /// </summary>
    [DataField]
    public HashSet<ProtoId<ReagentPrototype>> FilteredReagents = new();
}

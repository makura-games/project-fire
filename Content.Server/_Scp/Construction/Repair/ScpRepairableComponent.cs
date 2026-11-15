using Content.Server.Construction.Components;
using Content.Shared.Construction.Prototypes;
using Content.Shared.EntityConditions;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Server._Scp.Construction.Repair;

/// <summary>
/// Компонент, позволяющий сущности быть отремонтированной с использованием <see cref="ConstructionGraphPrototype"/>.
/// Является аналогом <see cref="ConstructionComponent"/>, но для ремонта.
/// <seealso cref="ScpRepairableSystem"/>
/// <seealso cref="ScpRepairableCanSeeExamineHintsComponent"/>
/// </summary>
[RegisterComponent]
public sealed partial class ScpRepairableComponent : Component
{
    /// <summary>
    /// Граф, по которому будет производиться ремонт.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<ConstructionGraphPrototype> Graph;

    #region Restrictions

    /// <summary>
    /// Правила проверки, которые будут применяться к цели ремонта
    /// Если проверок нет или они проходят - ремонт доступен, иначе показывается сообщение о провале.
    /// </summary>
    [DataField]
    public EntityCondition[] TargetConditions = [];

    /// <summary>
    /// Правила проверки, которые будут применяться к использующему ремонт (игроку).
    /// Если проверок нет или они проходят - ремонт доступен, иначе показывается сообщение о провале.
    /// </summary>
    [DataField]
    public EntityCondition[] UserConditions = [];

    /// <summary>
    /// Белый список для ремонтируемой сущности (цели).
    /// </summary>
    [DataField]
    public EntityWhitelist? TargetWhitelist;

    /// <summary>
    /// Черный список для ремонтируемой сущности (цели).
    /// </summary>
    [DataField]
    public EntityWhitelist? TargetBlacklist;

    /// <summary>
    /// Белый список для использующего ремонт (игрока).
    /// </summary>
    [DataField]
    public EntityWhitelist? UserWhitelist;

    /// <summary>
    /// Черный список для использующего ремонт (игрока).
    /// </summary>
    [DataField]
    public EntityWhitelist? UserBlacklist;

    #endregion

    #region Examining

    /// <summary>
    /// Строка, которая будет показываться определенным цветом, когда сущность можно отремонтировать.
    /// </summary>
    [DataField]
    public string ExamineMessage = "scp-repairable-can-be-repaired";

    /// <summary>
    /// Цвет, который будет использован для строки ExamineMessage.
    /// </summary>
    [DataField]
    public Color ExamineColor = Color.LightGray;

    /// <summary>
    /// Белый список для сущности, которая осматривает цель ремонта.
    /// Определяет, увидит ли осматривающий подсказки при ремонте.
    /// </summary>
    [DataField]
    public EntityWhitelist? ExamineWhitelist;

    /// <summary>
    /// Черный список для сущности, которая осматривает цель ремонта.
    /// Определяет, увидит ли осматривающий подсказки при ремонте.
    /// </summary>
    [DataField]
    public EntityWhitelist? ExamineBlacklist;

    #endregion

    #region Repair State

    /// <summary>
    /// Текущий узел в графе ремонта.
    /// </summary>
    [ViewVariables]
    public string? CurrentNode;

    /// <summary>
    /// Индекс текущего ребра в узле.
    /// </summary>
    [ViewVariables]
    public int? EdgeIndex;

    /// <summary>
    /// Индекс текущего шага в ребре.
    /// </summary>
    [ViewVariables]
    public int StepIndex;

    /// <summary>
    /// Стартовый узел графа (для возврата к началу цикла).
    /// </summary>
    [ViewVariables]
    public string? StartNode;

    #endregion
}


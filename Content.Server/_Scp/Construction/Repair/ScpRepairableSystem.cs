using System.Diagnostics.CodeAnalysis;
using Content.Server.Construction;
using Content.Server.DoAfter;
using Content.Server.Popups;
using Content.Server.Stack;
using Content.Server.Tools;
using Content.Server.Interaction;
using Content.Shared._Scp.Damage.ExaminableDamage;
using Content.Shared.Construction;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Construction.Steps;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Random.Rules;
using Content.Shared.Whitelist;
using Robust.Server.Containers;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._Scp.Construction.Repair;

/// <summary>
/// Система ремонта структур с использованием <see cref="ConstructionGraphPrototype"/>.
/// </summary>
public sealed partial class ScpRepairableSystem : EntitySystem
{
    [Dependency] private ConstructionSystem _construction = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private RulesSystem _rules = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private DoAfterSystem _doAfter = default!;
    [Dependency] private ContainerSystem _container = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private ToolSystem _tool = default!;
    [Dependency] private InteractionSystem _interaction = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    private const int ExaminePriority = SharedScpExaminableDamageSystem.Priority - 3;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpRepairableComponent, InteractUsingEvent>(OnInteractUsing, after: [typeof(ConstructionSystem)]);
        SubscribeLocalEvent<ScpRepairableComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ScpRepairableComponent, ConstructionInteractDoAfterEvent>(OnRepairDoAfter, after: [typeof(ConstructionSystem)]);
    }

    #region Event handlers

    private void OnInteractUsing(Entity<ScpRepairableComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (TryRepair(ent, args.User, args.Used))
            args.Handled = true;
    }

    private void OnRepairDoAfter(Entity<ScpRepairableComponent> ent, ref ConstructionInteractDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        if (!args.Used.HasValue)
        {
            Log.Error($"Tried to repair {ToPrettyString(ent)} with non-existing entity in args.Used field. Attempt aborted");
            return;
        }

        CompleteRepairDoAfter(ent, args.User, args.Used.Value);
    }

    private void OnExamined(Entity<ScpRepairableComponent> ent, ref ExaminedEvent args)
    {
        if (!CheckRepairConditions(ent, args.Examiner, ent.Comp, true))
            return;

        if (string.IsNullOrEmpty(ent.Comp.CurrentNode))
            InitializeRepairState(ent.Comp);

        var colorHex = ent.Comp.ExamineColor.ToHex();
        var nextStep = GetNextStep(ent);

        using (args.PushGroup(nameof(ScpRepairableComponent), ExaminePriority))
        {
            if (!string.IsNullOrEmpty(ent.Comp.ExamineMessage))
                args.PushMarkup($"[color={colorHex}]{Loc.GetString(ent.Comp.ExamineMessage)}[/color]");

            // Проверки для возможности видеть подсказки при ремонте.
            // Задумывается, что их должен видеть только персонал инженерно-технической службы
            if (_whitelist.CheckBoth(args.Examiner, ent.Comp.ExamineBlacklist, ent.Comp.ExamineWhitelist))
                nextStep?.DoExamine(args);
        }
    }

    #endregion

    /// <summary>
    /// Проверяет все условия ремонта (RulesRule и EntityWhitelist).
    /// </summary>
    private bool CheckRepairConditions(EntityUid target, EntityUid user, ScpRepairableComponent repairable, bool silent = false)
    {
        // Проверка RulesPrototype для цели
        if (_prototype.TryIndex(repairable.TargetRules, out var targetRule) && !_rules.IsTrue(target, targetRule))
        {
            if (!silent)
                _popup.PopupEntity(Loc.GetString("scp-repairable-failed-target"), target, user);

            return false;
        }

        // Проверка RulesPrototype для пользователя
        if (_prototype.TryIndex(repairable.UserRules, out var userRules) && !_rules.IsTrue(user, userRules))
        {
            if (!silent)
                _popup.PopupEntity(Loc.GetString("scp-repairable-failed-user"), target, user);

            return false;
        }

        // Проверка EntityWhitelist для цели
        if (!_whitelist.CheckBoth(target, repairable.TargetBlacklist, repairable.TargetWhitelist))
        {
            if (!silent)
                _popup.PopupEntity(Loc.GetString("scp-repairable-failed-target"), target, user);

            return false;
        }

        // Проверка EntityWhitelist для пользователя
        if (!_whitelist.CheckBoth(user, repairable.UserBlacklist, repairable.UserWhitelist))
        {
            if (!silent)
                _popup.PopupEntity(Loc.GetString("scp-repairable-failed-user"), target, user);

            return false;
        }

        return true;
    }

    /// <summary>
    /// Инициализирует состояние ремонта для сущности.
    /// </summary>
    private bool InitializeRepairState(ScpRepairableComponent repairable)
    {
        if (!_prototype.Resolve(repairable.Graph, out var graph))
            return false;

        if (graph.Start == null)
        {
            Log.Error($"Graph {repairable.Graph} does not have any start node");
            return false;
        }

        repairable.CurrentNode = graph.Start;
        repairable.StartNode = graph.Start;
        repairable.EdgeIndex = null;
        repairable.StepIndex = 0;

        return true;
    }

    /// <summary>
    /// Сбрасывает состояние ремонта к стартовому ноду.
    /// </summary>
    private static void ResetToStart(ScpRepairableComponent repairable)
    {
        repairable.CurrentNode = repairable.StartNode;
        repairable.EdgeIndex = null;
        repairable.StepIndex = 0;
    }

    /// <summary>
    /// Получает следующий шаг для отображения при осмотре.
    /// </summary>
    private ConstructionGraphStep? GetNextStep(Entity<ScpRepairableComponent> ent)
    {
        if (!TryGetNode(ent, out var node))
            return null;

        // Если мы на ребре, получаем текущий шаг
        if (ent.Comp.EdgeIndex.HasValue && ent.Comp.EdgeIndex.Value < node.Edges.Count)
        {
            var edge = node.Edges[ent.Comp.EdgeIndex.Value];
            if (ent.Comp.StepIndex < edge.Steps.Count)
            {
                return edge.Steps[ent.Comp.StepIndex];
            }
        }

        // Если мы не на ребре, получаем первый шаг первого ребра
        if (node.Edges.Count > 0)
        {
            var firstEdge = node.Edges[0];
            if (firstEdge.Steps.Count > 0)
            {
                return firstEdge.Steps[0];
            }
        }

        return null;
    }

    #region Repair Methods

    /// <summary>
    /// Проверяет возможность ремонта и начинает его выполнение.
    /// </summary>
    public bool TryRepair(Entity<ScpRepairableComponent> ent, EntityUid user, EntityUid used)
    {
        // Инициализируем состояние ремонта, если его еще нет
        if (string.IsNullOrEmpty(ent.Comp.StartNode) && !InitializeRepairState(ent.Comp))
            return false;

        if (!CanRepair(ent, user))
            return false;

        return Repair(ent, user, used);
    }

    /// <summary>
    /// Проверяет возможность ремонта сущности.
    /// </summary>
    public bool CanRepair(Entity<ScpRepairableComponent> ent, EntityUid user)
    {
        return _interaction.InRangeUnobstructed(user, ent.Owner) &&
            CheckRepairConditions(ent, user, ent.Comp) &&
            CanRepairGraph(ent);
    }

    /// <summary>
    /// Выполняет ремонт сущности.
    /// </summary>
    private bool Repair(Entity<ScpRepairableComponent> ent, EntityUid user, EntityUid used)
    {
        if (!CanRepairStep(ent, used))
            return false;

        return DoRepairStep(ent, used, user);
    }

    /// <summary>
    /// Проверяет возможность ремонта на уровне графа.
    /// </summary>
    private bool CanRepairGraph(Entity<ScpRepairableComponent> ent)
    {
        if (!TryGetNode(ent, out _))
            return false;

        return true;
    }

    /// <summary>
    /// Проверяет возможность выполнения текущего шага ремонта.
    /// </summary>
    private bool CanRepairStep(Entity<ScpRepairableComponent> ent, EntityUid used)
    {
        if (!TryGetNode(ent, out var node))
            return false;

        // Если мы на ребре, проверяем ребро
        if (ent.Comp.EdgeIndex.HasValue && ent.Comp.EdgeIndex.Value < node.Edges.Count)
        {
            var edge = node.Edges[ent.Comp.EdgeIndex.Value];
            return CanRepairEdge(ent, used, edge);
        }

        // Если мы не на ребре, проверяем нод
        return CanRepairNode(ent, used, node);
    }

    /// <summary>
    /// Выполняет текущий шаг ремонта.
    /// </summary>
    private bool DoRepairStep(Entity<ScpRepairableComponent> ent, EntityUid used, EntityUid user)
    {
        if (!_prototype.Resolve(ent.Comp.Graph, out var graph))
            return false;

        if (!TryGetNode(ent, out var node))
            return false;

        // Если мы на ребре, выполняем ребро
        if (ent.Comp.EdgeIndex.HasValue && ent.Comp.EdgeIndex.Value < node.Edges.Count)
        {
            var edge = node.Edges[ent.Comp.EdgeIndex.Value];
            return DoRepairEdge(ent, used, user, edge, graph);
        }

        // Если мы не на ребре, выполняем нод
        return DoRepairNode(ent, used, user, node);
    }

    /// <summary>
    /// Проверяет возможность обработки нода.
    /// </summary>
    private bool CanRepairNode(Entity<ScpRepairableComponent> ent, EntityUid used, ConstructionGraphNode node)
    {
        // Сбрасываем индекс шага
        var cachedIndex = ent.Comp.StepIndex;
        ent.Comp.StepIndex = 0;

        // Проверяем все ребра нода
        foreach (var edge in node.Edges)
        {
            if (!CanRepairEdge(ent, used, edge))
                continue;

            // Если находим первый результат - считаем, что сущность можно отремонтировать
            ent.Comp.StepIndex = cachedIndex;
            return true;
        }

        ent.Comp.StepIndex = cachedIndex;
        return false;
    }

    /// <summary>
    /// Выполняет обработку нода.
    /// </summary>
    private bool DoRepairNode(Entity<ScpRepairableComponent> ent, EntityUid used, EntityUid user, ConstructionGraphNode node)
    {
        // Сбрасываем индекс шага
        ent.Comp.StepIndex = 0;

        if (!_prototype.Resolve(ent.Comp.Graph, out var graph))
            return false;

        // Проверяем все ребра нода
        for (var i = 0; i < node.Edges.Count; i++)
        {
            var edge = node.Edges[i];

            if (!CanRepairEdge(ent, used, edge))
                continue;

            ent.Comp.EdgeIndex = i;
            return DoRepairEdge(ent, used, user, edge, graph);
        }

        return false;
    }

    /// <summary>
    /// Проверяет возможность обработки ребра.
    /// </summary>
    private bool CanRepairEdge(Entity<ScpRepairableComponent> ent, EntityUid used, ConstructionGraphEdge edge)
    {
        if (!_construction.CheckConditions(ent, edge.Conditions))
            return false;

        var step = _construction.GetStepFromEdge(edge, ent.Comp.StepIndex);
        if (step == null)
            return false;

        return CanRepairStepAction(used, step);
    }

    /// <summary>
    /// Выполняет обработку ребра.
    /// </summary>
    private bool DoRepairEdge(Entity<ScpRepairableComponent> ent, EntityUid used, EntityUid user, ConstructionGraphEdge edge, ConstructionGraphPrototype graph)
    {
        if (!_construction.CheckConditions(ent, edge.Conditions))
            return false;

        var step = _construction.GetStepFromEdge(edge, ent.Comp.StepIndex);
        if (step == null)
            return false;

        if (!DoRepairStepAction(ent, used, user, step))
            return false;

        return true;
    }

    /// <summary>
    /// Проверяет возможность выполнения действия шага.
    /// </summary>
    private bool CanRepairStepAction(EntityUid used, ConstructionGraphStep step)
    {
        switch (step)
        {
            case EntityInsertConstructionGraphStep insertStep:
                if (!insertStep.EntityValid(used, EntityManager, Factory))
                    return false;

                if (HasComp<UnremoveableComponent>(used))
                    return false;

                return true;

            case ToolConstructionGraphStep toolInsertStep:
                return _tool.HasQuality(used, toolInsertStep.Tool);

            default:
                throw new NotImplementedException($"Step logic for {step.GetType()} does not implemented");
        }
    }

    /// <summary>
    /// Завершает DoAfter действие ремонта.
    /// </summary>
    private void CompleteRepairDoAfter(Entity<ScpRepairableComponent> ent, EntityUid user, EntityUid used)
    {
        if (!_prototype.TryIndex(ent.Comp.Graph, out var graph))
            return;

        if (!TryGetNode(ent, out var node))
            return;

        if (ent.Comp.EdgeIndex.HasValue && ent.Comp.EdgeIndex.Value < node.Edges.Count)
        {
            var edge = node.Edges[ent.Comp.EdgeIndex.Value];
            CompleteRepairEdgeDoAfter(ent, user, used, edge, graph);
        }
    }

    /// <summary>
    /// Завершает DoAfter действие для ребра.
    /// </summary>
    private void CompleteRepairEdgeDoAfter(Entity<ScpRepairableComponent> ent, EntityUid user, EntityUid used, ConstructionGraphEdge edge, ConstructionGraphPrototype graph)
    {
        var step = _construction.GetStepFromEdge(edge, ent.Comp.StepIndex);
        if (step == null)
            return;

        switch (step)
        {
            case EntityInsertConstructionGraphStep insertStep:

                if (insertStep is MaterialConstructionGraphStep materialInsertStep)
                {
                    var stack = _stack.Split(used, materialInsertStep.Amount, Transform(user).Coordinates);
                    if (!stack.HasValue)
                        return;

                    used = stack.Value;
                }

                if (!string.IsNullOrEmpty(insertStep.Store))
                    _container.Insert(used, _container.EnsureContainer<Container>(ent, insertStep.Store));
                else
                    Del(used);

                break;

            case ToolConstructionGraphStep:
                // Для инструментов DoAfter уже завершен в UseTool
                break;
        }

        // Выполняем действия завершения шага
        _construction.PerformActions(ent, user, step.Completed);

        // Переходим к следующему шагу
        ent.Comp.StepIndex++;

        // Проверяем, завершено ли ребро
        if (ent.Comp.StepIndex >= edge.Steps.Count)
        {
            // Ребро завершено - выполняем действия завершения ребра
            _construction.PerformActions(ent, user, edge.Completed);

            if (Deleted(ent))
                return;

            // Переходим к следующему ноду
            var nextNode = edge.Target;
            var nextNodeObj = _construction.GetNodeFromGraph(graph, nextNode);

            if (nextNodeObj != null)
            {
                // Выполняем действия нового нода
                _construction.PerformActions(ent, user, nextNodeObj.Actions);

                if (nextNodeObj.Edges.Count == 0)
                {
                    ResetToStart(ent.Comp);
                }
                else
                {
                    ent.Comp.CurrentNode = nextNode;
                    ent.Comp.EdgeIndex = null;
                    ent.Comp.StepIndex = 0;
                }
            }
        }
    }

    /// <summary>
    /// Выполняет действие шага.
    /// </summary>
    private bool DoRepairStepAction(Entity<ScpRepairableComponent> ent, EntityUid used, EntityUid user, ConstructionGraphStep step)
    {
        switch (step)
        {
            case EntityInsertConstructionGraphStep insertStep:
                if (!insertStep.EntityValid(used, EntityManager, Factory))
                    return false;

                if (HasComp<UnremoveableComponent>(used))
                    return false;

                var doAfterEv = new ConstructionInteractDoAfterEvent(GetNetCoordinates(Transform(ent).Coordinates));
                var doAfterArgs = new DoAfterArgs(EntityManager, user, step.DoAfter, doAfterEv, ent, ent, used)
                {
                    BreakOnDamage = true,
                    BreakOnMove = true,
                    NeedHand = true,
                };

                return _doAfter.TryStartDoAfter(doAfterArgs);

            case ToolConstructionGraphStep toolInsertStep:
                var result = _tool.UseTool(
                    used,
                    user,
                    ent,
                    TimeSpan.FromSeconds(toolInsertStep.DoAfter),
                    [ toolInsertStep.Tool ],
                    new ConstructionInteractDoAfterEvent(GetNetCoordinates(Transform(ent).Coordinates)),
                    out _,
                    toolInsertStep.Fuel);

                return result;

            default:
                throw new NotImplementedException($"Step logic for {step.GetType()} does not implemented");
        }
    }

    private bool TryGetNode(Entity<ScpRepairableComponent> ent, [NotNullWhen(true)] out ConstructionGraphNode? node)
    {
        node = null;
        if (!_prototype.Resolve(ent.Comp.Graph, out var graph))
            return false;

        var currentNode = ent.Comp.CurrentNode ?? ent.Comp.StartNode;
        if (string.IsNullOrEmpty(currentNode))
            return false;

        node = _construction.GetNodeFromGraph(graph, currentNode);
        if (node == null)
            return false;

        return true;
    }

    #endregion
}

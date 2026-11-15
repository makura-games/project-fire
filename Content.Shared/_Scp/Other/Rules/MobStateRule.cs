using Content.Shared.EntityConditions;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Other.Rules;

public sealed partial class ScpMobStateCondition : EntityConditionBase<ScpMobStateCondition>
{
    [DataField] public HashSet<MobState> AllowedStates = [];
    public override string EntityConditionGuidebookText(IPrototypeManager prototype) => string.Empty;
}

public sealed partial class ScpMobStateConditionSystem : EntityConditionSystem<MobStateComponent, ScpMobStateCondition>
{
    protected override void Condition(Entity<MobStateComponent> entity, ref EntityConditionEvent<ScpMobStateCondition> args)
        => args.Result = args.Condition.AllowedStates.Contains(entity.Comp.CurrentState);
}

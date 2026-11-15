using Content.Shared.EntityConditions;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Other.Rules;

public sealed partial class AlwaysFalseCondition : EntityConditionBase<AlwaysFalseCondition>
{
    public override string EntityConditionGuidebookText(IPrototypeManager prototype) => string.Empty;
}

public sealed partial class AlwaysFalseConditionSystem : EntityConditionSystem<MetaDataComponent, AlwaysFalseCondition>
{
    protected override void Condition(Entity<MetaDataComponent> entity, ref EntityConditionEvent<AlwaysFalseCondition> args) { }
}

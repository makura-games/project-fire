using Content.Shared._Scp.Other.ClassDAppearance;
using Content.Shared.EntityConditions;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Other.Rules;

public sealed partial class IsClassDCondition : EntityConditionBase<IsClassDCondition>
{
    public override string EntityConditionGuidebookText(IPrototypeManager prototype) => string.Empty;
}

public sealed partial class IsClassDConditionSystem : EntityConditionSystem<ClassDAppearanceComponent, IsClassDCondition>
{
    protected override void Condition(Entity<ClassDAppearanceComponent> entity, ref EntityConditionEvent<IsClassDCondition> args)
        => args.Result = true;
}

using Content.Shared._Scp.Mobs.Components;
using Content.Shared.EntityConditions;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.EntityConditions;

public sealed partial class ScpEntityConditionSystem : EntityConditionSystem<ScpComponent, ScpCondition>
{
    protected override void Condition(Entity<ScpComponent> entity, ref EntityConditionEvent<ScpCondition> args)
    {
        args.Result = true;
    }
}

public sealed partial class ScpCondition : EntityConditionBase<ScpCondition>
{
    public override string EntityConditionGuidebookText(IPrototypeManager prototype) =>
        Loc.GetString("entity-condition-guidebook-scp", ("invert", Inverted));
}

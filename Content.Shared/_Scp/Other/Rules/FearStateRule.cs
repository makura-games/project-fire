using Content.Shared._Scp.Fear;
using Content.Shared._Scp.Fear.Components;
using Content.Shared.EntityConditions;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Other.Rules;

public sealed partial class FearStateCondition : EntityConditionBase<FearStateCondition>
{
    [DataField] public FearState RequiredState;
    [DataField] public bool CanBeGreater;
    public override string EntityConditionGuidebookText(IPrototypeManager prototype) => string.Empty;
}

public sealed partial class FearStateConditionSystem : EntityConditionSystem<FearComponent, FearStateCondition>
{
    protected override void Condition(Entity<FearComponent> entity, ref EntityConditionEvent<FearStateCondition> args)
    {
        args.Result = args.Condition.CanBeGreater
            ? entity.Comp.State >= args.Condition.RequiredState
            : entity.Comp.State == args.Condition.RequiredState;
    }
}

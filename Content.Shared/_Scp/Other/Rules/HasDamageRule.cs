using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.EntityConditions;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Other.Rules;

public sealed partial class HasDamageCondition : EntityConditionBase<HasDamageCondition>
{
    [DataField] public bool Require;
    public override string EntityConditionGuidebookText(IPrototypeManager prototype) => string.Empty;
}

public sealed partial class HasDamageConditionSystem : EntityConditionSystem<MetaDataComponent, HasDamageCondition>
{
    protected override void Condition(Entity<MetaDataComponent> entity, ref EntityConditionEvent<HasDamageCondition> args)
    {
        var hasDamage = TryComp(entity.Owner, out DamageableComponent? damageable)
            && EntityManager.System<DamageableSystem>().GetTotalDamage((entity.Owner, damageable)) != FixedPoint2.Zero;
        args.Result = args.Condition.Require == hasDamage;
    }
}

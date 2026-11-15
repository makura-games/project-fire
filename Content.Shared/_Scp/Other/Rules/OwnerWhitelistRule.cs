using Content.Shared.EntityConditions;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Other.Rules;

public sealed partial class OwnerWhitelistCondition : EntityConditionBase<OwnerWhitelistCondition>
{
    [DataField] public EntityWhitelist? Whitelist;
    [DataField] public EntityWhitelist? Blacklist;
    public override string EntityConditionGuidebookText(IPrototypeManager prototype) => string.Empty;
}

public sealed partial class OwnerWhitelistConditionSystem : EntityConditionSystem<MetaDataComponent, OwnerWhitelistCondition>
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;

    protected override void Condition(Entity<MetaDataComponent> entity, ref EntityConditionEvent<OwnerWhitelistCondition> args)
        => args.Result = _whitelist.CheckBoth(entity, args.Condition.Blacklist, args.Condition.Whitelist);
}

using Content.Shared.EntityConditions;
using Content.Shared.Station.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Other.Rules;

public sealed partial class StationHasEntityCondition : EntityConditionBase<StationHasEntityCondition>
{
    [DataField] public EntityWhitelist? Whitelist;
    [DataField] public EntityWhitelist? Blacklist;
    public override string EntityConditionGuidebookText(IPrototypeManager prototype) => string.Empty;
}

public sealed partial class StationHasEntityConditionSystem : EntityConditionSystem<StationDataComponent, StationHasEntityCondition>
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    private EntityQuery<TransformComponent> _xformQuery;
    private readonly HashSet<Entity<TransformComponent>> _entities = [];

    public override void Initialize()
    {
        base.Initialize();
        _xformQuery = GetEntityQuery<TransformComponent>();
    }

    protected override void Condition(Entity<StationDataComponent> entity, ref EntityConditionEvent<StationHasEntityCondition> args)
    {
        foreach (var gridUid in entity.Comp.Grids)
        {
            if (!_xformQuery.TryGetComponent(gridUid, out var xform))
                continue;

            _entities.Clear();
            _lookup.GetEntitiesOnMap(xform.MapID, _entities);
            foreach (var candidate in _entities)
            {
                if (_whitelist.CheckBoth(candidate, args.Condition.Blacklist, args.Condition.Whitelist))
                {
                    args.Result = true;
                    return;
                }
            }
        }
    }
}

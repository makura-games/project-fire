using Content.Server._Scp.MetaGarbage;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;

namespace Content.Server._Scp.GameTicking.Rules.MetaGarbageSpawn;

public sealed partial class MetaGarbageSpawnRule : GameRuleSystem<MetaGarbageSpawnRuleComponent>
{
    [Dependency] private MetaGarbageSystem _metaGarbage = default!;

    protected override void Started(EntityUid uid,
        MetaGarbageSpawnRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (!TryGetRandomStation(out var targetStation))
        {
            ForceEndSelf(uid, gameRule);
            return;
        }

        if (_metaGarbage.TrySpawnGarbage(targetStation.Value))
            GameTicker.StartGameRule(component.SuccessfullySpawnedDocumentRule);
        else
            GameTicker.StartGameRule(component.FailSpawnedDocumentRule);

        ForceEndSelf(uid, gameRule);
    }
}

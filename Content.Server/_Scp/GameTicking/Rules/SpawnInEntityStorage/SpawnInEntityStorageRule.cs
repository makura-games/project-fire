using Content.Server.Station.Systems;
using Content.Server.StationEvents.Events;
using Content.Server.Storage.EntitySystems;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.EntityConditions;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Scp.GameTicking.Rules.SpawnInEntityStorage;

public sealed partial class SpawnInEntityStorageRule : StationEventSystem<SpawnInEntityStorageRuleComponent>
{
    [Dependency] private StationSystem _station = default!;
    [Dependency] private EntityStorageSystem _storage = default!;
    [Dependency] private SharedEntityConditionsSystem _conditions = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly List<PendingClose> _pendingStorageClosing = [];

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _pendingStorageClosing.Clear());
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        for (var i = _pendingStorageClosing.Count - 1; i >= 0; i--)
        {
            var time = _pendingStorageClosing[i].Time;
            if (_timing.CurTime < time)
                continue;

            var ent = _pendingStorageClosing[i].Entity;
            if (Exists(ent))
                _storage.CloseStorage(ent.AsNullable());

            _pendingStorageClosing.RemoveAt(i);
        }
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _pendingStorageClosing.Clear();
    }

    protected override void Started(EntityUid uid,
        SpawnInEntityStorageRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (!TryGetRandomStation(out var station))
            return;

        if (!_conditions.TryConditions(station.Value, component.StationConditions))
        {
            Log.Info($"Skipped {Prototype(uid)} because station conditions failed. Event aborted");
            return;
        }

        var query = EntityQueryEnumerator<EntityStorageComponent, TransformComponent>();
        while (query.MoveNext(out var storage, out var storageComp, out var xform))
        {
            if (!RobustRandom.Prob(component.Probability))
                continue;

            var storageStation = _station.GetOwningStation(storage, xform);
            if (station != storageStation)
                continue;

            var ents = EntitySpawnCollection.GetSpawns(component.Entities, RobustRandom);
            foreach (var spawn in ents)
            {
                var spawned = Spawn(spawn);
                _storage.Insert(spawned, storage, storageComp);

                Log.Debug($"Spawned {ToPrettyString(spawned)} in {ToPrettyString(storage)}");
            }

            if (component.DoOpenCloseAnimation)
                OpenStorage(storage, storageComp, component);
        }
    }

    private void OpenStorage(EntityUid storage, EntityStorageComponent component, SpawnInEntityStorageRuleComponent rule)
    {
        _storage.OpenStorage((storage, component));
        if (!_storage.IsOpen(storage, component))
            return;

        var variation = RobustRandom.Next(-rule.CloseAfterVariation, rule.CloseAfterVariation);
        var closeAfter = rule.CloseAfter + variation;

        var toAdd = new PendingClose((storage, component), _timing.CurTime + closeAfter);
        _pendingStorageClosing.Add(toAdd);
    }

    private readonly record struct PendingClose(Entity<EntityStorageComponent> Entity, TimeSpan Time);
}


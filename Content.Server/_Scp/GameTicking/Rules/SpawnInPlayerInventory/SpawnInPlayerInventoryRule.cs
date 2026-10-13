using Content.Server.Station.Systems;
using Content.Server.StationEvents.Events;
using Content.Server.Storage.EntitySystems;
using Content.Shared.GameTicking.Components;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Random.Rules;
using Content.Shared.Storage;
using Robust.Server.Audio;
using Robust.Server.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Scp.GameTicking.Rules.SpawnInPlayerInventory;

public sealed partial class SpawnInPlayerInventoryRule : StationEventSystem<SpawnInPlayerInventoryRuleComponent>
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private ContainerSystem _container = default!;
    [Dependency] private StorageSystem _storage = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private RulesSystem _rules = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    private const string Pocket1Slot = "pocket1";
    private const string Pocket2Slot = "pocket2";

    private const float SpawnInInventoryStorageChance = 0.5f;

    protected override void Started(EntityUid uid,
        SpawnInPlayerInventoryRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (!TryGetRandomStation(out var station))
            return;

        if (_prototype.TryIndex(component.StationRules, out var stationRule) &&
            !_rules.IsTrue(station.Value, stationRule))
        {
            Log.Info($"Skipped {Prototype(uid)} due to {component.StationRules} fails! Event aborted");
            return;
        }

        var query = EntityQueryEnumerator<HumanoidProfileComponent, InventoryComponent, TransformComponent>();
        while (query.MoveNext(out var target, out _, out var inventory, out var xform))
        {
            if (!RobustRandom.Prob(component.Probability))
                continue;

            var targetStation = _station.GetOwningStation(target, xform);
            if (targetStation != station)
                continue;

            var ents = EntitySpawnCollection.GetSpawns(component.Entities, RobustRandom);
            var spawnedAny = false;
            foreach (var toSpawn in ents)
            {
                var spawned = Spawn(toSpawn);
                if (!SpawnInPlayerInventory((target, inventory), spawned))
                {
                    QueueDel(spawned);
                    continue;
                }

                spawnedAny = true;
                Log.Debug($"Spawned {ToPrettyString(spawned)} in {ToPrettyString(target)} inventory");
            }

            if (spawnedAny)
                _audio.PlayGlobal(component.Sound, target);
        }
    }

    private bool SpawnInPlayerInventory(Entity<InventoryComponent> target, EntityUid spawned)
    {
        // Случайный приоритет спавна
        // Если true -> сначала спавним в хранилищах в инвентаре, а потом в карманах
        // Если false -> наоборот
        if (RobustRandom.Prob(SpawnInInventoryStorageChance))
            return SpawnInInventoryStorage(target, spawned) || SpawnInPockets(target, spawned);
        else
            return SpawnInPockets(target, spawned) || SpawnInInventoryStorage(target, spawned);
    }

    private bool SpawnInInventoryStorage(Entity<InventoryComponent> target, EntityUid spawned)
    {
        if (!_inventory.TryGetContainerSlotEnumerator(target.AsNullable(), out var enumerator))
            return false;

        while (enumerator.MoveNext(out var slot))
        {
            if (!_inventory.TryGetSlotEntity(target, slot.ID, out var item, target.Comp))
                continue;

            if (!_storage.CanInsert(item.Value, spawned, out _))
                continue;

            if (_storage.Insert(item.Value, spawned, out _, playSound: false))
                return true;
        }

        return false;
    }

    private bool SpawnInPockets(Entity<InventoryComponent> target, EntityUid spawned)
    {
        if (_inventory.TryGetSlotContainer(target, Pocket1Slot, out var pocket1, out _, target.Comp)
            && _container.Insert(spawned, pocket1))
            return true;

        if (_inventory.TryGetSlotContainer(target, Pocket2Slot, out var pocket2, out _, target.Comp)
            && _container.Insert(spawned, pocket2))
            return true;

        return false;
    }
}

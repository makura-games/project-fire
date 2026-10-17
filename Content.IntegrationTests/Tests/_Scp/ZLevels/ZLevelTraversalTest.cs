using System.Collections.Generic;
using System.Numerics;
using Content.Server._CE.ZLevels.Core;
using Content.Server._CE.ZLevels.LaddersCache;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Ghost;
using Content.Shared._CE.ZLevels.Mapping.Prototypes;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Toggleable;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Scp.ZLevels;

[TestFixture]
public sealed class ZLevelTraversalTest
{
    private static readonly ProtoId<CEZLevelMapPrototype> DebugMap = "CEZLevelsDebug";

    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ScpZTraversalTestSubject
          components:
          - type: CEZPhysics
            disabled: true
          - type: CEZLevelGhostMover
          - type: CEZLevelViewer

        - type: entity
          id: ScpZFallTestSubject
          components:
          - type: Physics
            bodyType: Dynamic
          - type: Fixtures
            fixtures:
              test:
                shape: !type:PhysShapeCircle
                  radius: 0.2
                density: 1
          - type: CEZPhysics
            bounciness: 0
            timeToSleep: 0.1
        """;

    [Test]
    public async Task DebugMapsInitializeStairsRoofsAndReplicateNetwork()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = server.System<SharedMapSystem>();
        var zLevels = server.System<CEZLevelsSystem>();
        var loader = server.System<MapLoaderSystem>();
        var maps = new List<Entity<MapComponent>>();
        Entity<CEZMapNetworkComponent> network = default;
        bool added = default;

        await server.WaitPost(() =>
        {
            var prototype = server.ProtoMan.Index(DebugMap);
            network = zLevels.CreateMapNetwork(prototype.Components);
            var depths = new Dictionary<EntityUid, int>();
            foreach (var path in prototype.Maps)
            {
                if (!loader.TryLoadMap(path, out var loadedMap, out _))
                    continue;

                depths.Add(loadedMap.Value, maps.Count);
                maps.Add(loadedMap.Value);
            }

            added = zLevels.TryAddMapsIntoNetwork(network, depths);
            zLevels.InitializeZNetwork(network);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(maps, Has.Count.EqualTo(2));
            var lower = maps[0].Owner;
            var upper = maps[1].Owner;
            var lowerGrid = server.EntMan.GetComponent<MapGridComponent>(lower);
            var upperGrid = server.EntMan.GetComponent<MapGridComponent>(upper);
            var lowerRoof = server.EntMan.GetComponent<RoofComponent>(lower);
            var roof = server.System<SharedRoofSystem>();
            var cache = server.EntMan.GetComponent<CEZLevelsLaddersCacheComponent>(lower);
            Assert.Multiple(() =>
            {
                Assert.That(added, Is.True);
                Assert.That(network.Comp.SortedZLevels, Is.EqualTo(new[] { lower, upper }));
                Assert.That(map.IsInitialized(lower), Is.True);
                Assert.That(map.IsInitialized(upper), Is.True);
                Assert.That(map.GetTileRef(upper, upperGrid, new Vector2i(3, 3)).Tile.IsEmpty, Is.True);
                Assert.That(map.GetTileRef(upper, upperGrid, Vector2i.Zero).Tile.IsEmpty, Is.False);
                Assert.That(cache.Slopes.ContainsKey(new Vector2i(3, 3)), Is.True);
                Assert.That(roof.IsRooved((lower, lowerGrid, lowerRoof), Vector2i.Zero), Is.True);
                Assert.That(roof.IsRooved((lower, lowerGrid, lowerRoof), new Vector2i(3, 3)), Is.False);
            });
        });

        await pair.RunTicksSync(10);
        await client.WaitAssertion(() =>
        {
            var replicated = client.EntMan.GetComponent<CEZMapNetworkComponent>(pair.ToClientUid(network));
            Assert.That(replicated.SortedMin, Is.EqualTo(0));
            Assert.That(replicated.SortedMax, Is.EqualTo(1));
            Assert.That(replicated.SortedZLevels, Is.EqualTo(new[]
            {
                pair.ToClientUid(maps[0]), pair.ToClientUid(maps[1]),
            }));
        });

        await server.WaitPost(() =>
        {
            foreach (var loadedMap in maps)
                map.DeleteMap(loadedMap.Comp.MapId);
            server.EntMan.DeleteEntity(network);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MixedDepthNetworkMaintainsNeighborsAndTraversalAfterRemoval()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = server.System<SharedMapSystem>();
        var transform = server.System<SharedTransformSystem>();
        var zLevels = server.System<CEZLevelsSystem>();
        Entity<CEZMapNetworkComponent> network = default;
        EntityUid lowerMap = default;
        EntityUid middleMap = default;
        EntityUid upperMap = default;
        EntityUid subject = default;
        MapId lowerMapId = default;
        MapId middleMapId = default;
        MapId upperMapId = default;
        var position = new Vector2(6.25f, -4.75f);
        var rotation = Angle.FromDegrees(37);
        bool added = default;
        bool moved = default;
        bool removed = default;

        await server.WaitPost(() =>
        {
            lowerMap = map.CreateMap(out lowerMapId);
            middleMap = map.CreateMap(out middleMapId);
            upperMap = map.CreateMap(out upperMapId);
            network = zLevels.CreateMapNetwork();
            added = zLevels.TryAddMapsIntoNetwork(network, new Dictionary<EntityUid, int>
            {
                [upperMap] = 1,
                [lowerMap] = -1,
                [middleMap] = 0,
            });
            subject = server.EntMan.SpawnEntity("ScpZTraversalTestSubject", new MapCoordinates(position, middleMapId));
            transform.SetWorldRotation(subject, rotation);
        });

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(added, Is.True);
                Assert.That(network.Comp.SortedZLevels, Is.EqualTo(new[] { lowerMap, middleMap, upperMap }));
                Assert.That(network.Comp.SortedMin, Is.EqualTo(-1));
                Assert.That(network.Comp.SortedMax, Is.EqualTo(1));
                Assert.That(server.EntMan.GetComponent<CEZMapComponent>(lowerMap).MapAbove, Is.EqualTo(middleMap));
                Assert.That(server.EntMan.GetComponent<CEZMapComponent>(upperMap).MapBelow, Is.EqualTo(middleMap));
                Assert.That(zLevels.TryGetZLevelOffset(lowerMap, upperMap, out var offset), Is.True);
                Assert.That(offset, Is.EqualTo(2));
            });
        });

        await server.WaitPost(() => moved = zLevels.TryMoveUp(subject));
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(moved, Is.True);
                Assert.That(server.EntMan.GetComponent<TransformComponent>(subject).MapUid, Is.EqualTo(upperMap));
                Assert.That(server.EntMan.GetComponent<CEZPhysicsComponent>(subject).CurrentZLevel, Is.EqualTo(1));
                Assert.That(transform.GetWorldPosition(subject), Is.EqualTo(position));
                Assert.That(transform.GetWorldRotation(subject), Is.EqualTo(rotation));
            });
        });

        await server.WaitPost(() => moved = zLevels.TryMoveDown(subject) && zLevels.TryMoveDown(subject));
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(moved, Is.True);
                Assert.That(server.EntMan.GetComponent<TransformComponent>(subject).MapUid, Is.EqualTo(lowerMap));
                Assert.That(server.EntMan.GetComponent<CEZPhysicsComponent>(subject).CurrentZLevel, Is.EqualTo(-1));
                Assert.That(transform.GetWorldPosition(subject), Is.EqualTo(position));
                Assert.That(transform.GetWorldRotation(subject), Is.EqualTo(rotation));
            });
        });

        await server.WaitPost(() => removed = zLevels.TryRemoveMapsFromNetwork(network, new[] { middleMap }));
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(removed, Is.True);
                Assert.That(network.Comp.SortedZLevels, Is.EqualTo(new[] { lowerMap, EntityUid.Invalid, upperMap }));
                Assert.That(zLevels.TryMapUp(lowerMap, out _), Is.False);
                Assert.That(zLevels.TryMapDown(upperMap, out _), Is.False);
                Assert.That(server.EntMan.HasComponent<CEZMapComponent>(middleMap), Is.False);
            });
        });

        await server.WaitPost(() =>
        {
            map.DeleteMap(lowerMapId);
            map.DeleteMap(middleMapId);
            map.DeleteMap(upperMapId);
            server.EntMan.DeleteEntity(network);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RemovingFloorWakesSleepingBodyAndLandsOnLowerLevel()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = server.System<SharedMapSystem>();
        var zLevels = server.System<CEZLevelsSystem>();
        Entity<CEZMapNetworkComponent> network = default;
        Entity<MapGridComponent> upperGrid = default;
        EntityUid lowerMap = default;
        EntityUid upperMap = default;
        EntityUid subject = default;
        MapId lowerMapId = default;
        MapId upperMapId = default;
        var position = new Vector2(0.5f, 0.5f);

        await server.WaitPost(() =>
        {
            lowerMap = map.CreateMap(out lowerMapId);
            upperMap = map.CreateMap(out upperMapId);
            var lowerGrid = server.MapMan.CreateGridEntity(lowerMapId);
            upperGrid = server.MapMan.CreateGridEntity(upperMapId);
            map.SetTile(lowerGrid, lowerGrid.Comp, Vector2i.Zero, new Tile(1));
            map.SetTile(upperGrid, upperGrid.Comp, Vector2i.Zero, new Tile(1));
            map.SetTile(upperGrid, upperGrid.Comp, new Vector2i(1, 0), new Tile(1));
            network = zLevels.CreateMapNetwork();
            zLevels.TryAddMapsIntoNetwork(network, new Dictionary<EntityUid, int>
            {
                [lowerMap] = 0,
                [upperMap] = 1,
            });
            subject = server.EntMan.SpawnEntity("ScpZFallTestSubject", new MapCoordinates(position, upperMapId));
            server.EntMan.GetComponent<TransformComponent>(subject).GridTraversal = false;
            server.System<SharedTransformSystem>().SetCoordinates(subject, new EntityCoordinates(upperMap, position));
        });

        await pair.RunSeconds(0.3f);
        await server.WaitAssertion(() =>
        {
            var physics = server.EntMan.GetComponent<CEZPhysicsComponent>(subject);
            Assert.That(physics.Sleeping, Is.True, "The body must be asleep before its supporting floor is removed.");
            Assert.That(physics.LocalPosition, Is.EqualTo(0f).Within(0.01f));
            Assert.That(server.EntMan.GetComponent<TransformComponent>(subject).ParentUid, Is.EqualTo(upperMap));
        });

        await server.WaitPost(() => map.SetTile(upperGrid, upperGrid.Comp, Vector2i.Zero, Tile.Empty));
        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<TransformComponent>(subject).ParentUid, Is.EqualTo(upperMap));
            Assert.That(server.EntMan.GetComponent<CEZPhysicsComponent>(subject).CachedGroundHeight, Is.EqualTo(-1f));
        });
        await pair.RunSeconds(1f);

        await server.WaitAssertion(() =>
        {
            var physics = server.EntMan.GetComponent<CEZPhysicsComponent>(subject);
            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.GetComponent<TransformComponent>(subject).MapUid, Is.EqualTo(lowerMap));
                Assert.That(physics.CurrentZLevel, Is.EqualTo(0));
                Assert.That(physics.LocalPosition, Is.EqualTo(0f).Within(0.01f));
                Assert.That(physics.Velocity, Is.EqualTo(0f).Within(0.01f));
                Assert.That(physics.Sleeping, Is.True);
                Assert.That(server.System<SharedTransformSystem>().GetWorldPosition(subject), Is.EqualTo(position));
            });
        });

        await server.WaitPost(() =>
        {
            map.DeleteMap(lowerMapId);
            map.DeleteMap(upperMapId);
            server.EntMan.DeleteEntity(network);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GhostActionsTraverseAndViewerActionTogglesLookUp()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = server.System<SharedMapSystem>();
        var zLevels = server.System<CEZLevelsSystem>();
        var actions = server.System<SharedActionsSystem>();
        Entity<CEZMapNetworkComponent> network = default;
        EntityUid lowerMap = default;
        EntityUid upperMap = default;
        EntityUid subject = default;
        MapId lowerMapId = default;
        MapId upperMapId = default;
        Entity<ActionComponent>? upAction = default;
        Entity<ActionComponent>? downAction = default;
        Entity<ActionComponent>? lookAction = default;
        var up = new CEZLevelActionUp();
        var down = new CEZLevelActionDown();
        var toggle = new ToggleActionEvent();

        await server.WaitPost(() =>
        {
            lowerMap = map.CreateMap(out lowerMapId);
            upperMap = map.CreateMap(out upperMapId);
            network = zLevels.CreateMapNetwork();
            zLevels.TryAddMapsIntoNetwork(network, new Dictionary<EntityUid, int>
            {
                [lowerMap] = 0,
                [upperMap] = 1,
            });
            subject = server.EntMan.SpawnEntity("ScpZTraversalTestSubject", new MapCoordinates(Vector2.Zero, lowerMapId));
            var ghost = server.EntMan.GetComponent<CEZLevelGhostMoverComponent>(subject);
            upAction = actions.GetAction(ghost.ZLevelUpActionEntity);
            downAction = actions.GetAction(ghost.ZLevelDownActionEntity);
            lookAction = actions.GetAction(server.EntMan.GetComponent<CEZLevelViewerComponent>(subject).ActionEntity);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(upAction, Is.Not.Null);
            Assert.That(downAction, Is.Not.Null);
            Assert.That(lookAction, Is.Not.Null);
        });

        await server.WaitPost(() => actions.PerformAction(subject, upAction!.Value, up));
        await server.WaitAssertion(() =>
        {
            Assert.That(up.Handled, Is.True);
            Assert.That(server.EntMan.GetComponent<TransformComponent>(subject).MapUid, Is.EqualTo(upperMap));
        });

        await server.WaitPost(() => actions.PerformAction(subject, downAction!.Value, down));
        await server.WaitAssertion(() =>
        {
            Assert.That(down.Handled, Is.True);
            Assert.That(server.EntMan.GetComponent<TransformComponent>(subject).MapUid, Is.EqualTo(lowerMap));
        });

        await server.WaitPost(() => actions.PerformAction(subject, lookAction!.Value, toggle));
        await server.WaitAssertion(() =>
        {
            Assert.That(toggle.Handled, Is.True);
            Assert.That(server.EntMan.GetComponent<CEZLevelViewerComponent>(subject).LookUp, Is.True);
            Assert.That(lookAction!.Value.Comp.Toggled, Is.True);
        });

        await server.WaitPost(() => actions.PerformAction(subject, lookAction!.Value, toggle));
        await server.WaitAssertion(() =>
        {
            Assert.That(toggle.Handled, Is.True);
            Assert.That(server.EntMan.GetComponent<CEZLevelViewerComponent>(subject).LookUp, Is.False);
            Assert.That(lookAction!.Value.Comp.Toggled, Is.False);
        });

        await server.WaitPost(() =>
        {
            map.DeleteMap(lowerMapId);
            map.DeleteMap(upperMapId);
            server.EntMan.DeleteEntity(network);
        });
        await pair.CleanReturnAsync();
    }
}

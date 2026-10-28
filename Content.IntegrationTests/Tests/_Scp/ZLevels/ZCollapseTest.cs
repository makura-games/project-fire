using System.Collections.Generic;
using System.Numerics;
using Content.Server._CE.ZCollapse;
using Content.Server._CE.ZLevels.Core;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Tiles;
using Content.Shared.DoAfter;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Scp.ZLevels;

[TestFixture]
public sealed class ZCollapseTest
{
    [TestCase("FloorDevFloor", null, false)]
    [TestCase("Lattice", "PartRodMetal1", false)]
    [TestCase("FloorDevFloor", null, true)]
    [TestCase("Lattice", "PartRodMetal1", true)]
    public async Task TileRemovalDropsItemsWithOptionalZPhysics(string tileId, string expectedDrop, bool ceilingTool)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = server.System<SharedMapSystem>();
        var zLevels = server.System<CEZLevelsSystem>();
        var tileDefinitions = server.ResolveDependency<ITileDefinitionManager>();
        Entity<CEZMapNetworkComponent> network = default;
        Entity<MapGridComponent> upperGrid = default;
        EntityUid lowerMap = default;
        MapId lowerMapId = default;
        MapId upperMapId = default;
        var tile = new Tile(tileDefinitions[tileId].TileId);
        bool ceilingHandled = default;
        float? initialDropPosition = null;

        await server.WaitPost(() =>
        {
            lowerMap = map.CreateMap(out lowerMapId, runMapInit: false);
            var upperMap = map.CreateMap(out upperMapId, runMapInit: false);
            var lowerGrid = server.MapMan.CreateGridEntity(lowerMapId);
            upperGrid = server.MapMan.CreateGridEntity(upperMapId);
            server.EntMan.AddComponent<CEGridStabilityComponent>(lowerGrid).SupportLowestLevel = true;
            if (!ceilingTool)
                server.EntMan.AddComponent<CEGridStabilityComponent>(upperGrid);

            var permanentTile = new Tile(tileDefinitions["FloorHullReinforced"].TileId);
            for (var x = -2; x <= 2; x++)
            for (var y = -2; y <= 2; y++)
                map.SetTile(lowerGrid, lowerGrid.Comp, new Vector2i(x, y), permanentTile);

            // Несущая способность не задана: сохраняем грид соседним неразрушимым тайлом.
            map.SetTile(upperGrid, upperGrid.Comp, new Vector2i(1, 0), permanentTile);
            map.SetTile(upperGrid, upperGrid.Comp, Vector2i.Zero, tile);
            network = zLevels.CreateMapNetwork();
            zLevels.TryAddMapsIntoNetwork(network, new Dictionary<EntityUid, int>
            {
                [lowerMap] = 0,
                [upperMap] = 1,
            });
            zLevels.InitializeZNetwork(network);

            if (!ceilingTool)
                return;

            var coordinates = new EntityCoordinates(lowerGrid, new Vector2(0.5f, 0.5f));
            var user = server.EntMan.SpawnEntity(null, coordinates);
            var tool = server.EntMan.SpawnEntity(tileId == "Lattice" ? "Wirecutter" : "Crowbar", coordinates);
            server.EntMan.EnsureComponent<CEZLevelToolTileComponent>(tool);
            var completion = new CEZLevelTileToolDoAfterEvent(server.EntMan.GetNetEntity(upperGrid), Vector2i.Zero);
            completion.DoAfter = new Content.Shared.DoAfter.DoAfter(0,
                new DoAfterArgs(server.EntMan, user, 0f, completion, tool), server.Timing.CurTime);
            server.EntMan.EventBus.RaiseLocalEvent(tool, completion);
            ceilingHandled = completion.Handled;

            var drops = server.EntMan.EntityQueryEnumerator<CEZPhysicsComponent, MetaDataComponent, TransformComponent>();
            while (drops.MoveNext(out _, out var physics, out var metadata, out var transform))
            {
                if (transform.MapUid == lowerMap && metadata.EntityPrototype?.ID == expectedDrop)
                    initialDropPosition = physics.LocalPosition;
            }
        });

        // Полный срок коллапса — не более 10 секунд; оставляем запас на расчёт устойчивости.
        await pair.RunSeconds(ceilingTool ? 1f : 11f);

        await server.WaitAssertion(() =>
        {
            Assert.That(map.GetTileRef(upperGrid, upperGrid.Comp, Vector2i.Zero).Tile, Is.Not.EqualTo(tile),
                "Removing a tile must succeed even when its drop has no Z physics.");

            if (ceilingTool)
            {
                Assert.That(ceilingHandled, Is.True);
                if (expectedDrop != null)
                    Assert.That(initialDropPosition, Is.EqualTo(0.9f).Within(0.001f),
                        "Physical ceiling debris must start near the ceiling before falling.");
            }

            var foundDrop = false;
            var entities = server.EntMan.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (entities.MoveNext(out var uid, out var metadata, out var transform))
            {
                if (transform.MapUid != lowerMap)
                    continue;

                Assert.That(metadata.EntityPrototype?.ID, Is.Not.EqualTo("NothingEntity"),
                    "A self-deleting placeholder must not remain as debris.");

                if (expectedDrop == null || metadata.EntityPrototype?.ID != expectedDrop)
                    continue;

                foundDrop = true;
                Assert.That(server.EntMan.HasComponent<CEZPhysicsComponent>(uid), Is.True,
                    "Physical debris must retain Z physics on the lower level.");
            }

            if (expectedDrop != null)
                Assert.That(foundDrop, Is.True, "A collapsing lattice must still drop its metal rod below.");
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

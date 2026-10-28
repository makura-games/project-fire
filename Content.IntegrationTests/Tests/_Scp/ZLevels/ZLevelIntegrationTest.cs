using System.Collections.Generic;
using System.Numerics;
using Content.Server._CE.ZLevels.Core;
using Content.Server.Weather;
using Content.Shared.Maps;
using Robust.Shared.EntitySerialization;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Content.Server.GameTicking;

namespace Content.IntegrationTests.Tests._Scp.ZLevels;

[TestFixture]
public sealed class ZLevelIntegrationTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: gameMap
          id: ScpZLevelLoadTestVanilla
          minPlayers: 0
          mapName: ScpZLevelLoadTestVanilla
          mapPath: /Maps/Test/empty.yml
          stations:
            Station:
              stationProto: StandardNanotrasenStation
              components: []

        - type: gameMap
          id: ScpZLevelLoadTestAbove
          minPlayers: 0
          mapName: ScpZLevelLoadTestAbove
          mapPath: /Maps/Test/empty.yml
          stations:
            Station:
              stationProto: StandardNanotrasenStation
              components:
              - type: CEStationZLevels
                mapsAbove: [/Maps/Test/empty.yml]

        - type: gameMap
          id: ScpZLevelLoadTestBelow
          minPlayers: 0
          mapName: ScpZLevelLoadTestBelow
          mapPath: /Maps/Test/empty.yml
          stations:
            Station:
              stationProto: StandardNanotrasenStation
              components:
              - type: CEStationZLevels
                mapsBelow: [/Maps/Test/empty.yml]

        - type: gameMap
          id: ScpZLevelLoadTestEmpty
          minPlayers: 0
          mapName: ScpZLevelLoadTestEmpty
          mapPath: /Maps/Test/empty.yml
          stations:
            Station:
              stationProto: StandardNanotrasenStation
              components:
              - type: CEStationZLevels
        """;

    [TestCase("ScpZLevelLoadTestVanilla", false)]
    [TestCase("ScpZLevelLoadTestEmpty", false)]
    [TestCase("ScpZLevelLoadTestAbove", true)]
    [TestCase("ScpZLevelLoadTestBelow", true)]
    public async Task MapLoadAlignsOnlyStationsWithAdditionalZLevels(string prototypeId, bool aligned)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var offset = new Vector2(31f, -23f);
        var rotation = Angle.FromDegrees(73);
        PreGameMapLoad load = default!;

        await server.WaitPost(() =>
        {
            var prototype = server.ProtoMan.Index<GameMapPrototype>(prototypeId);
            load = new PreGameMapLoad(prototype, DeserializationOptions.Default, offset, rotation);
            server.EntMan.EventBus.RaiseEvent(EventSource.Local, load);
        });

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(load.Offset, Is.EqualTo(aligned ? Vector2.Zero : offset));
                Assert.That(load.Rotation, Is.EqualTo(aligned ? Angle.Zero : rotation));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WeatherRespectsZLevelCeilingsOverEmptyTilesOnMovedGrids()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        Entity<MapGridComponent> lowerGrid = default;
        EntityUid network = default;
        MapId lowerMapId = default;
        MapId upperMapId = default;
        bool covered = default;
        bool uncovered = default;

        await server.WaitPost(() =>
        {
            var map = server.System<SharedMapSystem>();
            var transform = server.System<SharedTransformSystem>();
            var zLevels = server.System<CEZLevelsSystem>();
            var weather = server.System<WeatherSystem>();
            var lowerMap = map.CreateMap(out lowerMapId);
            var upperMap = map.CreateMap(out upperMapId);
            lowerGrid = server.MapMan.CreateGridEntity(lowerMapId);
            var upperGrid = server.MapMan.CreateGridEntity(upperMapId);

            transform.SetLocalPositionRotation(lowerGrid, new Vector2(6f, -4f), Angle.FromDegrees(90));
            transform.SetLocalPosition(upperGrid, new Vector2(5f, -4f));
            map.SetTile(lowerGrid, lowerGrid.Comp, new Vector2i(-1, 0), new Tile(1));
            map.SetTile(upperGrid, upperGrid.Comp, Vector2i.Zero, new Tile(1));

            var zNetwork = zLevels.CreateMapNetwork();
            network = zNetwork;
            zLevels.TryAddMapsIntoNetwork(zNetwork, new Dictionary<EntityUid, int>
            {
                [lowerMap] = 0,
                [upperMap] = 1,
            });

            covered = weather.CanWeatherAffect((lowerGrid.Owner, lowerGrid.Comp, null),
                map.GetTileRef(lowerGrid, lowerGrid.Comp, Vector2i.Zero));
            uncovered = weather.CanWeatherAffect((lowerGrid.Owner, lowerGrid.Comp, null),
                map.GetTileRef(lowerGrid, lowerGrid.Comp, new Vector2i(0, 1)));
        });

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(covered, Is.False, "The floor above must block weather over an empty tile.");
                Assert.That(uncovered, Is.True, "An uncovered empty tile must still receive weather.");
            });
        });

        await server.WaitPost(() =>
        {
            server.System<SharedMapSystem>().DeleteMap(lowerMapId);
            server.System<SharedMapSystem>().DeleteMap(upperMapId);
            server.EntMan.DeleteEntity(network);
        });

        await pair.CleanReturnAsync();
    }
}

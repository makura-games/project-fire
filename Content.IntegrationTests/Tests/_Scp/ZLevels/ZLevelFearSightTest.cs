#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._Scp.Blinking;
using Content.Shared._Scp.Proximity;
using Content.Shared._Scp.Watching;
using Content.Shared.Mobs;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Scp.ZLevels;

[TestFixture]
public sealed class ZLevelFearSightTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ScpZLevelFearSightTestViewer
          components:
          - type: CEZLevelViewer
            lookUp: true
          - type: Blinkable
            blinkingInterval: 1000
          - type: MobState
          - type: FieldOfView
            angle: 90
            angleFeather: 0

        - type: entity
          parent: ScpZLevelFearSightTestViewer
          id: ScpZLevelFearSightTestViewerForward
          components:
          - type: CEZLevelViewer
            lookUp: false

        - type: entity
          id: ScpZLevelFearSightTestWall
          components:
          - type: Occluder
          - type: Tag
            tags: [Wall]
          - type: Physics
            bodyType: Static
          - type: Fixtures
            fixtures:
              wall:
                shape: !type:PhysShapeAabb
                  bounds: "-0.45,-0.45,0.45,0.45"
                hard: true
                layer: [Impassable]
        """;

    [TestCase(false, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    public async Task UpperFloorRequiresLookUpAndAnOpenCeiling(bool lookUp, bool roof, bool expected)
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        bool visible = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair, lookUp);
            if (!roof)
                scene.SetFloor(1, Scene.ViewerPosition, Tile.Empty);
            visible = scene.IsVisible(scene.SpawnTarget(1));
        });
        await pair.Server.WaitAssertion(() => Assert.That(visible, Is.EqualTo(expected)));
        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [TestCase(false, false, true)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    public async Task LowerTargetRequiresAnOpeningThroughEveryIntermediateFloor(
        bool transparent, bool opaqueIntermediate, bool expected)
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        bool visible = default;
        float distance = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair, false);
            scene.OpenColumn(-2, transparent);
            if (opaqueIntermediate)
                scene.SetFloor(-1, Scene.TargetPosition, scene.SolidFloor);
            visible = scene.IsVisible(scene.SpawnTarget(-2), out distance);
        });
        await pair.Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(visible, Is.EqualTo(expected));
                Assert.That(distance, Is.EqualTo(4f).Within(0.001f));
            });
        });
        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnUnrelatedOpeningDoesNotExposeACoveredTarget()
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        bool visible = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair);
            scene.SetFloor(0, Scene.ViewerPosition, Tile.Empty);
            visible = scene.IsVisible(scene.SpawnTarget(-1));
        });
        await pair.Server.WaitAssertion(() => Assert.That(visible, Is.False));
        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [TestCase(0)]
    [TestCase(-1)]
    public async Task WallOccludersOnEitherMapPreventSight(int blockerDepth)
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        EntityUid target = default;
        bool visible = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair);
            scene.OpenColumn(-1);
            target = scene.SpawnTarget(-1);
            scene.SpawnWall(blockerDepth);
        });
        // Окклюдер и fixture должны попасть в пространственные деревья перед трассировкой.
        await pair.RunTicksSync(2);
        await pair.Server.WaitPost(() => visible = scene.IsVisible(target));
        await pair.Server.WaitAssertion(() => Assert.That(visible, Is.False));
        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [TestCase(0, 4.5f, false)]
    [TestCase(2, 4.5f, false)]
    [TestCase(-6, 4.5f, false)]
    [TestCase(10, 4.5f, false)]
    [TestCase(-5, 4.5f, true)]
    [TestCase(-1, 100.5f, false)]
    [TestCase(-1, -4.5f, false)]
    [TestCase(1, -4.5f, false)]
    public async Task SightRespectsMapsDepthRangeAndTheViewCone(int depth, float targetX, bool expected)
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        bool visible = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair);
            scene.OpenColumn(-5);
            scene.SetFloor(1, Scene.ViewerPosition, Tile.Empty);
            visible = scene.IsVisible(scene.SpawnTarget(depth, new Vector2(targetX, 0.5f)));
        });
        await pair.Server.WaitAssertion(() => Assert.That(visible, Is.EqualTo(expected)));
        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [TestCase(-1, 4.5f, true)]
    [TestCase(-1, -4.5f, false)]
    [TestCase(1, 4.5f, true)]
    [TestCase(1, -4.5f, false)]
    public async Task InterfloorSourcesRespectTheViewDirectionOnEveryFloor(int depth, float sourceX, bool expected)
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        bool visible = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair);
            scene.OpenColumn(-1);
            scene.SetFloor(1, Scene.ViewerPosition, Tile.Empty);
            visible = scene.IsVisible(scene.SpawnTarget(depth, new Vector2(sourceX, 0.5f)));
        });
        await pair.Server.WaitAssertion(() => Assert.That(visible, Is.EqualTo(expected)));
        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ClosedEyesOrIncapacitationPreventInterfloorSight(bool incapacitated)
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        bool visible = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair);
            scene.OpenColumn(-1);
            if (incapacitated)
                pair.Server.System<MobStateSystem>().ChangeMobState(scene.Viewer, MobState.Critical);
            else
            {
                var blinkable = pair.Server.EntMan.GetComponent<BlinkableComponent>(scene.Viewer);
                blinkable.State = EyesState.Closed;
                blinkable.CloseMode = EyeCloseReason.Action;
            }
            visible = scene.IsVisible(scene.SpawnTarget(-1));
        });
        await pair.Server.WaitAssertion(() => Assert.That(visible, Is.False));
        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    private sealed class Scene
    {
        public static readonly Vector2 ViewerPosition = new(0.5f, 0.5f);
        public static readonly Vector2 TargetPosition = new(4.5f, 0.5f);
        private readonly TestPair _pair;
        private readonly Entity<CEZMapNetworkComponent> _network;
        private readonly Dictionary<int, MapId> _maps = new();
        private readonly Dictionary<int, Entity<MapGridComponent>> _grids = new();
        public readonly EntityUid Viewer;
        public readonly Tile SolidFloor;
        private readonly Tile _transparentFloor;
        private readonly ContentTileDefinition _glassDefinition;
        private readonly bool _originalGlassTransparency;

        public Scene(TestPair pair, bool lookUp = true)
        {
            _pair = pair;
            var server = pair.Server;
            var map = server.System<SharedMapSystem>();
            var zLevels = server.System<CEZLevelsSystem>();
            var levels = new Dictionary<EntityUid, int>();
            SolidFloor = new Tile(server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"].TileId);
            _glassDefinition = (ContentTileDefinition) server.ResolveDependency<ITileDefinitionManager>()["FloorGlass"];
            _originalGlassTransparency = _glassDefinition.Transparent;
            _glassDefinition.Transparent = true;
            _transparentFloor = new Tile(_glassDefinition.TileId);
            for (var depth = -6; depth <= 2; depth++)
            {
                var uid = map.CreateMap(out var id);
                _maps.Add(depth, id);
                levels.Add(uid, depth);
                var grid = server.MapMan.CreateGridEntity(id);
                _grids.Add(depth, grid);
                for (var x = 0; x <= 5; x++)
                    map.SetTile(grid, grid.Comp, new Vector2i(x, 0), SolidFloor);
            }
            _network = zLevels.CreateMapNetwork();
            zLevels.TryAddMapsIntoNetwork(_network, levels);
            map.CreateMap(out var unrelated);
            _maps.Add(10, unrelated);
            Viewer = server.EntMan.SpawnEntity(lookUp ? "ScpZLevelFearSightTestViewer" : "ScpZLevelFearSightTestViewerForward",
                new MapCoordinates(ViewerPosition, _maps[0]));
            server.System<SharedTransformSystem>().SetWorldRotation(Viewer, Angle.FromWorldVec(Vector2.UnitX));
            server.System<SharedBlinkingSystem>().SetNextBlink(Viewer, TimeSpan.FromMinutes(10), predicted: false);
        }

        public void OpenColumn(int targetDepth, bool transparent = false)
        {
            for (var depth = 0; depth > targetDepth; depth--)
                SetFloor(depth, TargetPosition, transparent ? _transparentFloor : Tile.Empty);
        }

        public void SetFloor(int depth, Vector2 position, Tile tile)
        {
            var grid = _grids[depth];
            var map = _pair.Server.System<SharedMapSystem>();
            var indices = map.TileIndicesFor(grid.Owner, grid.Comp, new MapCoordinates(position, _maps[depth]));
            map.SetTile(grid, grid.Comp, indices, tile);
        }

        public EntityUid SpawnTarget(int depth, Vector2? position = null) =>
            _pair.Server.EntMan.SpawnEntity(null, new MapCoordinates(position ?? TargetPosition, _maps[depth]));

        public void SpawnWall(int depth) =>
            _pair.Server.EntMan.SpawnEntity("ScpZLevelFearSightTestWall", new MapCoordinates(new Vector2(2.5f, 0.5f), _maps[depth]));

        public bool IsVisible(EntityUid target) => IsVisible(target, out _);

        public bool IsVisible(EntityUid target, out float distance) =>
            _pair.Server.System<EyeWatchingSystem>().CanSeeAcrossZ(Viewer, target,
                LineOfSightBlockerLevel.Transparent, out distance);

        public void Cleanup()
        {
            _glassDefinition.Transparent = _originalGlassTransparency;
            foreach (var id in _maps.Values)
                _pair.Server.System<SharedMapSystem>().DeleteMap(id);
            _pair.Server.EntMan.DeleteEntity(_network);
        }
    }
}

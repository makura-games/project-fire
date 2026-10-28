#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared._Scp.ZLevels;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Scp.ZLevels;

[TestFixture]
public sealed class ZLevelShootingTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ScpZShotTestViewer
          components:
          - type: CEZLevelViewer
            lookUp: true

        - type: entity
          parent: ScpZShotTestViewer
          id: ScpZShotTestViewerForward
          components:
          - type: CEZLevelViewer
            lookUp: false

        - type: entity
          id: ScpZShotTestGun
          components:
          - type: Gun
            soundGunshot: null
            minAngle: 0
            maxAngle: 0
            angleIncrease: 0
            angleDecay: 0
            projectileSpeed: 10

        - type: entity
          parent: ScpZShotTestGun
          id: ScpZShotTestLimitGun
          components:
          - type: Gun
            projectileSpeed: 11

        - type: entity
          id: ScpZShotTestProjectile
          components:
          - type: Ammo
            muzzleFlash: null
          - type: Projectile
            damage:
              types:
                Piercing: 10
          - type: Physics
            bodyType: Dynamic
          - type: Fixtures
            fixtures:
              projectile:
                shape: !type:PhysShapeCircle
                  radius: 0.04
                density: 1
                hard: false
                mask:
                - BulletImpassable

        - type: entity
          id: ScpZShotTestHitscan
          components:
          - type: HitscanAmmo
          - type: HitscanBasicRaycast
            maxDistance: 20
            collisionMask: BulletImpassable
          - type: HitscanBasicDamage
            damage:
              types:
                Piercing: 10

        - type: entity
          id: ScpZShotTestTarget
          components:
          - type: Damageable
          - type: Injurable
          - type: Physics
            bodyType: Static
          - type: Fixtures
            fixtures:
              target:
                shape: !type:PhysShapeAabb
                  bounds: "-0.2,-0.2,0.2,0.2"
                hard: true
                layer:
                - BulletImpassable

        - type: entity
          parent: ScpZShotTestTarget
          id: ScpZShotTestEdgeBlocker
          components:
          - type: Fixtures
            fixtures:
              target:
                shape: !type:PhysShapeAabb
                  bounds: "-0.02,-0.2,0.02,0.2"
                hard: true
                layer:
                - BulletImpassable
        """;

    [TestCase(true)]
    [TestCase(false)]
    public async Task UpwardShotsRequireLookUpAndEndTwoTilesPastLedge(bool lookUp)
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        ZLevelShotPlan plan = default;
        bool accepted = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair, lookUp);
            accepted = scene.TryPlan(new Vector2(0.5f, 0.5f), new Vector2(8.5f, 0.5f), scene.UpperId, out plan);
        });

        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(accepted, Is.EqualTo(lookUp));
            if (!lookUp)
                return;

            Assert.Multiple(() =>
            {
                Assert.That(plan.Offset, Is.EqualTo(1));
                Assert.That(plan.TargetMap, Is.EqualTo(scene.Upper));
                Assert.That(plan.Transition.X, Is.EqualTo(2f).Within(0.002f));
                Assert.That(plan.MaxDistance, Is.EqualTo(3.5f).Within(0.001f));
                Assert.That(0.5f + plan.MaxDistance, Is.EqualTo(4f).Within(0.001f),
                    "The first two upper tiles end at x=4; a target on the third tile must be unreachable.");
            });
        });

        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [TestCase(0.5f, false)]
    [TestCase(1f, true)]
    [TestCase(1.5f, true)]
    [TestCase(2.5f, false)]
    public async Task DownwardShotsRequireStandingWithinOneTileOfTheOpening(float sourceX, bool expected)
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        ZLevelShotPlan plan = default;
        bool accepted = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair);
            accepted = scene.TryPlan(new Vector2(sourceX, 0.5f), new Vector2(5.5f, 0.5f), scene.LowerId, out plan);
        });

        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(accepted, Is.EqualTo(expected));
            if (!expected)
                return;

            Assert.Multiple(() =>
            {
                Assert.That(plan.Offset, Is.EqualTo(-1));
                Assert.That(plan.Transition.X, Is.EqualTo(2f).Within(0.002f));
                Assert.That(plan.TransitionDistance, Is.EqualTo(2f - sourceX).Within(0.002f));
                Assert.That(float.IsPositiveInfinity(plan.MaxDistance), Is.True);
            });
        });

        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DownwardObliqueShotUsesDistanceToTheLedge()
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        ZLevelShotPlan plan = default;
        bool accepted = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair);
            scene.ExtendMiddleFloor(-5, 6);
            accepted = scene.TryPlan(new Vector2(1.2f, 0.5f), new Vector2(3.5f, 4.5f), scene.LowerId, out plan);
        });
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(accepted, Is.True, "A shooter 0.8 tiles from the ledge can aim obliquely across it.");
            Assert.That(plan.TransitionDistance, Is.GreaterThan(1f), "The physical source-floor ray is longer than the perpendicular distance.");
        });
        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [TestCase(1f, false, true)]
    [TestCase(0.99f, false, false)]
    [TestCase(1f, true, true)]
    [TestCase(0.99f, true, false)]
    public async Task DownwardObliqueShotKeepsTheOneTileLimitOnRotatedGrids(float sourceX, bool rotated, bool expected)
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        bool accepted = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair);
            scene.ExtendMiddleFloor(-5, 6);
            if (rotated)
            {
                pair.Server.System<SharedTransformSystem>().SetLocalPositionRotation(
                    scene.MiddleGrid, new Vector2(13f, -9f), Angle.FromDegrees(67));
            }
            accepted = scene.TryPlan(new Vector2(sourceX, 0.5f), new Vector2(3.5f, 4.5f), scene.LowerId, out _);
        });
        await pair.Server.WaitAssertion(() => Assert.That(accepted, Is.EqualTo(expected)));
        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [TestCase(0.5f, false)]
    [TestCase(1.5f, true)]
    public async Task DownwardObliqueShotMeasuresTheRealOpeningCorner(float sourceY, bool expected)
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        bool accepted = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair);
            scene.ExtendMiddleFloor(0, 5, 3);
            var map = pair.Server.System<SharedMapSystem>();
            for (var x = 2; x <= 3; x++)
            for (var y = 2; y <= 5; y++)
                map.SetTile(scene.MiddleGrid, scene.MiddleGrid.Comp, new Vector2i(x, y), Tile.Empty);
            accepted = scene.TryPlan(new Vector2(1.5f, sourceY), new Vector2(2.5f, 4.5f), scene.LowerId, out _);
        });
        await pair.Server.WaitAssertion(() => Assert.That(accepted, Is.EqualTo(expected)));
        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DownwardObliqueShotCannotUseAnEdgeCoveredByAnotherGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        bool accepted = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair);
            scene.ExtendMiddleFloor(-5, 6);
            var coveringGrid = pair.Server.MapMan.CreateGridEntity(scene.MiddleId);
            var tile = new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"].TileId);
            pair.Server.System<SharedMapSystem>().SetTile(coveringGrid, coveringGrid.Comp, new Vector2i(2, 0), tile);
            accepted = scene.TryPlan(new Vector2(1f, 0.5f), new Vector2(3.5f, 4.5f), scene.LowerId, out _);
        });
        await pair.Server.WaitAssertion(() => Assert.That(accepted, Is.False));
        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DownwardObliqueShotsHitTargetsOnTheLowerFloor(bool hitscan)
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        EntityUid target = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair);
            scene.ExtendMiddleFloor(-5, 6);
            target = scene.SpawnTarget(new Vector2(3.5f, 4.5f), scene.LowerId);
            scene.Fire(hitscan, new Vector2(1.2f, 0.5f), new Vector2(3.5f, 4.5f), scene.LowerId);
        });
        await pair.RunSeconds(1f);
        await pair.Server.WaitAssertion(() => Assert.That(scene.GetDamage(target), Is.GreaterThan(FixedPoint2.Zero)));
        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task DownwardObliqueShotUsesTheOpenPartOfAPartiallyCoveredEdge(bool rotated, bool rotatedCover)
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        bool accepted = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair);
            scene.ExtendMiddleFloor(-5, 6);
            var transform = pair.Server.System<SharedTransformSystem>();
            if (rotated)
                transform.SetLocalPositionRotation(scene.MiddleGrid, new Vector2(13f, -9f), Angle.FromDegrees(67));
            var coveringGrid = pair.Server.MapMan.CreateGridEntity(scene.MiddleId);
            var tile = new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"].TileId);
            pair.Server.System<SharedMapSystem>().SetTile(coveringGrid, coveringGrid.Comp, Vector2i.Zero, tile);
            transform.SetLocalPositionRotation(coveringGrid, scene.World(new Vector2(2f, 0.45f)),
                Angle.FromDegrees((rotated ? 67 : 0) + (rotatedCover ? 45 : 0)));
            // Проекция на край закрыта, но ближайший открытый угол находится в 0,9913 тайла от стрелка.
            accepted = scene.TryPlan(new Vector2(1.01f, 0.5f), new Vector2(3.5f, 4.5f), scene.LowerId, out _);
        });
        await pair.Server.WaitAssertion(() => Assert.That(accepted, Is.True));
        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ExistingTransparentFloorsBlockVerticalPassage(bool upward)
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        bool accepted = default;
        bool empty = default;
        bool transparentDuringCheck = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair);
            var tiles = pair.Server.ResolveDependency<ITileDefinitionManager>();
            var definition = (ContentTileDefinition) tiles["FloorGlass"];
            var originalTransparency = definition.Transparent;
            // Новые определения тайлов не поддерживают загрузку после запуска пары; флаг меняется только на время проверки.
            try
            {
                definition.Transparent = true;
                transparentDuringCheck = definition.Transparent;
                var transparent = new Tile(definition.TileId);
                var grid = upward ? scene.UpperGrid : scene.MiddleGrid;
                var tile = upward ? Vector2i.Zero : new Vector2i(5, 0);
                pair.Server.System<SharedMapSystem>().SetTile(grid, grid.Comp, tile, transparent);
                var position = upward ? new Vector2(0.5f, 0.5f) : new Vector2(5.5f, 0.5f);
                var mapId = upward ? scene.UpperId : scene.MiddleId;
                empty = pair.Server.System<ZLevelShootingSystem>().IsEmptyAt(new MapCoordinates(position, mapId));
                accepted = scene.TryPlan(
                    new Vector2(upward ? 0.5f : 1.5f, 0.5f),
                    new Vector2(5.5f, 0.5f),
                    upward ? scene.UpperId : scene.LowerId,
                    out _);
            }
            finally
            {
                definition.Transparent = originalTransparency;
            }
        });

        await pair.Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(transparentDuringCheck, Is.True);
                Assert.That(empty, Is.False);
                Assert.That(accepted, Is.False);
            });
        });

        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ShotsCannotSkipAnIntermediateFloorOrEnterAnUnrelatedMap(bool unrelated)
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        bool accepted = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair);
            accepted = scene.TryPlan(new Vector2(0.5f, 0.5f), new Vector2(5.5f, 0.5f),
                unrelated ? scene.UnrelatedId : scene.FarId, out _);
        });
        await pair.Server.WaitAssertion(() => Assert.That(accepted, Is.False));
        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task LedgesAccountForFloorsOnOtherGrids(bool upward)
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        bool accepted = default;
        ZLevelShotPlan plan = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair);
            var grid = pair.Server.MapMan.CreateGridEntity(upward ? scene.UpperId : scene.MiddleId);
            var map = pair.Server.System<SharedMapSystem>();
            var tile = new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"].TileId);
            if (upward)
                map.SetTile(grid, grid.Comp, new Vector2i(1, 0), tile);
            else
            {
                map.SetTile(grid, grid.Comp, new Vector2i(2, 0), tile);
                map.SetTile(grid, grid.Comp, new Vector2i(3, 0), tile);
            }

            accepted = scene.TryPlan(new Vector2(upward ? 0.5f : 1.5f, 0.5f), new Vector2(5.5f, 0.5f),
                upward ? scene.UpperId : scene.LowerId, out plan);
        });
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(accepted, Is.EqualTo(upward));
            if (upward)
                Assert.That(plan.MaxDistance, Is.EqualTo(2.5f).Within(0.001f));
        });
        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [TestCase(1)]
    [TestCase(-1)]
    public async Task LedgeGeometryUsesTheRotatedGridCoordinateSystem(int offset)
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        ZLevelShotPlan plan = default;
        bool accepted = default;
        Vector2 edge = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair);
            var transform = pair.Server.System<SharedTransformSystem>();
            var position = new Vector2(13f, -9f);
            var rotation = Angle.FromDegrees(67);
            transform.SetLocalPositionRotation(scene.MiddleGrid, position, rotation);
            transform.SetLocalPositionRotation(scene.UpperGrid, position, rotation);
            edge = scene.World(new Vector2(2f, 0.5f));
            accepted = scene.TryPlan(new Vector2(offset > 0 ? 0.5f : 1.5f, 0.5f), new Vector2(5.5f, 0.5f),
                offset > 0 ? scene.UpperId : scene.LowerId, out plan);
        });
        await pair.Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(accepted, Is.True);
                Assert.That(Vector2.Distance(plan.Transition, edge), Is.LessThan(0.002f));
                Assert.That(plan.TransitionDistance, Is.EqualTo(offset > 0 ? 1.5f : 0.5f).Within(0.002f));
            });
        });
        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SameFloorShotsDamageTargetsWithTheStandardDamageModel(bool hitscan)
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        EntityUid target = default;
        EntityUid ammo = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair);
            target = scene.SpawnTarget(new Vector2(1.5f, 0.5f), scene.MiddleId);
            ammo = scene.Fire(hitscan, new Vector2(0.5f, 0.5f), new Vector2(1.5f, 0.5f), scene.MiddleId);
        });

        await pair.RunSeconds(1f);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(scene.GetDamage(target), Is.GreaterThan(FixedPoint2.Zero));
                Assert.That(pair.Server.EntMan.EntityExists(ammo), Is.False);
            });
        });
        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [TestCase(false, 3.5f, true)]
    [TestCase(true, 3.5f, true)]
    [TestCase(false, 4.5f, false)]
    [TestCase(true, 4.5f, false)]
    public async Task UpwardShotDamageStopsAtTwoTiles(bool hitscan, float targetX, bool shouldHit)
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        EntityUid target = default;
        EntityUid ammo = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair);
            target = scene.SpawnTarget(new Vector2(targetX, 0.5f), scene.UpperId);
            ammo = scene.Fire(hitscan, new Vector2(0.5f, 0.5f), new Vector2(8.5f, 0.5f), scene.UpperId);
        });

        // Скорость 10 тайлов/с: секунды достаточно для достижения цели или удаления у границы x=4.
        await pair.RunSeconds(1f);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(scene.GetDamage(target) > FixedPoint2.Zero, Is.EqualTo(shouldHit));
                Assert.That(pair.Server.EntMan.EntityExists(ammo), Is.False);
            });
        });
        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SourceFloorBlockerInterceptsTheLastSegmentBeforeTheEdge(bool hitscan)
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        EntityUid blocker = default;
        EntityUid target = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair);
            blocker = scene.SpawnTarget(new Vector2(1.95f, 0.5f), scene.MiddleId, true);
            target = scene.SpawnTarget(new Vector2(3.5f, 0.5f), scene.UpperId);
            scene.Fire(hitscan, new Vector2(0.5f, 0.5f), new Vector2(8.5f, 0.5f), scene.UpperId);
        });

        await pair.RunSeconds(1f);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(scene.GetDamage(blocker), Is.GreaterThan(FixedPoint2.Zero));
                Assert.That(scene.GetDamage(target), Is.EqualTo(FixedPoint2.Zero));
            });
        });
        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UpperTargetAtRangeBoundaryReceivesTheLastContactPass()
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        EntityUid target = default;
        EntityUid ammo = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair);
            target = scene.SpawnTarget(new Vector2(3.99f, 0.5f), scene.UpperId, true);
            ammo = scene.Fire(false, new Vector2(0.5f, 0.5f), new Vector2(8.5f, 0.5f), scene.UpperId,
                "ScpZShotTestLimitGun");
        });

        // При скорости 11 последний обычный шаг не касается тонкой цели; нужен проход контактов после ограничения x=4.
        await pair.RunSeconds(1f);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(scene.GetDamage(target), Is.GreaterThan(FixedPoint2.Zero));
                Assert.That(pair.Server.EntMan.EntityExists(ammo), Is.False);
            });
        });
        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DownwardShotsRetainEnoughRangeToHitDistantLowerTargets(bool hitscan)
    {
        await using var pair = await PoolManager.GetServerClient();
        Scene scene = default!;
        EntityUid target = default;
        await pair.Server.WaitPost(() =>
        {
            scene = new Scene(pair);
            target = scene.SpawnTarget(new Vector2(8.5f, 0.5f), scene.LowerId);
            scene.Fire(hitscan, new Vector2(1.5f, 0.5f), new Vector2(8.5f, 0.5f), scene.LowerId);
        });

        // Цель находится в семи тайлах от стрелка; учитываем дополнительный шаг контактов у края.
        await pair.RunSeconds(1.5f);
        await pair.Server.WaitAssertion(() => Assert.That(scene.GetDamage(target), Is.GreaterThan(FixedPoint2.Zero)));
        await pair.Server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    private sealed class Scene
    {
        private readonly TestPair _pair;
        private readonly EntityUid _network;
        private readonly EntityUid _shooter;
        private readonly MapId[] _maps;
        public readonly Entity<MapGridComponent> MiddleGrid;
        public readonly Entity<MapGridComponent> UpperGrid;
        public readonly EntityUid Upper;
        public readonly MapId LowerId;
        public readonly MapId MiddleId;
        public readonly MapId UpperId;
        public readonly MapId FarId;
        public readonly MapId UnrelatedId;

        public Scene(TestPair pair, bool lookUp = true)
        {
            _pair = pair;
            var server = pair.Server;
            var map = server.System<SharedMapSystem>();
            var zLevels = server.System<CEZLevelsSystem>();
            var lower = map.CreateMap(out LowerId);
            var middle = map.CreateMap(out MiddleId);
            Upper = map.CreateMap(out UpperId);
            var far = map.CreateMap(out FarId);
            map.CreateMap(out UnrelatedId);
            _maps = [LowerId, MiddleId, UpperId, FarId, UnrelatedId];
            var network = zLevels.CreateMapNetwork();
            _network = network;
            zLevels.TryAddMapsIntoNetwork(network, new Dictionary<EntityUid, int>
            {
                [lower] = -1,
                [middle] = 0,
                [Upper] = 1,
                [far] = 2,
            });
            MiddleGrid = server.MapMan.CreateGridEntity(MiddleId);
            UpperGrid = server.MapMan.CreateGridEntity(UpperId);
            var tile = new Tile(server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"].TileId);
            for (var x = 0; x < 2; x++)
                map.SetTile(MiddleGrid, MiddleGrid.Comp, new Vector2i(x, 0), tile);
            for (var x = 2; x <= 8; x++)
                map.SetTile(UpperGrid, UpperGrid.Comp, new Vector2i(x, 0), tile);
            _shooter = server.EntMan.SpawnEntity(lookUp ? "ScpZShotTestViewer" : "ScpZShotTestViewerForward",
                new MapCoordinates(new Vector2(0.5f, 0.5f), MiddleId));
        }

        public Vector2 World(Vector2 local)
        {
            return Vector2.Transform(local, _pair.Server.System<SharedTransformSystem>().GetWorldMatrix(MiddleGrid));
        }

        public void ExtendMiddleFloor(int minY, int maxY, int maxX = 1)
        {
            var tile = new Tile(_pair.Server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"].TileId);
            var map = _pair.Server.System<SharedMapSystem>();
            for (var x = 0; x <= maxX; x++)
            for (var y = minY; y <= maxY; y++)
                map.SetTile(MiddleGrid, MiddleGrid.Comp, new Vector2i(x, y), tile);
        }

        public bool TryPlan(Vector2 from, Vector2 to, MapId target, out ZLevelShotPlan plan)
        {
            var source = new MapCoordinates(World(from), MiddleId);
            _pair.Server.System<SharedTransformSystem>().SetMapCoordinates(_shooter, source);
            return _pair.Server.System<ZLevelShootingSystem>().TryGetShotPlan(_shooter, source,
                new MapCoordinates(World(to), target), out plan);
        }

        public EntityUid SpawnTarget(Vector2 position, MapId mapId, bool thin = false)
        {
            return _pair.Server.EntMan.SpawnEntity(thin ? "ScpZShotTestEdgeBlocker" : "ScpZShotTestTarget",
                new MapCoordinates(World(position), mapId));
        }

        public EntityUid Fire(bool hitscan, Vector2 from, Vector2 to, MapId target, string gunPrototype = "ScpZShotTestGun")
        {
            var server = _pair.Server;
            var transform = server.System<SharedTransformSystem>();
            var source = new MapCoordinates(World(from), MiddleId);
            transform.SetMapCoordinates(_shooter, source);
            var gun = server.EntMan.SpawnEntity(gunPrototype, source);
            var ammo = server.EntMan.SpawnEntity(hitscan ? "ScpZShotTestHitscan" : "ScpZShotTestProjectile", source);
            server.System<GunSystem>().Shoot(gun, server.EntMan.GetComponent<GunComponent>(gun), ammo,
                transform.ToCoordinates(source), transform.ToCoordinates(new MapCoordinates(World(to), target)), out _, _shooter);
            return ammo;
        }

        public FixedPoint2 GetDamage(EntityUid target)
        {
            var damageable = _pair.Server.EntMan.GetComponent<DamageableComponent>(target);
            return _pair.Server.System<DamageableSystem>().GetPositiveDamage((target, damageable)).GetTotal();
        }

        public void Cleanup()
        {
            foreach (var id in _maps)
                _pair.Server.System<SharedMapSystem>().DeleteMap(id);
            _pair.Server.EntMan.DeleteEntity(_network);
        }
    }
}

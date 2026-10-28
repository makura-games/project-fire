using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._CE.ZLevels.Core;
using Content.Server._Scp.Fear;
using Content.Server._Sunrise.Mood;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._Scp.Fear;
using Content.Shared._Scp.Blinking;
using Content.Shared._Scp.Fear.Components;
using Content.Shared._Scp.Proximity;
using Content.Shared._Scp.Shaders.Grain;
using Content.Shared._Scp.Shaders.Highlighting;
using Content.Shared._Scp.Shaders.Vignette;
using Content.Shared._Scp.Weapons.Ranged;
using Content.Shared.Actions;
using Content.Shared.Maps;
using Content.Shared.StatusEffect;
using Content.Shared.StatusEffectNew.Components;
using Content.Shared.Toggleable;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using NewStatusEffectsSystem = Content.Shared.StatusEffectNew.StatusEffectsSystem;

namespace Content.IntegrationTests.Tests._Scp.Fear;

[TestFixture]
public sealed class ZLevelFearTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ScpZFearBehaviorTestViewer
          components:
          - type: Fear
            phobias: [Exoremophobia]
          - type: MobState
          - type: Mood
          - type: CEZLevelViewer
          - type: Blinkable
            blinkingInterval: 1000
          - type: FieldOfView
            angle: 360
          - type: GrainOverlay
          - type: VignetteOverlay
          - type: StatusEffects
            allowed: [Jitter]

        - type: entity
          id: ScpZFearBehaviorTestSource
          components:
          - type: FearSource
            phobiaType: Exoremophobia
            uponSeenState: Fear
            uponComeCloser: None
          - type: WatchingTarget
          - type: Sprite
            sprite: Objects/Fun/ducky.rsi
            state: icon
        """;

    [TestCase(-1, false)]
    [TestCase(1, false)]
    [TestCase(-1, true)]
    [TestCase(1, true)]
    public async Task InterfloorFearHighlightsTheSourceForTheConnectedViewer(int depth, bool initiallyVisible)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        Scene scene = default!;
        ICommonSession session = default!;
        EntityUid? previousEntity = null;
        bool attached = default;
        await server.WaitPost(() =>
        {
            scene = new Scene(pair, depth);
            if (initiallyVisible)
                scene.RevealSource(depth);
        });

        if (initiallyVisible)
        {
            // Маркер должен существовать до attach: состояния сущностей применяются клиентом раньше состояния игрока.
            await pair.RunSeconds(0.5f);
            await server.WaitAssertion(() =>
            {
                Assert.That(server.EntMan.GetComponent<FearComponent>(scene.Viewer).State, Is.EqualTo(FearState.Fear));
                Assert.That(server.EntMan.GetComponent<HighlightedComponent>(scene.Source).Recipient,
                    Is.EqualTo(scene.Viewer));
            });
        }

        await server.WaitPost(() =>
        {
            session = pair.Player ?? throw new InvalidOperationException("The connected test pair has no server session.");
            previousEntity = session.AttachedEntity;
            attached = server.PlayerMan.SetAttachedEntity(session, scene.Viewer);
        });

        // Реплицируем игрока, соседние карты и источник с уже созданным маркером либо пока без подсветки.
        await pair.RunSeconds(0.5f);
        await pair.SyncTicks(targetDelta: 1);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(attached, Is.True);
                Assert.That(server.EntMan.GetComponent<FearComponent>(scene.Viewer).State,
                    Is.EqualTo(initiallyVisible ? FearState.Fear : FearState.None));
            });
        });
        await client.WaitAssertion(() =>
        {
            Assert.That(client.PlayerMan.LocalEntity, Is.EqualTo(pair.ToClientUid(scene.Viewer)));
            Assert.That(client.EntMan.GetComponent<SpriteComponent>(pair.ToClientUid(scene.Source)).PostShader != null,
                Is.EqualTo(initiallyVisible));
        });

        if (!initiallyVisible)
        {
            await server.WaitPost(() => scene.RevealSource(depth));

            // Обычный scan вызывает подсветку на сервере; клиент должен получить ее через сетевой компонент.
            await pair.RunSeconds(0.5f);
            await pair.SyncTicks(targetDelta: 1);
        }
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.GetComponent<FearComponent>(scene.Viewer).State, Is.EqualTo(FearState.Fear));
                Assert.That(server.EntMan.GetComponent<HighlightedComponent>(scene.Source).Recipient, Is.EqualTo(scene.Viewer));
            });
        });
        await client.WaitAssertion(() =>
        {
            var source = pair.ToClientUid(scene.Source);
            Assert.Multiple(() =>
            {
                Assert.That(client.EntMan.GetComponent<HighlightedComponent>(source).Recipient,
                    Is.EqualTo(pair.ToClientUid(scene.Viewer)));
                Assert.That(client.EntMan.GetComponent<SpriteComponent>(source).PostShader, Is.Not.Null);
            });
        });

        await server.WaitPost(() =>
        {
            server.PlayerMan.SetAttachedEntity(session, previousEntity);
            scene.Cleanup();
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(-1, false, FearState.Fear)]
    [TestCase(-1, true, FearState.Fear)]
    [TestCase(-1, false, FearState.Terror)]
    [TestCase(0, false, FearState.Fear)]
    public async Task VisibleSourceAppliesHalfEffectsOnlyOnAnotherFloor(int depth, bool transparent, FearState state)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        Scene scene = default!;
        await server.WaitPost(() =>
        {
            scene = new Scene(pair, depth, state);
            if (depth < 0)
                scene.OpenLowerFloor(transparent);
        });
        // Несколько обычных обновлений покрывают период межэтажного поиска в 0.2 секунды.
        await pair.RunSeconds(0.5f);

        await server.WaitAssertion(() =>
        {
            var fear = entMan.GetComponent<FearComponent>(scene.Viewer);
            var spread = entMan.GetComponent<DispersingShotSourceComponent>(scene.Viewer);
            var distant = depth != 0;
            var terror = state == FearState.Terror;
            Assert.Multiple(() =>
            {
                Assert.That(fear.State, Is.EqualTo(state));
                Assert.That(fear.EffectStrength, Is.EqualTo(distant ? 0.5f : 1f));
                Assert.That(entMan.GetComponent<MoodComponent>(scene.Viewer).CurrentMoodLevel,
                    Is.EqualTo(!distant ? 32f : terror ? 33.5f : 41f));
                Assert.That(entMan.GetComponent<GrainOverlayComponent>(scene.Viewer).AdditionalStrength,
                    Is.EqualTo(!distant ? 300f : terror ? 350f : 150f));
                Assert.That(entMan.GetComponent<VignetteOverlayComponent>(scene.Viewer).AdditionalStrength,
                    Is.EqualTo(!distant ? 70f : terror ? 200f : 35f));
                Assert.That(spread.AngleIncreaseMultiplier, Is.EqualTo(!distant ? 4f : terror ? 5f : 3f));
                Assert.That(spread.MaxAngleMultiplier, Is.EqualTo(!distant ? 4f : terror ? 5f : 3f));
            });
        });

        await server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UpperSourceDoesNotScareUntilLookUpActionIsUsed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        Scene scene = default!;
        var toggle = new ToggleActionEvent();
        await server.WaitPost(() =>
        {
            scene = new Scene(pair, 1);
            scene.OpenUpperFloor();
        });
        await pair.RunSeconds(0.5f);
        await server.WaitAssertion(() => Assert.That(entMan.GetComponent<FearComponent>(scene.Viewer).State, Is.EqualTo(FearState.None)));

        await server.WaitPost(() =>
        {
            var actions = server.System<SharedActionsSystem>();
            var viewer = entMan.GetComponent<CEZLevelViewerComponent>(scene.Viewer);
            var action = actions.GetAction(viewer.ActionEntity);
            if (action == null)
                throw new InvalidOperationException("The test viewer has no look-up action.");
            actions.PerformAction(scene.Viewer, action.Value, toggle);
        });
        await pair.RunSeconds(0.5f);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(toggle.Handled, Is.True);
                Assert.That(entMan.GetComponent<CEZLevelViewerComponent>(scene.Viewer).LookUp, Is.True);
                Assert.That(entMan.GetComponent<FearComponent>(scene.Viewer).State, Is.EqualTo(FearState.Fear));
                Assert.That(entMan.GetComponent<FearComponent>(scene.Viewer).EffectStrength, Is.EqualTo(0.5f));
            });
        });
        await server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OpaqueFloorOrUnmatchedPhobiaPreventsInterfloorFear(bool unmatchedPhobia)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        Scene scene = default!;
        await server.WaitPost(() =>
        {
            scene = new Scene(pair, -1);
            if (unmatchedPhobia)
            {
                scene.OpenLowerFloor();
                entMan.GetComponent<FearSourceComponent>(scene.Source).PhobiaType = "Necrophobia";
            }
        });
        await pair.RunSeconds(0.5f);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<FearComponent>(scene.Viewer).State, Is.EqualTo(FearState.None));
                Assert.That(entMan.GetComponent<GrainOverlayComponent>(scene.Viewer).AdditionalStrength, Is.Zero);
                Assert.That(entMan.GetComponent<MoodComponent>(scene.Viewer).CurrentMoodLevel, Is.EqualTo(50f));
            });
        });
        await server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RepeatedInterfloorSightDoesNotAccumulateAdrenalineOrResetCalmTimer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        Scene scene = default!;
        var adrenalineEnd = TimeSpan.Zero;
        var nextCalmDown = TimeSpan.Zero;
        await server.WaitPost(() =>
        {
            scene = new Scene(pair, -1);
            scene.OpenLowerFloor();
        });
        await pair.RunSeconds(0.5f);
        await server.WaitPost(() =>
        {
            adrenalineEnd = GetAdrenalineEnd(pair, scene.Viewer);
            nextCalmDown = entMan.GetComponent<FearComponent>(scene.Viewer).NextTimeDecreaseFearLevel;
        });
        await pair.RunSeconds(0.8f);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(GetAdrenalineEnd(pair, scene.Viewer), Is.EqualTo(adrenalineEnd));
                Assert.That(entMan.GetComponent<FearComponent>(scene.Viewer).NextTimeDecreaseFearLevel, Is.EqualTo(nextCalmDown));
            });
        });
        await server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FullSameStateUpgradeChangesPersistentEffectsWithoutReplayingAdrenalineOrJitter()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        Scene scene = default!;
        var adrenalineEnd = TimeSpan.Zero;
        var jitterEnd = TimeSpan.Zero;
        await server.WaitPost(() =>
        {
            scene = new Scene(pair, -1);
            var fear = server.System<FearSystem>();
            fear.TrySetFearLevel(scene.Viewer, FearState.Fear, 0.5f);
            adrenalineEnd = GetAdrenalineEnd(pair, scene.Viewer);
            jitterEnd = entMan.GetComponent<StatusEffectsComponent>(scene.Viewer).ActiveEffects["Jitter"].Cooldown.Item2;
            fear.TrySetFearLevel(scene.Viewer, FearState.Fear);
            scene.OpenLowerFloor();
        });
        await pair.RunSeconds(0.5f);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<FearComponent>(scene.Viewer).State, Is.EqualTo(FearState.Fear));
                Assert.That(entMan.GetComponent<FearComponent>(scene.Viewer).EffectStrength, Is.EqualTo(1f));
                Assert.That(entMan.GetComponent<MoodComponent>(scene.Viewer).CurrentMoodLevel, Is.EqualTo(40f));
                Assert.That(entMan.GetComponent<GrainOverlayComponent>(scene.Viewer).AdditionalStrength, Is.EqualTo(300f));
                Assert.That(entMan.GetComponent<DispersingShotSourceComponent>(scene.Viewer).MaxAngleMultiplier, Is.EqualTo(4f));
                Assert.That(GetAdrenalineEnd(pair, scene.Viewer), Is.EqualTo(adrenalineEnd));
                Assert.That(entMan.GetComponent<StatusEffectsComponent>(scene.Viewer).ActiveEffects["Jitter"].Cooldown.Item2, Is.EqualTo(jitterEnd));
            });
        });
        await server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LosingSightRetainsHalfFearAndTimerAndCalmingPreservesStrength()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        Scene scene = default!;
        var nextCalmDown = TimeSpan.Zero;
        var calmed = false;
        await server.WaitPost(() =>
        {
            scene = new Scene(pair, -1);
            scene.OpenLowerFloor();
        });
        await pair.RunSeconds(0.5f);
        await server.WaitPost(() =>
        {
            nextCalmDown = entMan.GetComponent<FearComponent>(scene.Viewer).NextTimeDecreaseFearLevel;
            scene.CloseLowerFloor();
        });
        await pair.RunSeconds(0.5f);
        await server.WaitAssertion(() =>
        {
            var fear = entMan.GetComponent<FearComponent>(scene.Viewer);
            Assert.Multiple(() =>
            {
                Assert.That(fear.State, Is.EqualTo(FearState.Fear));
                Assert.That(fear.EffectStrength, Is.EqualTo(0.5f));
                Assert.That(fear.NextTimeDecreaseFearLevel, Is.EqualTo(nextCalmDown));
            });
        });
        await server.WaitPost(() => calmed = server.System<FearSystem>().TryCalmDown(
            (scene.Viewer, entMan.GetComponent<FearComponent>(scene.Viewer))));
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(calmed, Is.True);
                Assert.That(entMan.GetComponent<FearComponent>(scene.Viewer).State, Is.EqualTo(FearState.Anxiety));
                Assert.That(entMan.GetComponent<FearComponent>(scene.Viewer).EffectStrength, Is.EqualTo(0.5f));
            });
        });

        await server.WaitPost(() => calmed = server.System<FearSystem>().TryCalmDown(
            (scene.Viewer, entMan.GetComponent<FearComponent>(scene.Viewer))));
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(calmed, Is.True);
                Assert.That(entMan.GetComponent<FearComponent>(scene.Viewer).State, Is.EqualTo(FearState.None));
                Assert.That(entMan.GetComponent<FearComponent>(scene.Viewer).EffectStrength, Is.EqualTo(1f));
            });
        });
        await server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    private static TimeSpan GetAdrenalineEnd(TestPair pair, EntityUid subject)
    {
        var server = pair.Server;
        if (!server.System<NewStatusEffectsSystem>().TryGetStatusEffect(subject, "StatusEffectFearAdrenaline", out var effect))
            throw new InvalidOperationException("The test subject has no fear adrenaline effect.");
        return server.EntMan.GetComponent<StatusEffectComponent>(effect.Value).EndEffectTime!.Value;
    }

    [Test]
    public async Task SeeingTheSameFearOnOwnFloorUpgradesSightMoodWithoutStackingIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        Scene scene = default!;
        await server.WaitPost(() =>
        {
            scene = new Scene(pair, -1);
            scene.OpenLowerFloor();
        });
        await pair.RunSeconds(0.5f);
        await server.WaitAssertion(() => Assert.That(
            entMan.GetComponent<MoodComponent>(scene.Viewer).CurrentMoodLevel, Is.EqualTo(41f)));
        await server.WaitPost(() => entMan.SpawnEntity("ScpZFearBehaviorTestSource",
            new MapCoordinates(new Vector2(3.5f, 0.5f), entMan.GetComponent<TransformComponent>(scene.Viewer).MapID)));
        await pair.RunSeconds(0.5f);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<FearComponent>(scene.Viewer).EffectStrength, Is.EqualTo(1f));
                Assert.That(entMan.GetComponent<MoodComponent>(scene.Viewer).CurrentMoodLevel, Is.EqualTo(32f));
            });
        });
        await server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InterfloorProximityKeepsAudioParametersAndClearsOnlyActiveEffectsOnLostSight()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        Scene scene = default!;
        await server.WaitPost(() =>
        {
            scene = new Scene(pair, -1);
            scene.OpenLowerFloor();
            var source = entMan.GetComponent<FearSourceComponent>(scene.Source);
            source.UponSeenState = FearState.None;
            source.UponComeCloser = FearState.Terror;
            entMan.AddComponent<ProximityReceiverComponent>(scene.Source).CloseRange = 6f;
        });
        await pair.RunSeconds(0.5f);
        await server.WaitAssertion(() =>
        {
            var effects = entMan.GetComponent<FearActiveSoundEffectsComponent>(scene.Viewer);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<FearComponent>(scene.Viewer).EffectStrength, Is.EqualTo(0.5f));
                Assert.That(entMan.GetComponent<ActiveFearFallOffComponent>(scene.Viewer).FallOffChance, Is.EqualTo(0.015f));
                Assert.That(effects.PlayHeartbeatSound, Is.True);
                Assert.That(effects.PlayBreathingSound, Is.True);
                // Расстояние 4 при радиусе 6: звуковой коэффициент близости равен 1/3 и не ослабляется этажом.
                Assert.That(effects.AdditionalVolume, Is.EqualTo(5f + 11f / 3f).Within(0.001f));
                Assert.That(effects.Pitch, Is.EqualTo(1f - 0.35f / 3f).Within(0.001f));
                Assert.That(effects.NextHeartbeatCooldown.TotalSeconds, Is.EqualTo(2f - 1.7f / 3f).Within(0.001f));
            });
        });
        await server.WaitPost(scene.CloseLowerFloor);
        await pair.RunSeconds(0.5f);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<FearActiveSoundEffectsComponent>(scene.Viewer), Is.False);
                Assert.That(entMan.HasComponent<ActiveCloseFearComponent>(scene.Viewer), Is.False);
                Assert.That(entMan.GetComponent<FearComponent>(scene.Viewer).State, Is.EqualTo(FearState.Terror));
                Assert.That(entMan.GetComponent<FearComponent>(scene.Viewer).EffectStrength, Is.EqualTo(0.5f));
                Assert.That(entMan.GetComponent<GrainOverlayComponent>(scene.Viewer).AdditionalStrength, Is.EqualTo(350f));
            });
        });
        await server.WaitPost(scene.Cleanup);
        await pair.CleanReturnAsync();
    }

    private sealed class Scene
    {
        private static readonly Vector2 ViewerPosition = new(0.5f, 0.5f);
        private static readonly Vector2 SourcePosition = new(4.5f, 0.5f);
        private readonly TestPair _pair;
        private readonly Entity<CEZMapNetworkComponent> _network;
        private readonly Dictionary<int, MapId> _maps = new();
        private readonly Dictionary<int, Entity<MapGridComponent>> _grids = new();
        private readonly Tile _solidFloor;
        private readonly Tile _glassFloor;
        private readonly ContentTileDefinition _glassDefinition;
        private readonly bool _originalGlassTransparency;
        public readonly EntityUid Viewer;
        public readonly EntityUid Source;

        public Scene(TestPair pair, int sourceDepth, FearState state = FearState.Fear)
        {
            _pair = pair;
            var server = pair.Server;
            var map = server.System<SharedMapSystem>();
            var levels = new Dictionary<EntityUid, int>();
            var tiles = server.ResolveDependency<ITileDefinitionManager>();
            _solidFloor = new Tile(tiles["FloorSteel"].TileId);
            _glassDefinition = (ContentTileDefinition) tiles["FloorGlass"];
            _originalGlassTransparency = _glassDefinition.Transparent;
            _glassDefinition.Transparent = true;
            _glassFloor = new Tile(_glassDefinition.TileId);
            for (var depth = -1; depth <= 1; depth++)
            {
                var uid = map.CreateMap(out var id);
                _maps.Add(depth, id);
                levels.Add(uid, depth);
                var grid = server.MapMan.CreateGridEntity(id);
                _grids.Add(depth, grid);
                for (var x = 0; x <= 5; x++)
                    map.SetTile(grid, grid.Comp, new Vector2i(x, 0), _solidFloor);
            }
            var zLevels = server.System<CEZLevelsSystem>();
            _network = zLevels.CreateMapNetwork();
            zLevels.TryAddMapsIntoNetwork(_network, levels);
            Viewer = server.EntMan.SpawnEntity("ScpZFearBehaviorTestViewer", new MapCoordinates(ViewerPosition, _maps[0]));
            // Автоматическое моргание не должно прерывать проверяемую последовательность наблюдений.
            server.System<SharedBlinkingSystem>().SetNextBlink(Viewer, TimeSpan.FromMinutes(10), predicted: false);
            Source = server.EntMan.SpawnEntity("ScpZFearBehaviorTestSource", new MapCoordinates(SourcePosition, _maps[sourceDepth]));
            server.EntMan.GetComponent<FearSourceComponent>(Source).UponSeenState = state;
        }

        public void OpenLowerFloor(bool transparent = false) => SetFloor(0, SourcePosition, transparent ? _glassFloor : Tile.Empty);
        public void CloseLowerFloor() => SetFloor(0, SourcePosition, _solidFloor);
        public void OpenUpperFloor() => SetFloor(1, ViewerPosition, Tile.Empty);

        public void RevealSource(int depth)
        {
            if (depth < 0)
            {
                OpenLowerFloor();
                return;
            }

            OpenUpperFloor();
            var server = _pair.Server;
            var actions = server.System<SharedActionsSystem>();
            var viewer = server.EntMan.GetComponent<CEZLevelViewerComponent>(Viewer);
            var action = actions.GetAction(viewer.ActionEntity)
                ?? throw new InvalidOperationException("The test viewer has no look-up action.");
            actions.PerformAction(Viewer, action, new ToggleActionEvent());
        }

        public void Cleanup()
        {
            _glassDefinition.Transparent = _originalGlassTransparency;
            foreach (var map in _maps.Values)
                _pair.Server.System<SharedMapSystem>().DeleteMap(map);
            _pair.Server.EntMan.DeleteEntity(_network);
        }

        private void SetFloor(int depth, Vector2 position, Tile tile)
        {
            var grid = _grids[depth];
            var map = _pair.Server.System<SharedMapSystem>();
            map.SetTile(grid, grid.Comp,
                map.TileIndicesFor(grid.Owner, grid.Comp, new MapCoordinates(position, _maps[depth])), tile);
        }
    }
}

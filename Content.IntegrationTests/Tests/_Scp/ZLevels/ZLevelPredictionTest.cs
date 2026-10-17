#nullable enable
using System.Numerics;
using Content.Client._CE.ZLevels.Core;
using Content.Shared._CE.ZLevels.Core.Components;
using Robust.Client.Timing;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Scp.ZLevels;

[TestFixture]
public sealed class ZLevelPredictionTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ScpZPredictionTestSubject
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
    public async Task AuthoritativeAirborneStateWakesPredictedSleepingBody()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        IComponentState? airborneState = null;
        EntityUid subject = default;
        MapId mapId = default;
        var position = new Vector2(0.5f, 0.5f);

        await server.WaitPost(() =>
        {
            var source = server.EntMan.SpawnEntity("ScpZPredictionTestSubject", MapCoordinates.Nullspace);
            var physics = server.EntMan.GetComponent<CEZPhysicsComponent>(source);
            physics.LocalPosition = 0.5f;
            physics.Velocity = -1f;
            airborneState = server.EntMan.GetComponentState(server.EntMan.EventBus, physics, null, GameTick.Zero);
            server.EntMan.DeleteEntity(source);
        });

        await client.WaitPost(() =>
        {
            var map = client.System<SharedMapSystem>();
            var mapUid = map.CreateMap(out mapId);
            client.EntMan.AddComponent<CEZMapComponent>(mapUid);
            var grid = client.MapMan.CreateGridEntity(mapId);
            map.SetTile(grid, grid.Comp, Vector2i.Zero, new Tile(1));
            subject = client.EntMan.SpawnEntity("ScpZPredictionTestSubject", new MapCoordinates(position, mapId));
            var physics = client.EntMan.GetComponent<CEZPhysicsComponent>(subject);
            client.System<CEClientZLevelsSystem>().SleepBody((subject, physics));
            physics.SleepTimer = physics.TimeToSleep;
            physics.CachedGroundHeight = -1f;
            physics.CachedStickyGround = true;
        });

        await client.WaitAssertion(() =>
        {
            Assert.That(airborneState, Is.Not.Null);
            Assert.That(client.EntMan.GetComponent<CEZPhysicsComponent>(subject).Sleeping, Is.True);
            Assert.That(client.System<CEClientZLevelsSystem>().ActiveBodies, Does.Not.Contain(subject));
        });

        await client.WaitPost(() =>
        {
            var physics = client.EntMan.GetComponent<CEZPhysicsComponent>(subject);
            var state = new ComponentHandleState(airborneState, null);
            using var application = client.ResolveDependency<IClientGameTiming>().StartStateApplicationArea();
            client.EntMan.EventBus.RaiseComponentEvent(subject, physics, ref state);
        });

        await client.WaitAssertion(() =>
        {
            var physics = client.EntMan.GetComponent<CEZPhysicsComponent>(subject);
            Assert.Multiple(() =>
            {
                Assert.That(physics.LocalPosition, Is.EqualTo(0.5f));
                Assert.That(physics.Velocity, Is.EqualTo(-1f));
                Assert.That(physics.SleepTimer, Is.Zero);
                Assert.That(physics.CachedGroundHeight, Is.Zero);
                Assert.That(physics.CachedStickyGround, Is.False);
                Assert.That(physics.Sleeping, Is.False);
                Assert.That(client.System<CEClientZLevelsSystem>().ActiveBodies, Does.Contain(subject));
            });
        });

        await client.WaitPost(() =>
        {
            var zLevels = client.System<CEClientZLevelsSystem>();
            // Проигрываем секунду Z-физики после отката без новых серверных состояний.
            for (var tick = 0; tick < 60; tick++)
                zLevels.Update(1f / 60f);
        });

        await client.WaitAssertion(() =>
        {
            var physics = client.EntMan.GetComponent<CEZPhysicsComponent>(subject);
            Assert.Multiple(() =>
            {
                Assert.That(physics.LocalPosition, Is.EqualTo(0f).Within(0.01f));
                Assert.That(physics.Velocity, Is.Zero);
                Assert.That(physics.Sleeping, Is.True);
                Assert.That(client.System<CEClientZLevelsSystem>().ActiveBodies, Does.Not.Contain(subject));
                Assert.That(client.System<SharedTransformSystem>().GetWorldPosition(subject), Is.EqualTo(position));
            });
        });

        await client.WaitPost(() => client.System<SharedMapSystem>().DeleteMap(mapId));
        await pair.CleanReturnAsync();
    }
}

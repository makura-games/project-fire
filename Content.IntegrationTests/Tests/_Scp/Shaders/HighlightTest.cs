#nullable enable
using Content.IntegrationTests.Pair;
using Content.Shared._Scp.Shaders.Highlighting;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Timing;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using ClientPlayerManager = Robust.Client.Player.IPlayerManager;

namespace Content.IntegrationTests.Tests._Scp.Shaders;

[TestFixture]
public sealed class HighlightTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ScpHighlightTestSprite
          components:
          - type: Sprite
            sprite: Objects/Tools/crowbar.rsi
            state: icon
        """;

    [TestCase(false)]
    [TestCase(true)]
    public async Task ServerHighlightStateAppliesToTheClientSprite(bool beforeStartup)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var state = await GetServerHighlightState(pair);
        var client = pair.Client;
        EntityUid target = default;

        await client.WaitPost(() =>
        {
            target = beforeStartup
                ? client.EntMan.CreateEntityUninitialized("ScpHighlightTestSprite", MapCoordinates.Nullspace)
                : client.EntMan.SpawnEntity("ScpHighlightTestSprite", MapCoordinates.Nullspace);

            client.EntMan.AddComponent<HighlightedComponent>(target);
            ApplyState(pair, target, state);

            if (beforeStartup)
            {
                client.EntMan.InitializeEntity(target);
                client.EntMan.StartEntity(target);
            }
        });

        await client.WaitAssertion(() =>
            Assert.That(client.EntMan.GetComponent<SpriteComponent>(target).PostShader, Is.Not.Null));

        await client.WaitPost(() => client.EntMan.DeleteEntity(target));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RecipientStateChangeHidesAndRestoresTheHighlight()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var state = await GetServerHighlightState(pair);
        var client = pair.Client;
        EntityUid target = default;
        EntityUid otherRecipient = default;

        await client.WaitPost(() =>
        {
            target = client.EntMan.SpawnEntity("ScpHighlightTestSprite", MapCoordinates.Nullspace);
            otherRecipient = client.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            client.EntMan.AddComponent<HighlightedComponent>(target);
            ApplyState(pair, target, state);
        });
        await client.WaitAssertion(() =>
            Assert.That(client.EntMan.GetComponent<SpriteComponent>(target).PostShader, Is.Not.Null));

        await client.WaitPost(() => ApplyRecipientState(pair, target, otherRecipient));
        await client.WaitAssertion(() =>
            Assert.That(client.EntMan.GetComponent<SpriteComponent>(target).PostShader, Is.Null));

        await client.WaitPost(() => ApplyRecipientState(pair, target, null));
        await client.WaitAssertion(() =>
            Assert.That(client.EntMan.GetComponent<SpriteComponent>(target).PostShader, Is.Not.Null));

        await client.WaitPost(() =>
        {
            client.EntMan.DeleteEntity(target);
            client.EntMan.DeleteEntity(otherRecipient);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RemovingTheMarkerClearsOnlyItsOwnShader(bool anotherShader)
    {
        ProtoId<ShaderPrototype> unshadedShader = "unshaded";
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var state = await GetServerHighlightState(pair);
        var client = pair.Client;
        EntityUid target = default;

        await client.WaitPost(() =>
        {
            target = client.EntMan.SpawnEntity("ScpHighlightTestSprite", MapCoordinates.Nullspace);
            client.EntMan.AddComponent<HighlightedComponent>(target);
            ApplyState(pair, target, state);
        });
        await client.WaitAssertion(() =>
            Assert.That(client.EntMan.GetComponent<SpriteComponent>(target).PostShader, Is.Not.Null));

        await client.WaitPost(() =>
        {
            if (anotherShader)
            {
                client.EntMan.GetComponent<SpriteComponent>(target).PostShader =
                    client.ProtoMan.Index(unshadedShader).Instance();
            }

            client.EntMan.RemoveComponent<HighlightedComponent>(target);
        });
        await client.WaitAssertion(() =>
        {
            var shader = client.EntMan.GetComponent<SpriteComponent>(target).PostShader;
            if (anotherShader)
                Assert.That(shader, Is.SameAs(client.ProtoMan.Index(unshadedShader).Instance()));
            else
                Assert.That(shader, Is.Null);
        });

        await client.WaitPost(() => client.EntMan.DeleteEntity(target));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AddingASpriteAfterTheHighlightStateAppliesTheShader()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var state = await GetServerHighlightState(pair);
        var client = pair.Client;
        EntityUid target = default;

        await client.WaitPost(() =>
        {
            target = client.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            client.EntMan.AddComponent<HighlightedComponent>(target);
            ApplyState(pair, target, state);
            client.EntMan.AddComponent<SpriteComponent>(target);
        });
        await client.WaitAssertion(() =>
            Assert.That(client.EntMan.GetComponent<SpriteComponent>(target).PostShader, Is.Not.Null));

        await client.WaitPost(() => client.EntMan.DeleteEntity(target));
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ChangingTheLocalAttachmentResynchronizesRecipientHighlights(bool global)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var state = await GetServerHighlightState(pair);
        var client = pair.Client;
        var player = client.ResolveDependency<ClientPlayerManager>();
        EntityUid target = default;
        EntityUid recipient = default;
        EntityUid otherBody = default;
        EntityUid? original = null;

        await client.WaitPost(() =>
        {
            original = player.LocalEntity;
            recipient = client.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            client.EntMan.AddComponent<EyeComponent>(recipient);
            otherBody = client.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            client.EntMan.AddComponent<EyeComponent>(otherBody);
            target = client.EntMan.SpawnEntity("ScpHighlightTestSprite", MapCoordinates.Nullspace);
            client.EntMan.AddComponent<HighlightedComponent>(target);
            ApplyState(pair, target, state);
            if (!global)
                ApplyRecipientState(pair, target, recipient);
        });
        await client.WaitAssertion(() =>
        {
            var shader = client.EntMan.GetComponent<SpriteComponent>(target).PostShader;
            Assert.That(shader is not null, Is.EqualTo(global));
        });

        // Отметка уже получена; привязка игрока не должна требовать повторного сетевого состояния.
        await client.WaitPost(() => player.SetAttachedEntity(player.LocalSession, recipient, out _, true));
        await client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(player.LocalEntity, Is.EqualTo(recipient));
                Assert.That(client.EntMan.GetComponent<SpriteComponent>(target).PostShader, Is.Not.Null);
            });
        });

        await client.WaitPost(() => player.SetAttachedEntity(player.LocalSession, null, out _, true));
        await client.WaitAssertion(() =>
        {
            Assert.That(player.LocalEntity, Is.Null);
            Assert.That(client.EntMan.GetComponent<SpriteComponent>(target).PostShader is not null,
                Is.EqualTo(global));
        });

        await client.WaitPost(() => player.SetAttachedEntity(player.LocalSession, otherBody, out _, true));
        await client.WaitAssertion(() =>
        {
            Assert.That(player.LocalEntity, Is.EqualTo(otherBody));
            Assert.That(client.EntMan.GetComponent<SpriteComponent>(target).PostShader is not null,
                Is.EqualTo(global));
        });

        await client.WaitPost(() =>
        {
            player.SetAttachedEntity(player.LocalSession, original, out _, true);
            client.EntMan.DeleteEntity(target);
            client.EntMan.DeleteEntity(recipient);
            client.EntMan.DeleteEntity(otherBody);
        });
        await pair.CleanReturnAsync();
    }

    private static async Task<IComponentState> GetServerHighlightState(TestPair pair)
    {
        IComponentState? state = null;
        await pair.Server.WaitPost(() =>
        {
            var entMan = pair.Server.EntMan;
            var target = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            pair.Server.System<SharedHighlightSystem>().Highlight(target, highlightTimes: -1);
            var highlight = entMan.GetComponent<HighlightedComponent>(target);
            state = entMan.GetComponentState(entMan.EventBus, highlight, null, GameTick.Zero);
            entMan.DeleteEntity(target);
        });

        return state ?? throw new InvalidOperationException("The server did not create the highlight state.");
    }

    private static void ApplyRecipientState(TestPair pair, EntityUid target, EntityUid? recipient)
    {
        var entMan = pair.Client.EntMan;
        var highlight = entMan.GetComponent<HighlightedComponent>(target);
        var previous = highlight.Recipient;
        highlight.Recipient = recipient;
        var state = entMan.GetComponentState(entMan.EventBus, highlight, null, GameTick.Zero)
                    ?? throw new InvalidOperationException("The client did not create the recipient state.");
        highlight.Recipient = previous;
        ApplyState(pair, target, state);
    }

    private static void ApplyState(TestPair pair, EntityUid target, IComponentState state)
    {
        var highlight = pair.Client.EntMan.GetComponent<HighlightedComponent>(target);
        var handle = new ComponentHandleState(state, null);
        using var application = pair.Client.ResolveDependency<IClientGameTiming>().StartStateApplicationArea();
        pair.Client.EntMan.EventBus.RaiseComponentEvent(target, highlight, ref handle);
    }
}

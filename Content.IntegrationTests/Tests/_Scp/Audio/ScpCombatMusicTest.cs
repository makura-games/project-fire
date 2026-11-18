using System.Collections.Generic;
using Content.Client._Scp.Audio;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Scp.Mobs.Components;
using Content.Shared._Scp.ScpCCVars;
using Content.Shared.CombatMode;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Scp.Audio;

public sealed class ScpCombatMusicTest : InteractionTest
{
    private const string CombatAudioDirectory = "/Audio/_Scp/Ambience/Music/Combat/";

    protected override string PlayerPrototype => "ScpCombatMusicTestMob";

    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ScpCombatMusicTestMob
          parent: InteractionTestMob
          components:
          - type: MobState
        """;

    [Test]
    public async Task CombatStateCreatesAndRetainsOneLocalNonLoopingStream()
    {
        await Client.WaitPost(() => Client.CfgMan.SetCVar(ScpCCVars.CombatMusicEnabled, false));
        await Server.WaitPost(() => SCombatMode.SetInCombatMode(SPlayer, true));
        await Pair.RunTicksSync(5);

        await Client.WaitAssertion(() =>
            Assert.That(CEntMan.GetComponent<CombatModeComponent>(CPlayer).IsInCombatMode, Is.True));

        // В headless нет воспроизводящего устройства: проверяем поток до первого аудиокадра,
        // не меняя общий DummyAudioSource и не подменяя игровые часы.
        await Client.WaitPost(() =>
        {
            Client.CfgMan.SetCVar(ScpCCVars.CombatMusicEnabled, true);
            CEntMan.System<ScpCombatMusicSystem>().FrameUpdate(0f);
        });

        EntityUid stream = default;
        var audioStart = TimeSpan.Zero;
        await Client.WaitAssertion(() =>
        {
            var streams = GetCombatStreams();
            Assert.That(streams, Has.Count.EqualTo(1));
            stream = streams[0];
            var audio = CEntMan.GetComponent<AudioComponent>(stream);
            audioStart = audio.AudioStart;

            using var assertions = Assert.EnterMultipleScope();
            Assert.That(CEntMan.GetNetEntity(stream).IsClientSide(), Is.True);
            Assert.That(audio.Global, Is.True);
            Assert.That(audio.Params.Loop, Is.False);
            Assert.That(audio.FileName, Is.EqualTo(CombatAudioDirectory + "Fallback.ogg"));
            Assert.That(audio.Params.Volume, Is.EqualTo(float.NegativeInfinity));
        });

        await Client.WaitPost(() =>
        {
            var combatMode = CEntMan.System<SharedCombatModeSystem>();
            var music = CEntMan.System<ScpCombatMusicSystem>();
            combatMode.SetInCombatMode(CPlayer, false);
            music.FrameUpdate(0f);
            combatMode.SetInCombatMode(CPlayer, true);
            music.FrameUpdate(0f);
        });

        await Client.WaitAssertion(() =>
        {
            using var assertions = Assert.EnterMultipleScope();
            var streams = GetCombatStreams();
            Assert.That(streams, Has.Count.EqualTo(1));
            Assert.That(streams, Does.Contain(stream));
            Assert.That(CEntMan.GetComponent<AudioComponent>(stream).AudioStart, Is.EqualTo(audioStart));
        });

        await Client.WaitPost(() => Client.CfgMan.SetCVar(ScpCCVars.CombatMusicEnabled, false));
        await Client.WaitAssertion(() =>
        {
            using var assertions = Assert.EnterMultipleScope();
            Assert.That(CEntMan.IsQueuedForDeletion(stream), Is.True);
            Assert.That(GetCombatStreams(), Is.Empty);
        });

        await Client.WaitPost(() =>
        {
            Client.CfgMan.SetCVar(ScpCCVars.CombatMusicEnabled, true);
            CEntMan.System<ScpCombatMusicSystem>().FrameUpdate(0f);
        });

        await Client.WaitAssertion(() =>
        {
            var streams = GetCombatStreams();
            Assert.That(streams, Has.Count.EqualTo(1));
            Assert.That(streams[0], Is.Not.EqualTo(stream));
        });

        await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(ServerSession, null));
        await Pair.RunTicksSync(5);
        await Client.WaitAssertion(() => Assert.That(GetCombatStreams(), Is.Empty));
        await Client.WaitPost(() => Client.CfgMan.SetCVar(ScpCCVars.CombatMusicEnabled, false));
    }

    [Test]
    public async Task ScpBodiesStartMusicFromTheirOwnCollection()
    {
        await Client.WaitPost(() => Client.CfgMan.SetCVar(ScpCCVars.CombatMusicEnabled, false));
        await Server.WaitPost(() =>
        {
            SEntMan.AddComponent<ScpComponent>(SPlayer).Class = Classification.Safe;
            SCombatMode.SetInCombatMode(SPlayer, true);
        });
        await Pair.RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            using var assertions = Assert.EnterMultipleScope();
            Assert.That(CEntMan.HasComponent<ScpComponent>(CPlayer), Is.True);
            Assert.That(CEntMan.GetComponent<CombatModeComponent>(CPlayer).IsInCombatMode, Is.True);
        });

        await Client.WaitPost(() =>
        {
            Client.CfgMan.SetCVar(ScpCCVars.CombatMusicEnabled, true);
            CEntMan.System<ScpCombatMusicSystem>().FrameUpdate(0f);
        });
        await Client.WaitAssertion(() =>
        {
            var streams = GetCombatStreams();
            Assert.That(streams, Has.Count.EqualTo(1));
            var audio = CEntMan.GetComponent<AudioComponent>(streams[0]);
            Assert.That(audio.FileName,
                Is.AnyOf(
                    CombatAudioDirectory + "SCP1.ogg",
                    CombatAudioDirectory + "SCP2.ogg",
                    CombatAudioDirectory + "SCP3.ogg"));
        });
        await Client.WaitPost(() => Client.CfgMan.SetCVar(ScpCCVars.CombatMusicEnabled, false));
    }

    private List<EntityUid> GetCombatStreams()
    {
        var streams = new List<EntityUid>();
        var query = CEntMan.EntityQueryEnumerator<AudioComponent>();
        while (query.MoveNext(out var uid, out var audio))
        {
            var fileName = audio.FileName;
            if (fileName.StartsWith(CombatAudioDirectory, StringComparison.Ordinal) &&
                !CEntMan.IsQueuedForDeletion(uid))
            {
                streams.Add(uid);
            }
        }

        return streams;
    }
}

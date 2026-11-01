#nullable enable
using System.Collections.Generic;
using Content.Shared._Scp.Audio.CombatMusic;
using Content.Shared._Scp.Chaos;
using Content.Shared._Scp.Mobs.Components;
using Content.Shared._Scp.Scp035;
using Content.Shared._Scp.Scp049;
using Content.Shared.Mind;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Scp.Audio;

[TestFixture]
public sealed class ScpCombatMusicGroupTests
{
    [Test]
    public async Task JobsSelectTheirDepartmentMusicWithPriority()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mindSystem = server.System<SharedMindSystem>();
        var roleSystem = server.System<SharedRoleSystem>();
        var musicSystem = server.System<SharedScpCombatMusicSystem>();
        List<EntityUid> created = [];
        (string? Job, string Music)[] cases =
        [
            ("Director", "ScpCombatMusicAdmin"),
            ("ScientificServiceDirector", "ScpCombatMusicAdmin"),
            ("FondSecurityOfficer", "ScpCombatMusicSecurity"),
            ("HeavyContainmentZoneCommandant", "ScpCombatMusicSecurity"),
            ("ExternalAdministrativeZoneCommandant", "ScpCombatMusicSecurity"),
            ("ClassD", "ScpCombatMusicCivilian"),
            ("MedicalSpecialist", "ScpCombatMusicCivilian"),
            ("NTFLeader", "ScpCombatMusicMTF"),
            ("NTFCadet", "ScpCombatMusicMTF"),
            (null, "ScpCombatMusicFallback"),
        ];
        var subjects = new EntityUid[cases.Length];

        await server.WaitPost(() =>
        {
            for (var i = 0; i < cases.Length; i++)
            {
                subjects[i] = SpawnHuman(entMan, mindSystem, roleSystem, cases[i].Job, created).Body;
            }
        });

        // Выбор группы обрабатывается сервером на следующем тике после добавления профессии.
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            for (var i = 0; i < cases.Length; i++)
            {
                var (job, expectedMusic) = cases[i];
                AssertMusic(musicSystem, subjects[i], expectedMusic, job ?? "Без профессии");
            }
        });

        await server.WaitPost(() => Cleanup(entMan, mindSystem, created));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ChaosMembershipOverridesTheJobMusic()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var musicSystem = server.System<SharedScpCombatMusicSystem>();
        var mindSystem = server.System<SharedMindSystem>();
        List<EntityUid> created = [];
        EntityUid body = default;

        await server.WaitPost(() =>
        {
            body = SpawnHuman(entMan,
                    mindSystem,
                    server.System<SharedRoleSystem>(),
                    "Director",
                    created)
                .Body;
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            AssertMusic(musicSystem, body, "ScpCombatMusicAdmin");
        });

        await server.WaitPost(() => entMan.AddComponent<ChaosSpyMobComponent>(body));
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            AssertMusic(musicSystem, body, "ScpCombatMusicChaos");
        });

        await server.WaitPost(() => Cleanup(entMan, mindSystem, created));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ScpsHaveNoCombatMusicEvenWithAJob()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var musicSystem = server.System<SharedScpCombatMusicSystem>();
        var mindSystem = server.System<SharedMindSystem>();
        List<EntityUid> created = [];
        EntityUid body = default;

        await server.WaitPost(() =>
        {
            body = SpawnHuman(entMan,
                    mindSystem,
                    server.System<SharedRoleSystem>(),
                    "Director",
                    created)
                .Body;
            entMan.AddComponent<ScpComponent>(body);
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var found = musicSystem.TryGetMusic(body, out var music);
            using var scope = Assert.EnterMultipleScope();
            Assert.That(found, Is.False);
            Assert.That(music, Is.Null);
        });

        await server.WaitPost(() => Cleanup(entMan, mindSystem, created));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TransformedBodiesKeepTheirProfessionMusicAfterMindReplacement()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var mindSystem = server.System<SharedMindSystem>();
        var roleSystem = server.System<SharedRoleSystem>();
        var musicSystem = server.System<SharedScpCombatMusicSystem>();
        List<EntityUid> created = [];
        (EntityUid Body, EntityUid Mind) maskUser = default;
        (EntityUid Body, EntityUid Mind) minion = default;
        var replacementMinds = new EntityUid[2];

        await server.WaitPost(() =>
        {
            maskUser = SpawnHuman(entMan, mindSystem, roleSystem, "FondSecurityOfficer", created);
            minion = SpawnHuman(entMan, mindSystem, roleSystem, "MedicalSpecialist", created);
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            AssertMusic(musicSystem, maskUser.Body, "ScpCombatMusicSecurity");
            AssertMusic(musicSystem, minion.Body, "ScpCombatMusicCivilian");
        });

        await server.WaitPost(() =>
        {
            entMan.AddComponent<Scp035MaskUserComponent>(maskUser.Body);
            entMan.AddComponent<Scp049MinionComponent>(minion.Body);

            // Разум захватившего тело игрока может не иметь профессии прежнего владельца.
            var originals = new[] { maskUser, minion };
            for (var i = 0; i < originals.Length; i++)
            {
                var (body, oldMind) = originals[i];
                mindSystem.TransferTo(oldMind, null, createGhost: false);
                var newMind = mindSystem.CreateMind(null);
                mindSystem.TransferTo(newMind, body);
                replacementMinds[i] = newMind.Owner;
                created.Add(newMind.Owner);
            }
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            AssertMusic(musicSystem, maskUser.Body, "ScpCombatMusicSecurity");
            AssertMusic(musicSystem, minion.Body, "ScpCombatMusicCivilian");
        });

        await server.WaitPost(() =>
        {
            foreach (var mind in replacementMinds)
            {
                roleSystem.MindAddJobRole(mind, jobPrototype: "Director");
            }
        });
        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            AssertMusic(musicSystem, maskUser.Body, "ScpCombatMusicAdmin");
            AssertMusic(musicSystem, minion.Body, "ScpCombatMusicAdmin");
        });

        await server.WaitPost(() => Cleanup(entMan, mindSystem, created));
        await pair.CleanReturnAsync();
    }

    private static (EntityUid Body, EntityUid Mind) SpawnHuman(
        EntityManager entMan,
        SharedMindSystem mindSystem,
        SharedRoleSystem roleSystem,
        string? job,
        List<EntityUid> created)
    {
        var body = entMan.SpawnEntity("MobHuman", MapCoordinates.Nullspace);
        var mind = mindSystem.CreateMind(null);
        created.Add(body);
        created.Add(mind.Owner);
        mindSystem.TransferTo(mind, body);
        if (job != null)
        {
            roleSystem.MindAddJobRole(mind, jobPrototype: job);
        }

        return (body, mind.Owner);
    }

    private static void Cleanup(EntityManager entMan, SharedMindSystem mindSystem, List<EntityUid> created)
    {
        foreach (var entity in created)
        {
            mindSystem.WipeMind(entity);
        }

        foreach (var entity in created)
        {
            entMan.DeleteEntity(entity);
        }
    }

    private static void AssertMusic(
        SharedScpCombatMusicSystem musicSystem,
        EntityUid body,
        string expectedMusic,
        string? context = null)
    {
        var found = musicSystem.TryGetMusic(body, out var music);
        using var scope = Assert.EnterMultipleScope();
        Assert.That(found, Is.True, context);
        Assert.That(music?.ID, Is.EqualTo(expectedMusic), context);
    }
}

using Content.Shared._Scp.Audio.CombatMusic;
using Content.Shared._Scp.Chaos;
using Content.Shared._Scp.Scp035;
using Content.Shared._Scp.Scp049;
using Content.Shared.CombatMode;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Server.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Scp.Audio.CombatMusic;

/// <summary>
/// Selects music by job and replicates the selected group to the body's owner.
/// </summary>
public sealed partial class ScpCombatMusicSystem : EntitySystem
{
    [Dependency] private SharedMindSystem _mind = null!;
    [Dependency] private SharedJobSystem _job = null!;
    [Dependency] private IPlayerManager _player = null!;
    [Dependency] private IPrototypeManager _prototype = null!;
    [Dependency] private IGameTiming _timing = null!;

    private static readonly ProtoId<ScpCombatMusicPrototype> ChaosMusic = "ScpCombatMusicChaos";
    private static readonly TimeSpan RoleRefreshInterval = TimeSpan.FromSeconds(1);

    private readonly HashSet<EntityUid> _pendingUpdates = [];
    private TimeSpan _nextRoleRefresh;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MindContainerComponent, MindAddedMessage>(OnMindAdded);
        SubscribeLocalEvent<RoleAddedEvent>(OnRoleAdded);
        SubscribeLocalEvent<RoleRemovedEvent>(OnRoleRemoved);
        SubscribeLocalEvent<MobChaosRaiderComponent, ComponentStartup>(OnGroupStartup);
        SubscribeLocalEvent<ChaosSpyMobComponent, ComponentStartup>(OnGroupStartup);
        SubscribeLocalEvent<ChaosSleepSpyMobComponent, ComponentStartup>(OnGroupStartup);
    }

    public override void Shutdown()
    {
        _pendingUpdates.Clear();
        base.Shutdown();
    }

    private void OnMindAdded(Entity<MindContainerComponent> ent, ref MindAddedMessage args)
    {
        _pendingUpdates.Add(ent);
    }

    private void OnRoleAdded(RoleAddedEvent args)
    {
        QueueMindBody(args.Mind);
    }

    private void OnRoleRemoved(RoleRemovedEvent args)
    {
        QueueMindBody(args.Mind);
    }

    private void OnGroupStartup<T>(Entity<T> ent, ref ComponentStartup args) where T : IComponent
    {
        _pendingUpdates.Add(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Замена JobPrototype у существующей mind-role не поднимает RoleAddedEvent.
        // Проверяем только занятые игроками тела раз в секунду.
        if (_timing.CurTime >= _nextRoleRefresh)
        {
            _nextRoleRefresh = _timing.CurTime + RoleRefreshInterval;
            foreach (var session in _player.Sessions)
            {
                if (session.AttachedEntity is { } body)
                    _pendingUpdates.Add(body);
            }
        }

        foreach (var body in _pendingUpdates)
        {
            if (!TerminatingOrDeleted(body) && HasComp<CombatModeComponent>(body))
                RefreshMusic(body);
        }

        _pendingUpdates.Clear();
    }

    private void QueueMindBody(MindComponent mind)
    {
        if (mind.OwnedEntity is { } body)
            _pendingUpdates.Add(body);
    }

    private void RefreshMusic(EntityUid uid)
    {
        JobPrototype? job = null;
        if (_mind.TryGetMind(uid, out var mind, out _))
            _job.MindTryGetJob(mind, out job);

        // После захвата тела новый разум может не иметь прежней профессии.
        if (job == null &&
            (HasComp<Scp035MaskUserComponent>(uid) || HasComp<Scp049MinionComponent>(uid)) &&
            TryComp<ScpCombatMusicComponent>(uid, out var existing) &&
            existing.Music != null)
        {
            return;
        }

        var music = SharedScpCombatMusicSystem.FallbackMusic;

        if (HasComp<MobChaosRaiderComponent>(uid) ||
            HasComp<ChaosSpyMobComponent>(uid) ||
            HasComp<ChaosSleepSpyMobComponent>(uid))
        {
            music = ChaosMusic;
        }
        else if (job != null)
        {
            ScpCombatMusicPrototype? selected = null;

            foreach (var candidate in _prototype.EnumeratePrototypes<ScpCombatMusicPrototype>())
            {
                if (selected != null && candidate.Priority <= selected.Priority)
                    continue;

                foreach (var departmentId in candidate.Departments)
                {
                    if (!_prototype.Index(departmentId).Roles.Contains(job.ID))
                        continue;

                    selected = candidate;
                    break;
                }
            }

            if (selected != null)
                music = selected.ID;
        }

        var component = EnsureComp<ScpCombatMusicComponent>(uid);
        if (component.Music == music)
            return;

        component.Music = music;
        Dirty(uid, component);
    }
}

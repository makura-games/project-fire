using System.Linq;
using Content.Server._Scp.GameTicking.Rules.Components;
using Content.Server.Antag;
using Content.Server.Antag.Components;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Mind;
using Content.Server.Objectives;
using Content.Shared.Objectives.Components;
using Content.Server.Roles;
using Content.Server.RoundEnd;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared._Scp.Chaos;
using Content.Shared._Scp.Fear.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Zombies;
using Robust.Shared.Audio;
using Robust.Shared.Timing;
using Content.Server._Scp.MetaGarbage;

namespace Content.Server._Scp.GameTicking.Rules;

public sealed class ChaosRaidRuleSystem : GameRuleSystem<ChaosRaidRuleComponent>
{
    [Dependency] private readonly AntagSelectionSystem _antag = default!;
    [Dependency] private readonly ObjectivesSystem _objectives = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly RoundEndSystem _roundEnd = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MobChaosRaiderComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MobChaosRaiderComponent, ComponentRemove>(OnComponentRemove);
        SubscribeLocalEvent<MobChaosRaiderComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<MobChaosRaiderComponent, EntityZombifiedEvent>(OnRaiderZombified);
        SubscribeLocalEvent<MobChaosRaiderComponent, EntParentChangedMessage>(OnEntParentChanged);

        SubscribeLocalEvent<ChaosRaidRuleComponent, AfterAntagEntitySelectedEvent>(OnAfterAntagEntSelected);
        SubscribeLocalEvent<ChaosRaiderRoleComponent, GetBriefingEvent>(OnGetBriefing);
    }

    protected override void Started(EntityUid uid,
        ChaosRaidRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        var eligible = new List<Entity<StationEventEligibleComponent, NpcFactionMemberComponent>>();
        var eligibleQuery = EntityQueryEnumerator<StationEventEligibleComponent, NpcFactionMemberComponent>();
        while (eligibleQuery.MoveNext(out var eligibleUid, out var eligibleComp, out var member))
        {
            if (!_npcFaction.IsFactionHostile(component.Faction, (eligibleUid, member)))
                continue;

            eligible.Add((eligibleUid, eligibleComp, member));
        }

        if (eligible.Count == 0)
            return;

        component.TargetComplex = eligible[0];

        // Выключаем перенос мусора из этого раунда (иначе будет очень много мусора от рейдеров)
        if (TryComp<MetaGarbageTargetComponent>(component.TargetComplex, out var metaGarbageTargetComp))
        {
            metaGarbageTargetComp.SpawnPercent = 0f;
            metaGarbageTargetComp.ReagentSaveModifiers.Clear();
        }

        AddRaidHelpObjectiveToExistingSpies();
    }

    protected override void AppendRoundEndText(EntityUid uid,
        ChaosRaidRuleComponent component,
        GameRuleComponent gameRule,
        ref RoundEndTextAppendEvent args)
    {
        if (component.Objectives == null)
            return;

        CalculateObjectivesProgress(component);
        CalculateFinalWinArgs(component);

        var winText = Loc.GetString($"chaos-raid-{component.WinType.ToString().ToLower()}");
        args.AddLine(winText);

        foreach (var cond in component.WinConditions)
        {
            var text = Loc.GetString($"chaos-raid-cond-{cond.ToString().ToLower()}");
            args.AddLine(text);
        }

        args.AddLine(Loc.GetString("chaos-raid-list-completed-objectives-count",
            ("objectivesCount", component.Objectives.Count()), ("completedCount", component.CompletedObjectivesCount)
        ));

        args.AddLine(Loc.GetString("chaos-raid-list-objective-data-start"));
        int objectiveNum = 1;
        foreach (var (objectiveId, progress) in component.Objectives)
        {
            var objectiveStatus = Loc.GetString("chaos-raid-list-objective-status-failed");
            if (progress > 0.999f)
                objectiveStatus = Loc.GetString("chaos-raid-list-objective-status-successfully");

            args.AddLine(Loc.GetString("chaos-raid-list-objective-data",
                ("objectiveNumber", objectiveNum), ("objectiveName", MetaData(objectiveId).EntityName), ("objectiveStatus", objectiveStatus)
            ));
            objectiveNum++;
        }

        args.AddLine("");
        args.AddLine(Loc.GetString("chaos-raid-list-start"));

        var antags = _antag.GetAntagIdentifiers(uid);

        foreach (var (_, sessionData, name) in antags)
        {
            args.AddLine(Loc.GetString("chaos-raid-list-name-user", ("name", name), ("user", sessionData.UserName)));
        }
        args.AddLine("");
    }

    private void OnMapInit(Entity<MobChaosRaiderComponent> ent, ref MapInitEvent args)
    {
        RemCompDeferred<FearComponent>(ent); // ПОВСТАНЦЫ БЕЗ СТРАХА!
    }

    private void OnComponentRemove(Entity<MobChaosRaiderComponent> ent, ref ComponentRemove args)
    {
        if (TryComp<MobStateComponent>(ent, out var stateComp) && stateComp.CurrentState != MobState.Dead)
            ChangeAliveRaidersCount(-1);
    }

    private void OnMobStateChanged(Entity<MobChaosRaiderComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            ChangeAliveRaidersCount(-1);
        else if (args.OldMobState == MobState.Dead && args.NewMobState != MobState.Dead)
            ChangeAliveRaidersCount(1);
    }

    private void OnRaiderZombified(Entity<MobChaosRaiderComponent> ent, ref EntityZombifiedEvent args)
    {
        RemCompDeferred<MobChaosRaiderComponent>(ent);
    }

    private void OnEntParentChanged(Entity<MobChaosRaiderComponent> ent, ref EntParentChangedMessage args)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out _, out var _, out var chaos, out _))
        {
            if (chaos.TargetComplex == null)
                return;

            if (chaos.TargetEnterAnnounced)
                return;

            if (!args.OldMapId.HasValue)
                return;

            if (args.Transform.MapID != Transform(chaos.TargetComplex.Value).MapID)
                return;

            var station = _station.GetOwningStation(ent, args.Transform);
            if (!station.HasValue)
                return;

            _chat.DispatchStationAnnouncement(ent,
                Loc.GetString("chaos-announce-on-spawn"),
                Loc.GetString("scp-announce-on-spawn-source-name"),
                colorOverride: Color.FromHex("#016900"),
                announceVoice: "Hanson",
                announcementSound: new SoundPathSpecifier("/Audio/_Scp/Effects/Announcement/mtf.ogg", new AudioParams { Volume = -5f }));

            chaos.TargetEnterAnnounced = true;
        }
    }

    private void OnAfterAntagEntSelected(Entity<ChaosRaidRuleComponent> ent, ref AfterAntagEntitySelectedEvent args)
    {
        var target = (ent.Comp.TargetComplex is not null) ? Name(ent.Comp.TargetComplex.Value) : "the target";

        _antag.SendBriefing(args.Session,
            Loc.GetString("chaos-raider-welcome",
                ("station", target),
                ("name", Name(ent))),
            Color.Red,
            ent.Comp.GreetSoundNotification);

        ent.Comp.RoundstartRaidersCount++;
        ent.Comp.AliveRaidersCount++;

        if (!_mind.TryGetMind(args.EntityUid, out var mindId, out var mind))
            return;

        // TODO: Переделать эту систему, что бы она работала иначе, брав точки отдельно на шаттле, а так же базе повстанцев хаоса, а не на всей карте.
        // Все StealArea-точки на этой карте (база повстанец хаоса) пренадлежат Повстанцам Хаоса
        var query = AllEntityQuery<StealAreaComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var stealAreaComp, out var xform))
        {
            if (Transform(args.EntityUid).MapID != xform.MapID)
                continue;

            stealAreaComp.Owners.Add(mindId);
            stealAreaComp.OwnerCount = stealAreaComp.Owners.Count;
            Dirty(uid, stealAreaComp);
        }

        if (ent.Comp.Objectives != null)
        {
            mind.Objectives.Clear();
            foreach (var (uid, _) in ent.Comp.Objectives)
            {
                mind.Objectives.Add(uid);
            }
            return;
        }

        ent.Comp.Objectives ??= new();
        foreach (var objective in mind.Objectives)
        {
            ent.Comp.Objectives.TryAdd(objective, 0f);
        }
    }

    private void OnGetBriefing(Entity<ChaosRaiderRoleComponent> ent, ref GetBriefingEvent args)
    {
        args.Append(Loc.GetString("chaos-raider-briefing"));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var chaos, out _))
        {
            if (_gameTiming.CurTime < chaos.NextObjectivesCheck)
                continue;

            chaos.NextObjectivesCheck = _gameTiming.CurTime + chaos.ObjectivesCheckInterval;

            CheckShouldBeEndRound((uid, chaos));
        }
    }

    private void ChangeAliveRaidersCount(int amount)
    {
        var query = QueryActiveRules();
        while (query.MoveNext(out var uid, out _, out var chaos, out _))
        {
            chaos.AliveRaidersCount = Math.Max(0, chaos.AliveRaidersCount + amount);

            if (chaos.AliveRaidersCount == 0)
                CheckShouldBeEndRound((uid, chaos));
        }
    }

    private void CheckShouldBeEndRound(Entity<ChaosRaidRuleComponent> ent)
    {
        if (ent.Comp.RoundstartRaidersCount == 0 || ent.Comp.Objectives == null)
            return;

        CalculateObjectivesProgress(ent);

        if (ent.Comp.AliveRaidersCount > 0 &&
            ent.Comp.CompletedObjectivesCount < ent.Comp.Objectives.Count())
            return;

        if (ent.Comp.RoundEndBehavior == RoundEndBehavior.Nothing)
            return;

        _roundEnd.DoRoundEndBehavior(ent.Comp.RoundEndBehavior,
            ent.Comp.EvacShuttleTime,
            ent.Comp.RoundEndTextSender,
            ent.Comp.RoundEndTextShuttleCall,
            ent.Comp.RoundEndTextAnnouncement);

        ent.Comp.RoundEndBehavior = RoundEndBehavior.Nothing;
    }

    private void CalculateObjectivesProgress(ChaosRaidRuleComponent ruleComp)
    {
        var objectives = new Dictionary<EntityUid, float>();
        var completedObjectivesCount = 0;

        var query = EntityQueryEnumerator<MobChaosRaiderComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!_mind.TryGetMind(uid, out _, out var mind))
                continue;

            foreach (var objective in mind.Objectives)
            {
                var progress = _objectives.GetProgress(objective, (uid, mind)) ?? 0f;

                objectives.TryAdd(objective, 0f);
                objectives[objective] += progress;
            }
        }

        foreach (var (key, progress) in objectives)
        {
            if (ruleComp.Objectives != null)
                ruleComp.Objectives[key] = progress;

            if (progress >= 0.999f)
                completedObjectivesCount++;
        }

        ruleComp.CompletedObjectivesCount = completedObjectivesCount;
    }

    private void CalculateFinalWinArgs(ChaosRaidRuleComponent comp)
    {
        if (comp.WinType == ChaosWinType.ChaosMajor || comp.WinType == ChaosWinType.CrewMajor)
            return;

        if (comp.Objectives == null)
            return;

        var halfRaiders = Math.Max(1, (comp.RoundstartRaidersCount + 1) / 2);
        var halfObjectives = Math.Max(1, (comp.Objectives.Count() + 1) / 2);

        if (comp.CompletedObjectivesCount >= comp.Objectives.Count() &&
            comp.Objectives.Count() > 0)
            AddWinCondition(comp, ChaosWinCondition.ChaosRaidersCompleteAllObjectives);

        if (comp.AliveRaidersCount <= 0)
        {
            AddWinCondition(comp, ChaosWinCondition.CrewKillAllChaosRaiders);
            comp.WinType = ChaosWinType.CrewMajor;
            return;
        }

        if (comp.WinConditions.Contains(ChaosWinCondition.ChaosRaidersCompleteAllObjectives) &&
            comp.RoundstartRaidersCount > 1 &&
            comp.AliveRaidersCount >= halfRaiders)
            comp.WinType = ChaosWinType.ChaosMajor;
        else if (comp.CompletedObjectivesCount >= halfObjectives)
            comp.WinType = ChaosWinType.ChaosMinor;
        else if (comp.AliveRaidersCount < halfRaiders)
            comp.WinType = ChaosWinType.CrewMinor;
    }

    private void AddWinCondition(ChaosRaidRuleComponent comp, ChaosWinCondition winCondition)
    {
        if (!comp.WinConditions.Contains(winCondition))
            comp.WinConditions.Add(winCondition);
    }

    private void AddRaidHelpObjectiveToExistingSpies()
    {
        var spyRuleQuery = EntityQueryEnumerator<ChaosSpyRuleComponent, AntagSelectionComponent>();
        while (spyRuleQuery.MoveNext(out _, out var spyRule, out var antag))
        {
            foreach (var (mindId, _) in antag.AssignedMinds)
            {
                if (!TryComp<MindComponent>(mindId, out var mind))
                    continue;

                _mind.TryAddObjective(mindId, mind, spyRule.ChaosRaidHelpObjectiveProtoId);
            }
        }
    }
}

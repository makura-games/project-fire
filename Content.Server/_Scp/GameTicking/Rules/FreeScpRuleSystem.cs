using System.Linq;
using Content.Server._Scp.BodyTakeover;
using Content.Server.Fax;
using Content.Server.GameTicking.Rules;
using Content.Server.Mind;
using Content.Server.Station.Systems;
using Content.Shared._Scp.FreeScp;
using Content.Shared._Scp.GameTicking.Rules;
using Content.Shared._Scp.Mobs.Components;
using Content.Shared.Fax.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mind.Components;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Scp.GameTicking.Rules;

public sealed partial class FreeScpRuleSystem : GameRuleSystem<FreeScpRuleComponent>
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private BodyTakeoverPollSystem _bodyTakeover = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private FaxSystem _fax = default!;
    [Dependency] private StationJobsSystem _stationJobs = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    private readonly List<JobPrototype> _cachedScpJobs = new();

    public override void Initialize()
    {
        base.Initialize();
        CacheScpJobs();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => CacheScpJobs());
    }

    protected override void Started(EntityUid uid, FreeScpRuleComponent comp, GameRuleComponent rule, GameRuleStartedEvent args)
    {
        base.Started(uid, comp, rule, args);
        comp.Phase = FreeScpRulePhase.WaitingForCheck;
        comp.Deadline = _timing.CurTime + comp.CheckDelay;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<FreeScpRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var comp, out var rule))
        {
            if (!GameTicker.IsGameRuleActive(uid, rule))
                continue;

            if (comp.Phase == FreeScpRulePhase.Finished)
                continue;

            if (comp.Deadline is not { } deadline || _timing.CurTime < deadline)
                continue;

            comp.Deadline = null;

            switch (comp.Phase)
            {
                case FreeScpRulePhase.WaitingForCheck:
                    HandleInitialCheck(uid, comp);
                    break;
                case FreeScpRulePhase.Finished:
                    break;
            }
        }
    }

    private void CacheScpJobs()
    {
        _cachedScpJobs.Clear();
        var scpCompName = _componentFactory.GetComponentName(typeof(ScpComponent));

        foreach (var job in _prototype.EnumeratePrototypes<JobPrototype>())
        {
            if (job.JobEntity == null)
                continue;

            if (!_prototype.TryIndex(job.JobEntity, out var entity))
                continue;

            if (!entity.Components.ContainsKey(scpCompName))
                continue;

            _cachedScpJobs.Add(job);
        }
    }

    private void HandleInitialCheck(EntityUid uid, FreeScpRuleComponent comp)
    {
        if (AnyScpInRound())
        {
            comp.Phase = FreeScpRulePhase.Finished;
            return;
        }

        var availableJobs = GetAllowedScpJobs();
        if (availableJobs.Count == 0)
        {
            comp.Phase = FreeScpRulePhase.Finished;
            SendDirectorFax();
            return;
        }

        var job = _random.Pick(availableJobs);
        var coords = GameTicker.GetObserverSpawnPoint();
        if (coords == default)
        {
            comp.Phase = FreeScpRulePhase.Finished;
            return;
        }

        var scpEntity = Spawn(job.JobEntity, coords);
        comp.Phase = FreeScpRulePhase.Finished;

        _bodyTakeover.StartPoll(
            scpEntity,
            job.LocalizedName,
            comp.PollDuration,
            comp.TransferDelay,
            onSuccess: session =>
            {
                var (mindId, mind) = _mind.GetOrCreateMind(session.UserId);
                _mind.TransferTo(mindId, scpEntity, ghostCheckOverride: true, mind: mind);
            },
            onFailed: () =>
            {
                QueueDel(scpEntity);
                SendDirectorFax();
            });
    }

    private bool AnyScpInRound()
    {
        var query = EntityQueryEnumerator<ScpComponent, MindContainerComponent>();
        while (query.MoveNext(out _, out _, out var mindContainer))
        {
            if (mindContainer.HasMind)
                return true;
        }
        return false;
    }

    private void SendDirectorFax()
    {
        var content = Loc.GetString("free-scp-no-volunteers-fax-content");
        var name = Loc.GetString("free-scp-no-volunteers-fax-name");
        var printout = new FaxPrintout(content, name);

        var query = EntityQueryEnumerator<ScpDirectorFaxComponent, FaxMachineComponent>();
        while (query.MoveNext(out var faxUid, out _, out var faxComp))
        {
            _fax.Receive(faxUid, printout, component: faxComp);
            break;
        }
    }

    private List<JobPrototype> GetAllowedScpJobs()
    {
        var stations = _station.GetStationsSet();
        var result = new List<JobPrototype>();

        foreach (var job in _cachedScpJobs)
        {
            if (!stations.Any())
            {
                result.Add(job);
                continue;
            }

            foreach (var station in stations)
            {
                if (!_stationJobs.TryGetJobSlot(station, job, out var slots))
                {
                    result.Add(job);
                    break;
                }

                if (slots > 0)
                {
                    result.Add(job);
                    break;
                }
            }
        }

        return result;
    }
}

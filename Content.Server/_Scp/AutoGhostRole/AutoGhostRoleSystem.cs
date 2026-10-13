using Content.Server._Scp.BodyTakeover;
using Content.Server.Mind;
using Content.Server.Station.Systems;
using Content.Shared._Scp.AutoGhostRole;
using Content.Shared._Scp.ScpCCVars;
using Content.Shared._Scp.Scp106.Components;
using Content.Shared.SSDIndicator;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Scp.AutoGhostRole;

/// <summary>
/// Monitors entities with <see cref="AutoGhostRoleComponent"/> for player disconnects
/// and starts a body takeover poll after the configured delay
/// </summary>
public sealed partial class AutoGhostRoleSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private BodyTakeoverPollSystem _bodyTakeover = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private StationSystem _station = default!;

    private EntityQuery<SSDIndicatorComponent> _ssdQuery;

    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        _ssdQuery = GetEntityQuery<SSDIndicatorComponent>();

        SubscribeLocalEvent<AutoGhostRoleComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<AutoGhostRoleComponent, PlayerDetachedEvent>(OnPlayerDetached);

        Subs.CVar(_cfg, ScpCCVars.AutoGhostRoleEnabled, v => _enabled = v, true);
    }

    private void OnPlayerAttached(Entity<AutoGhostRoleComponent> ent, ref PlayerAttachedEvent args)
    {
        ent.Comp.EverHadPlayer = true;
        RemCompDeferred<ActiveAutoGhostRoleComponent>(ent.Owner);

        // Cancel any active poll since a player has returned
        _bodyTakeover.CancelPoll(ent.Owner);
    }

    private void OnPlayerDetached(Entity<AutoGhostRoleComponent> ent, ref PlayerDetachedEvent args)
    {
        if (!ent.Comp.EverHadPlayer)
            return;

        var active = AddComp<ActiveAutoGhostRoleComponent>(ent.Owner);
        active.DisconnectedAt = _timing.CurTime;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_enabled)
            return;

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<ActiveAutoGhostRoleComponent, AutoGhostRoleComponent>();

        while (query.MoveNext(out var uid, out var active, out var comp))
        {
            if (curTime < active.DisconnectedAt + comp.DisconnectDelay)
                continue;

            RemCompDeferred<ActiveAutoGhostRoleComponent>(uid);

            if (!ShouldStartPoll(uid, comp))
                continue;

            StartPoll(uid, comp);
        }
    }

    private bool ShouldStartPoll(EntityUid uid, AutoGhostRoleComponent comp)
    {
        // Never give away entities that have never had a player
        if (!comp.EverHadPlayer)
            return false;

        // SCP-106 phantom check: if this body has an active phantom, don't replace the main body
        var ev = new AutoGhostRolePollAttemptEvent();
        RaiseLocalEvent(uid, ev);
        if (ev.Cancelled)
            return false;

        // Only poll for entities on a station grid, not admin arenas
        if (!IsOnStation(uid))
            return false;

        return true;
    }

    private bool IsOnStation(EntityUid uid)
    {
        var owningStation = _station.GetOwningStation(uid);
        return owningStation.HasValue && _station.GetStationsSet().Contains(owningStation.Value);
    }

    private void StartPoll(EntityUid uid, AutoGhostRoleComponent comp)
    {
        var entityName = MetaData(uid).EntityName;

        _bodyTakeover.StartPoll(
            uid,
            entityName,
            comp.PollDuration,
            comp.TransferDelay,
            onSuccess: session =>
            {
                if (Deleted(uid))
                    return;

                comp.DisconnectedAt = null;
                comp.EverHadPlayer = false;

                var (mindId, mind) = _mind.GetOrCreateMind(session.UserId);
                _mind.TransferTo(mindId, uid, ghostCheckOverride: true, mind: mind);
            },
            onFailed: () =>
            {
                // Nobody took the role. Entity remains unmanned
            });
    }
}

using Content.Server.EUI;
using Content.Shared._Scp.BodyTakeover;
using Content.Shared.Ghost;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Server.Player;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Scp.BodyTakeover;

/// <summary>
/// Generic system for polling players to take over a body.
/// Polls ghosts first, then living players if no ghost accepts.
/// Used by Free SCP, Auto Ghost Role, and future MTF auto-call systems.
/// </summary>
public sealed partial class BodyTakeoverPollSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private EuiManager _eui = default!;

    private EntityQuery<BodyTakeoverPollStateComponent> _pollQuery;
    private EntityQuery<GhostComponent> _ghostQuery;
    private EntityQuery<MobStateComponent> _mobStateQuery;

    public override void Initialize()
    {
        base.Initialize();

        _pollQuery = GetEntityQuery<BodyTakeoverPollStateComponent>();
        _ghostQuery = GetEntityQuery<GhostComponent>();
        _mobStateQuery = GetEntityQuery<MobStateComponent>();
    }

    /// <summary>
    /// Starts a body takeover poll for the given entity.
    /// Ghosts are polled first, then living players if no ghost accepts.
    /// </summary>
    /// <param name="target">The entity to transfer the winner into.</param>
    /// <param name="entityName">Display name shown in the poll window.</param>
    /// <param name="pollDuration">How long the poll stays open per phase.</param>
    /// <param name="transferDelay">How long the winner has before forced transfer.</param>
    /// <param name="onSuccess">Called with the winning session when transfer executes.</param>
    /// <param name="onFailed">Called when all candidates are exhausted or poll expires with no takers.</param>
    public void StartPoll(
        EntityUid target,
        string entityName,
        TimeSpan pollDuration,
        TimeSpan transferDelay,
        Action<ICommonSession> onSuccess,
        Action onFailed)
    {
        if (_pollQuery.HasComp(target))
            return;

        var state = AddComp<BodyTakeoverPollStateComponent>(target);
        state.Phase = BodyTakeoverPollPhase.GhostPoll;
        state.Deadline = _timing.CurTime + pollDuration;
        state.PollDuration = pollDuration;
        state.TransferDelay = transferDelay;
        state.OnSuccess = onSuccess;
        state.OnFailed = onFailed;

        SendPollToGhosts(target, state, entityName);
    }

    /// <summary>
    /// Cancels any active poll on the given entity
    /// </summary>
    public void CancelPoll(EntityUid target)
    {
        if (!_pollQuery.TryComp(target, out var state))
            return;

        state.Phase = BodyTakeoverPollPhase.Done;
        RemCompDeferred<BodyTakeoverPollStateComponent>(target);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<BodyTakeoverPollStateComponent>();
        while (query.MoveNext(out var uid, out var state))
        {
            if (state.Phase == BodyTakeoverPollPhase.Done)
                continue;

            if (state.Deadline is not { } deadline || _timing.CurTime < deadline)
                continue;

            state.Deadline = null;

            switch (state.Phase)
            {
                case BodyTakeoverPollPhase.GhostPoll:
                    FinishGhostPoll(uid, state);
                    break;
                case BodyTakeoverPollPhase.LivingPoll:
                    FinishLivingPoll(uid, state);
                    break;
                case BodyTakeoverPollPhase.TransferPending:
                    ExecuteTransfer(uid, state);
                    break;
            }
        }
    }

    private void SendPollToGhosts(EntityUid uid, BodyTakeoverPollStateComponent state, string entityName)
    {
        state.Acceptors.Clear();

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity == null || !_ghostQuery.HasComp(session.AttachedEntity.Value))
                continue;

            OpenPollEui(uid, session, entityName, state, BodyTakeoverPollPhase.GhostPoll);
        }
    }

    private void SendPollToLiving(EntityUid uid, BodyTakeoverPollStateComponent state, string entityName)
    {
        state.Acceptors.Clear();

        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity == null)
                continue;

            if (_ghostQuery.HasComp(session.AttachedEntity.Value))
                continue;

            if (!_mobStateQuery.TryComp(session.AttachedEntity.Value, out var mobState))
                continue;

            if (mobState.CurrentState != MobState.Alive)
                continue;

            OpenPollEui(uid, session, entityName, state, BodyTakeoverPollPhase.LivingPoll);
        }
    }

    private void OpenPollEui(EntityUid uid, ICommonSession session, string entityName, BodyTakeoverPollStateComponent state, BodyTakeoverPollPhase expectedPhase)
    {
        var eui = new BodyTakeoverPollEui(session,
            entityName,
            (s, accepted) =>
            {
                if (!accepted)
                    return;

                if (Deleted(uid))
                    return;

                if (!_pollQuery.TryComp(uid, out var liveState))
                    return;

                if (liveState.Phase != expectedPhase)
                    return;

                if (liveState.Deadline.HasValue && _timing.CurTime > liveState.Deadline.Value)
                    return;

                liveState.Acceptors.Add(s.UserId);

                if (liveState.Acceptors.Count == 1)
                    liveState.Deadline = _timing.CurTime + liveState.RaffleWindow;
            });
        _eui.OpenEui(eui, session);
    }

    private void FinishGhostPoll(EntityUid uid, BodyTakeoverPollStateComponent state)
    {
        if (state.Acceptors.Count > 0)
        {
            TryNextCandidate(uid, state);
            return;
        }

        // No ghosts accepted, escalate to living players
        var meta = MetaData(uid);
        state.Phase = BodyTakeoverPollPhase.LivingPoll;
        state.Deadline = _timing.CurTime + state.PollDuration;
        SendPollToLiving(uid, state, meta.EntityName);
    }

    private void FinishLivingPoll(EntityUid uid, BodyTakeoverPollStateComponent state)
    {
        if (state.Acceptors.Count > 0)
        {
            TryNextCandidate(uid, state);
            return;
        }

        // Nobody accepted
        Fail(uid, state);
    }

    private void TryNextCandidate(EntityUid uid, BodyTakeoverPollStateComponent state)
    {
        var meta = MetaData(uid);

        while (state.Acceptors.Count > 0)
        {
            var candidateId = _random.Pick(state.Acceptors);
            state.Acceptors.Remove(candidateId);

            if (!_players.TryGetSessionById(candidateId, out var session))
                continue;

            var isLivingPhase = state.Phase == BodyTakeoverPollPhase.LivingPoll;

            state.Winner = candidateId;
            state.PhaseBeforeTransfer = state.Phase;
            state.Phase = BodyTakeoverPollPhase.TransferPending;

            if (!isLivingPhase)
            {
                // Ghosts transfer immediately
                state.Deadline = _timing.CurTime;
                return;
            }

            state.Deadline = _timing.CurTime + state.TransferDelay;

            var transferEui = new BodyTakeoverTransferEui(
                session,
                meta.EntityName,
                (s, choice) =>
                {
                    if (Deleted(uid))
                        return;

                    if (!_pollQuery.TryComp(uid, out var liveState))
                        return;

                    if (liveState.Phase != BodyTakeoverPollPhase.TransferPending)
                        return;

                    if (liveState.Winner != s.UserId)
                        return;

                    if (liveState.Deadline.HasValue && _timing.CurTime > liveState.Deadline.Value)
                        return;

                    switch (choice)
                    {
                        case BodyTakeoverTransferMessage.Choice.Now:
                            liveState.Deadline = _timing.CurTime;
                            break;
                        case BodyTakeoverTransferMessage.Choice.Wait:
                            break;
                        case BodyTakeoverTransferMessage.Choice.Decline:
                            liveState.Winner = null;
                            liveState.Phase = BodyTakeoverPollPhase.LivingPoll;
                            liveState.Deadline = _timing.CurTime;
                            break;
                    }
                },
                state.TransferDelay);
            _eui.OpenEui(transferEui, session);
            return;
        }

        Fail(uid, state);
    }

    private void ExecuteTransfer(EntityUid uid, BodyTakeoverPollStateComponent state)
    {
        state.Phase = BodyTakeoverPollPhase.Done;

        if (state.Winner == null)
        {
            Fail(uid, state);
            return;
        }

        if (!_players.TryGetSessionById(state.Winner.Value, out var session))
        {
            state.Winner = null;

            if (state.PhaseBeforeTransfer == BodyTakeoverPollPhase.GhostPoll && state.Acceptors.Count == 0)
            {
                var meta = MetaData(uid);
                state.Phase = BodyTakeoverPollPhase.LivingPoll;
                state.Deadline = _timing.CurTime + state.PollDuration;
                SendPollToLiving(uid, state, meta.EntityName);
                return;
            }

            state.Phase = state.PhaseBeforeTransfer;
            TryNextCandidate(uid, state);
            return;
        }

        var onSuccess = state.OnSuccess;
        RemCompDeferred<BodyTakeoverPollStateComponent>(uid);
        onSuccess?.Invoke(session);
    }

    private void Fail(EntityUid uid, BodyTakeoverPollStateComponent state)
    {
        state.Phase = BodyTakeoverPollPhase.Done;
        var onFailed = state.OnFailed;
        RemCompDeferred<BodyTakeoverPollStateComponent>(uid);
        onFailed?.Invoke();
    }
}

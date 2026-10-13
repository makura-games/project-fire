using Content.Shared._Scp.Holding.Components;
using Content.Shared._Scp.Holding.Systems;
using Content.Shared.Movement.Pulling.Components;

#pragma warning disable IDE0130
namespace Content.Shared.Movement.Pulling.Systems;

public sealed partial class PullingSystem
{
    [Dependency] private SharedScpHoldingSystem _scpHolding = default!;

    private EntityQuery<PullableComponent> _pullableQuery;
    private EntityQuery<ActiveScpHolderComponent> _scpActiveHolderQuery;

    private void InitializeScpHolding()
    {
        _pullableQuery = GetEntityQuery<PullableComponent>();
        _scpActiveHolderQuery = GetEntityQuery<ActiveScpHolderComponent>();
    }

    /// <summary>
    /// Attempts to consume a pull request by redirecting it into SCP holding.
    /// Returns <see langword="true"/> when the pull attempt was handled, even if it was rejected.
    /// The <paramref name="success"/> output indicates whether the redirect actually succeeded.
    /// This includes the paths that validate via <see cref="SharedScpHoldingSystem.CanToggleHold"/>,
    /// stop existing pulls via <c>TryStopPull</c>, and finally toggle the hold via
    /// <see cref="SharedScpHoldingSystem.TryToggleHold(Content.Shared._Scp.Holding.Components.ScpHolderComponent, EntityUid, bool)"/>.
    /// </summary>
    private bool TryRedirectPullToScpHold(EntityUid pullerUid, EntityUid pullableUid,
        PullerComponent pullerComp, PullableComponent pullableComp, out bool success)
    {
        success = false;

        if (!_scpHolding.CanRedirectPullToScpHold(pullerUid, pullableUid))
            return false;

        var holdComp = Comp<ScpHolderComponent>(pullerUid);
        var holder = (pullerUid, holdComp);

        if (_scpActiveHolderQuery.TryComp(pullerUid, out var activeHolder) &&
            activeHolder.Target != null)
        {
            success = _scpHolding.TryToggleHold(holder, pullableUid);
            return true;
        }

        if (!_scpHolding.CanToggleHold(holder,
                pullableUid,
                ignoreHandAvailability: pullerComp.Pulling != null,
                checkAttempt: true))
            return true;

        if (pullerComp.Pulling is { } currentPullUid &&
            _pullableQuery.TryComp(currentPullUid, out var currentPull) &&
            !TryStopPull(currentPullUid, currentPull, pullerUid))
        {
            return true;
        }

        if (pullableComp.Puller != null &&
            !TryStopPull(pullableUid, pullableComp, pullableComp.Puller))
        {
            return true;
        }

        success = _scpHolding.TryToggleHold(holder, pullableUid, attemptChecked: true);
        return true;
    }
}

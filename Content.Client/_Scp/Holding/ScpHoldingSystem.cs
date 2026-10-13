using Content.Client.Hands.Systems;
using Content.Client.Inventory;
using Content.Shared._Scp.Holding.Components;
using Content.Shared._Scp.Holding.Systems;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Input;
using Content.Shared.Inventory.VirtualItem;
using Robust.Client.Physics;
using Robust.Client.Player;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._Scp.Holding;

public sealed partial class ScpHoldingSystem : SharedScpHoldingSystem
{
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private Robust.Client.Physics.PhysicsSystem _physics = default!;
    [Dependency] private VirtualItemSystem _virtualItem = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IGameTiming _timing = default!;

    private EntityUid? _trackedHolderTarget;
    private readonly List<EntityUid> _authoritativeBlockers = [];
    private readonly List<EntityUid> _predictedBlockers = [];

    private EntityQuery<HandsComponent> _handsQuery;
    private EntityQuery<ScpHoldableComponent> _holdableQuery;
    private EntityQuery<ScpHoldHandBlockerComponent> _blockerQuery;
    private EntityQuery<ActiveScpHolderComponent> _activeHolderQuery;
    private EntityQuery<VirtualItemComponent> _virtualItemQuery;

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.MovePulledObject, new PointerInputCmdHandler(OnMoveHeldToCursor))
            .Register<ScpHoldingSystem>();

        _handsQuery = GetEntityQuery<HandsComponent>();
        _holdableQuery = GetEntityQuery<ScpHoldableComponent>();
        _blockerQuery = GetEntityQuery<ScpHoldHandBlockerComponent>();
        _activeHolderQuery = GetEntityQuery<ActiveScpHolderComponent>();
        _virtualItemQuery = GetEntityQuery<VirtualItemComponent>();

        SubscribeLocalEvent<ActiveScpHoldableComponent, AfterAutoHandleStateEvent>(OnHeldAfterState);
        SubscribeLocalEvent<ActiveScpHolderComponent, AfterAutoHandleStateEvent>(OnHolderAfterState);
        SubscribeLocalEvent<ScpHoldHandBlockerComponent, ComponentStartup>(OnBlockerStartup);
        SubscribeLocalEvent<ScpHoldHandBlockerComponent, GotEquippedHandEvent>(OnBlockerEquipped);
        SubscribeLocalEvent<ActiveScpHoldableComponent, UpdateIsPredictedEvent>(OnUpdateHeldPredicted);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<ScpHoldingSystem>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_player.LocalEntity is not { Valid: true } local)
        {
            UpdateTrackedLocalHeldTarget(null);
            return;
        }

        if (!_activeHolderQuery.TryComp(local, out var localHolder))
        {
            UpdateTrackedLocalHeldTarget(null);
            return;
        }

        ReconcileLocalHolderState((local, localHolder));
    }

    private void OnHeldAfterState(Entity<ActiveScpHoldableComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        ReconcileHeldAfterState(ent);
    }

    private void OnHolderAfterState(Entity<ActiveScpHolderComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (_player.LocalEntity != ent)
            return;

        ReconcileLocalHolderState(ent);
    }

    private void OnBlockerStartup(Entity<ScpHoldHandBlockerComponent> ent, ref ComponentStartup args)
    {
        if (!_timing.ApplyingState)
            return;

        ReconcileLocalHolderBlocker(ent);
    }

    private void OnBlockerEquipped(Entity<ScpHoldHandBlockerComponent> ent, ref GotEquippedHandEvent args)
    {
        if (!_timing.ApplyingState)
            return;

        ReconcileLocalHolderBlocker(ent, args.User);
    }

    private void OnUpdateHeldPredicted(Entity<ActiveScpHoldableComponent> ent, ref UpdateIsPredictedEvent args)
    {
        if (_player.LocalEntity is not { Valid: true } local)
            return;

        if (ent.Owner == local)
        {
            args.IsPredicted = true;
            return;
        }

        if (_activeHolderQuery.TryComp(local, out var localHolder))
        {
            if (localHolder.Target == ent)
            {
                args.IsPredicted = true;
                return;
            }
        }

        foreach (var holder in ent.Comp.Holders)
        {
            if (holder != local)
                continue;

            args.IsPredicted = true;
            return;
        }

        if (ent.Comp.Holders.Count > 0)
            args.BlockPrediction = true;
    }

    private bool OnMoveHeldToCursor(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (_player.LocalEntity is not { Valid: true } local)
            return false;

        TryMoveHeldToCursor(local, coords);
        return false;
    }

    private void ReconcileHeldAfterState(Entity<ActiveScpHoldableComponent> held)
    {
        _physics.UpdateIsPredicted(held);

        if (HasComp<ActiveStateScpHoldableFullHoldComponent>(held))
            SyncPlaceholderHands(held);
    }

    protected override void UpdateHeldStates()
    {
        var query = EntityQueryEnumerator<ActiveScpHoldableComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var held, out var physics))
        {
            if (!physics.Predict)
                continue;

            _physics.UpdateIsPredicted(uid);
            UpdateHeld((uid, held));
        }
    }

    protected override void OnHeldStateShutdown(Entity<ActiveScpHoldableComponent> held)
    {
        _physics.UpdateIsPredicted(held);
    }

    private void ReconcileLocalHolderBlocker(EntityUid blocker, EntityUid? holderUid = null)
    {
        holderUid ??= _player.LocalEntity;

        if (holderUid is not { Valid: true } holder)
            return;

        if (!_activeHolderQuery.TryComp(holder, out var activeHolder))
            return;

        if (activeHolder.Target == null)
            return;

        if (!_virtualItemQuery.TryComp(blocker, out var virtualItem))
            return;

        if (virtualItem.BlockingEntity != activeHolder.Target.Value)
            return;

        if (!_handsQuery.TryComp(holder, out var hands))
            return;

        if (!_hands.IsHolding((holder, hands), blocker))
            return;

        ReconcileLocalHolderState((holder, activeHolder));
    }

    private void ReconcileLocalHolderState(Entity<ActiveScpHolderComponent> holder)
    {
        UpdateTrackedLocalHeldTarget(holder, holder.Comp.Target);
        ReconcileLocalHolderBlockerSteadyState(holder);
    }

    private void ReconcileLocalHolderBlockerSteadyState(Entity<ActiveScpHolderComponent> holder)
    {
        if (holder.Comp.Target == null)
            return;

        if (!_handsQuery.TryComp(holder, out var hands))
            return;

        var target = holder.Comp.Target.Value;
        if (!_holdableQuery.TryComp(target, out var holdable))
            return;

        var requiredHolderHandCount = GetRequiredHolderHandCount(holdable);
        _authoritativeBlockers.Clear();
        _predictedBlockers.Clear();

        Entity<HandsComponent?> holderHands = (holder, hands);

        foreach (var heldItem in _hands.EnumerateHeld(holderHands))
        {
            if (!_virtualItemQuery.TryComp(heldItem, out var virtualItem))
                continue;

            if (virtualItem.BlockingEntity != target)
                continue;

            if (!IsClientSide(heldItem))
            {
                _authoritativeBlockers.Add(heldItem);
                continue;
            }

            if (!_blockerQuery.HasComp(heldItem))
                continue;

            _predictedBlockers.Add(heldItem);
        }

        var requiredPredictedBlockerCount = Math.Max(requiredHolderHandCount - _authoritativeBlockers.Count, 0);
        for (var i = requiredPredictedBlockerCount; i < _predictedBlockers.Count; i++)
        {
            QueueDel(_predictedBlockers[i]);
        }

        if (_timing.ApplyingState)
        {
            return;
        }

        var currentPredictedBlockerCount = Math.Min(_predictedBlockers.Count, requiredPredictedBlockerCount);
        while (currentPredictedBlockerCount < requiredPredictedBlockerCount)
        {
            if (!_hands.TryGetEmptyHand(holderHands, out var emptyHand))
                break;

            if (!_virtualItem.TrySpawnVirtualItem(target, holder, out var spawnedVirtualItem))
                break;

            EnsureComp<ScpHoldHandBlockerComponent>(spawnedVirtualItem.Value);
            _hands.DoPickup(holder, emptyHand, spawnedVirtualItem.Value, hands);
            currentPredictedBlockerCount++;
        }
    }

    private void UpdateTrackedLocalHeldTarget(EntityUid? currentTarget, EntityUid? previousTarget = null)
    {
        if (_trackedHolderTarget == currentTarget)
            return;

        previousTarget ??= _trackedHolderTarget;

        if (previousTarget != null)
            _physics.UpdateIsPredicted(previousTarget.Value);

        _trackedHolderTarget = currentTarget;

        if (_trackedHolderTarget != null)
            _physics.UpdateIsPredicted(_trackedHolderTarget.Value);
    }

    private void UpdateTrackedLocalHeldTarget(EntityUid holderUid, EntityUid? currentTarget, EntityUid? previousTarget = null)
    {
        if (_player.LocalEntity != holderUid)
            return;

        UpdateTrackedLocalHeldTarget(currentTarget, previousTarget);
    }
}

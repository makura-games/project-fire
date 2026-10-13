using Content.Shared._Scp.Holding.Components;
using Content.Shared._Scp.Helpers;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Components;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Throwing;

namespace Content.Shared._Scp.Holding.Systems;

public abstract partial class SharedScpHoldingSystem
{
    /*
     * Hand-local dependencies, caches, placeholders, virtual blockers, and held-status visuals.
     */

    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedVirtualItemSystem _virtualItem = default!;

    private readonly List<EntityUid> _placeholderIcons = [];
    private readonly HashSet<EntityUid> _holdersSuppressingVirtualItemSync = [];
    private EntityQuery<HandsComponent> _handsQuery;
    private EntityQuery<VirtualItemComponent> _virtualItemQuery;
    private EntityQuery<ScpHeldHandBlockerComponent> _heldHandBlockerQuery;
    private EntityQuery<ScpHoldHandBlockerComponent> _holdHandBlockerQuery;
    private EntityQuery<UnremoveableComponent> _unremoveableQuery;

    private void InitializeHandQueries()
    {
        _handsQuery = GetEntityQuery<HandsComponent>();
        _virtualItemQuery = GetEntityQuery<VirtualItemComponent>();
        _heldHandBlockerQuery = GetEntityQuery<ScpHeldHandBlockerComponent>();
        _holdHandBlockerQuery = GetEntityQuery<ScpHoldHandBlockerComponent>();
        _unremoveableQuery = GetEntityQuery<UnremoveableComponent>();
    }

    private void InitializeHandEvents()
    {
        SubscribeLocalEvent<ActiveScpHolderComponent, BeforeThrowEvent>(OnHolderBeforeThrow);
        SubscribeLocalEvent<ActiveScpHolderComponent, DidEquipHandEvent>(OnHolderHandsModified);
        SubscribeLocalEvent<ActiveScpHolderComponent, VirtualItemDeletedEvent>(OnHolderVirtualItemDeleted);
        SubscribeLocalEvent<ScpHoldHandBlockerComponent, GettingDroppedAttemptEvent>(OnHolderBlockerGettingDropped);
    }

    protected void SyncPlaceholderHands(Entity<ActiveScpHoldableComponent> held)
    {
        if (!_handsQuery.TryComp(held, out var hands))
            return;

        if (!_activeHoldableFullHoldStateQuery.HasComp(held))
        {
            DeleteHeldHandBlockers(held);
            return;
        }

        CollectPlaceholderIconHolders(held);

        if (_placeholderIcons.Count == 0)
        {
            DeleteHeldHandBlockers(held);
            return;
        }

        var heldHands = (held, hands);
        DropHeldItemsForPlaceholders(heldHands);
        DeleteInvalidHeldHandBlockers(heldHands);
        EnsureHeldHandBlockers(heldHands);
    }

    private void CollectPlaceholderIconHolders(Entity<ActiveScpHoldableComponent> held)
    {
        _placeholderIcons.Clear();

        foreach (var holderUid in held.Comp.Holders)
        {
            if (!_activeHolderQuery.TryComp(holderUid, out var holder))
                continue;

            if (holder.Target != held)
                continue;

            _placeholderIcons.Add(holderUid);
        }
    }

    private void DropHeldItemsForPlaceholders(Entity<HandsComponent?> held)
    {
        foreach (var hand in _hands.EnumerateHands(held))
        {
            if (!_hands.TryGetHeldItem(held, hand, out var heldItem))
                continue;

            if (_unremoveableQuery.HasComp(heldItem.Value))
                continue;

            _hands.DoDrop(held, hand, doDropInteraction: true);
        }
    }

    private void DeleteInvalidHeldHandBlockers(Entity<HandsComponent?> held)
    {
        using var virtualBlockersToDelete = ListPoolEntity<VirtualItemComponent>.Rent();

        foreach (var heldItem in _hands.EnumerateHeld(held))
        {
            if (!_heldHandBlockerQuery.HasComp(heldItem))
                continue;

            if (!_virtualItemQuery.TryComp(heldItem, out var virtualItem))
                continue;

            if (!IsValidHeldHandBlocker(virtualItem))
                virtualBlockersToDelete.Value.Add((heldItem, virtualItem));
        }

        foreach (var virtualItem in virtualBlockersToDelete.Value)
        {
            _virtualItem.DeleteVirtualItem(virtualItem, held);
        }
    }

    private bool IsValidHeldHandBlocker(VirtualItemComponent virtualItem)
    {
        return _placeholderIcons.Contains(virtualItem.BlockingEntity);
    }

    private void EnsureHeldHandBlockers(Entity<HandsComponent?> held)
    {
        var iconIndex = 0;
        while (_hands.TryGetEmptyHand(held, out var emptyHand))
        {
            var holderUid = _placeholderIcons[iconIndex % _placeholderIcons.Count];
            if (!TryPickupHeldHandBlockerVirtualItem(holderUid, held, emptyHand))
                break;

            iconIndex++;
        }
    }

    private void SyncHolderHandBlocker(Entity<ActiveScpHolderComponent> holder)
    {
        using var virtualBlockersToDelete = ListPoolEntity<VirtualItemComponent>.Rent();
        var target = holder.Comp.Target;
        var validBlockerCount = 0;
        var requiredHolderHandCount = 0;

        if (target != null
            && _activeHoldableQuery.HasComp(target)
            && TryGetRequiredHolderHandCount(target.Value, out var resolvedRequiredHolderHandCount))
        {
            requiredHolderHandCount = resolvedRequiredHolderHandCount;
        }

        foreach (var heldItem in _hands.EnumerateHeld(holder.Owner))
        {
            if (!_virtualItemQuery.TryComp(heldItem, out var virtualItem))
                continue;

            var ownedBlocker = _holdHandBlockerQuery.HasComp(heldItem);
            var matchesCurrentTarget = target != null
                                       && virtualItem.BlockingEntity == target.Value;

            if (ownedBlocker && matchesCurrentTarget)
            {
                if (validBlockerCount < requiredHolderHandCount)
                {
                    validBlockerCount++;
                    RemComp<UnremoveableComponent>(heldItem);
                    continue;
                }
            }

            if (ownedBlocker)
                virtualBlockersToDelete.Value.Add((heldItem, virtualItem));
        }

        foreach (var virtualItem in virtualBlockersToDelete.Value)
        {
            RemoveHolderHandBlocker(holder, virtualItem);
        }

        if (target == null)
            return;

        if (!_handsQuery.TryComp(holder, out var hands))
        {
            ReleaseHolderContribution(holder, target.Value, clearIfEmpty: true);
            return;
        }

        var holderHands = (holder, hands);

        while (validBlockerCount < requiredHolderHandCount)
        {
            if (!_hands.TryGetEmptyHand(holderHands, out var emptyHand))
                break;

            if (!TryPickupHolderHandBlockerVirtualItem(target.Value, holderHands, emptyHand))
                break;
            validBlockerCount++;
        }

        validBlockerCount = CountOwnedHolderHandBlockers(holder, target.Value);
        if (validBlockerCount < requiredHolderHandCount)
            ReleaseHolderContribution(holder, target.Value, clearIfEmpty: true);
    }

    private bool HasAvailableHolderHands(EntityUid holderUid, int requiredHandCount)
    {
        return _handsQuery.TryComp(holderUid, out var hands)
               && _hands.CountFreeHands((holderUid, hands)) >= requiredHandCount;
    }

    private int CountOwnedHolderHandBlockers(EntityUid holderUid, EntityUid targetUid)
    {
        var blockerCount = 0;
        foreach (var heldItem in _hands.EnumerateHeld(holderUid))
        {
            if (!_holdHandBlockerQuery.HasComp(heldItem))
                continue;

            if (!_virtualItemQuery.TryComp(heldItem, out var virtualItem))
                continue;

            if (virtualItem.BlockingEntity != targetUid)
                continue;

            blockerCount++;
        }

        return blockerCount;
    }

    private bool TryPickupHeldHandBlockerVirtualItem(
        EntityUid blockingEntity,
        Entity<HandsComponent?> user,
        string handId)
    {
        if (!_virtualItem.TrySpawnVirtualItem(blockingEntity, user, out var virtualItemUid))
            return false;

        EnsureComp<UnremoveableComponent>(virtualItemUid.Value);
        EnsureComp<ScpHeldHandBlockerComponent>(virtualItemUid.Value);
        _hands.DoPickup(user, handId, virtualItemUid.Value, user.Comp);

        if (_hands.TryGetHeldItem(user, handId, out var heldItem) && heldItem == virtualItemUid.Value)
            return true;

        DeleteFailedHandBlockerVirtualItem(virtualItemUid.Value);
        return false;
    }

    private bool TryPickupHolderHandBlockerVirtualItem(
        EntityUid blockingEntity,
        Entity<HandsComponent?> user,
        string handId)
    {
        if (!_virtualItem.TrySpawnVirtualItem(blockingEntity, user, out var virtualItemUid))
            return false;

        EnsureComp<ScpHoldHandBlockerComponent>(virtualItemUid.Value);
        _hands.DoPickup(user, handId, virtualItemUid.Value, user.Comp);

        if (_hands.TryGetHeldItem(user, handId, out var heldItem) && heldItem == virtualItemUid.Value)
            return true;

        DeleteFailedHandBlockerVirtualItem(virtualItemUid.Value);
        return false;
    }

    private void DeleteFailedHandBlockerVirtualItem(EntityUid virtualItemUid)
    {
        QueueDel(virtualItemUid);
    }

    private void RemoveHolderHandBlocker(EntityUid holderUid, Entity<VirtualItemComponent> virtualItem)
    {
        var addedSuppression = _holdersSuppressingVirtualItemSync.Add(holderUid);

        if (_handsQuery.TryComp(holderUid, out var hands)
            && _hands.IsHolding((holderUid, hands), virtualItem, out var hand))
        {
            _hands.DoDrop((holderUid, hands), hand, doDropInteraction: false, log: false);
        }
        else
        {
            _virtualItem.DeleteVirtualItem(virtualItem, holderUid);
        }

        if (addedSuppression)
            _holdersSuppressingVirtualItemSync.Remove(holderUid);
    }

    private void DeleteHolderHandBlockers(EntityUid holderUid)
    {
        using var virtualBlockersToDelete = ListPoolEntity<VirtualItemComponent>.Rent();

        foreach (var heldItem in _hands.EnumerateHeld(holderUid))
        {
            if (!_holdHandBlockerQuery.HasComp(heldItem))
                continue;

            if (!_virtualItemQuery.TryComp(heldItem, out var virtualItem))
                continue;

            virtualBlockersToDelete.Value.Add((heldItem, virtualItem));
        }

        foreach (var virtualItem in virtualBlockersToDelete.Value)
        {
            RemoveHolderHandBlocker(holderUid, virtualItem);
        }
    }

    private void DeleteHeldHandBlockers(EntityUid heldUid)
    {
        using var virtualBlockersToDelete = ListPoolEntity<VirtualItemComponent>.Rent();

        foreach (var heldItem in _hands.EnumerateHeld(heldUid))
        {
            if (_heldHandBlockerQuery.HasComp(heldItem) &&
                _virtualItemQuery.TryComp(heldItem, out var virtualItem))
            {
                virtualBlockersToDelete.Value.Add((heldItem, virtualItem));
            }
        }

        foreach (var virtualItem in virtualBlockersToDelete.Value)
        {
            _virtualItem.DeleteVirtualItem(virtualItem, heldUid);
        }
    }

    private void OnHolderBeforeThrow(Entity<ActiveScpHolderComponent> ent, ref BeforeThrowEvent args)
    {
        if (ent.Comp.Target == null)
            return;

        if (!HasComp<ScpHoldHandBlockerComponent>(args.ItemUid))
            return;

        if (!_virtualItemQuery.TryComp(args.ItemUid, out var virtualItem))
            return;

        if (virtualItem.BlockingEntity != ent.Comp.Target.Value)
            return;

        ReleaseHolderContribution(ent, ent.Comp.Target.Value, clearIfEmpty: true);
        args.Cancelled = true;
    }

    private void OnHolderHandsModified(Entity<ActiveScpHolderComponent> ent, ref DidEquipHandEvent args)
    {
        if (ent.Comp.LifeStage > ComponentLifeStage.Running)
            return;

        if (ent.Comp.Target == null)
            return;

        if (!_activeHoldableQuery.HasComp(ent.Comp.Target.Value))
            return;

        SyncHolderState(ent);
    }

    private void OnHolderBlockerGettingDropped(Entity<ScpHoldHandBlockerComponent> ent, ref GettingDroppedAttemptEvent args)
    {
        if (!_virtualItemQuery.TryComp(ent, out var virtualItem))
            return;

        if (!TryComp<ScpHolderComponent>(args.User, out var holder))
            return;

        if (!TryReleaseHold((args.User, holder), virtualItem.BlockingEntity))
            return;

        args.Cancelled = true;
    }

    private void OnHolderVirtualItemDeleted(Entity<ActiveScpHolderComponent> ent, ref VirtualItemDeletedEvent args)
    {
        if (_timing.ApplyingState)
            return;

        if (_holdersSuppressingVirtualItemSync.Contains(ent))
            return;

        if (TerminatingOrDeleted(ent))
            return;

        if (ent.Comp.Target == null || ent.Comp.Target != args.BlockingEntity)
            return;

        if (!TryGetRequiredHolderHandCount(args.BlockingEntity, out var requiredHolderHandCount))
            return;

        if (CountOwnedHolderHandBlockers(ent, args.BlockingEntity) >= requiredHolderHandCount)
            return;

        SyncHolderState(ent);
    }
}

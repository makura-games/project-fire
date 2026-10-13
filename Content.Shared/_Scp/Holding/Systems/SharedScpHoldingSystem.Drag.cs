using System.Numerics;
using Content.Shared._Scp.Holding.Components;
using Content.Shared.Interaction;
using Content.Shared.Movement.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.Shared._Scp.Holding.Systems;

public abstract partial class SharedScpHoldingSystem
{
    /*
     * Drag-local dependencies, aggregate holder movement, and helper calculations.
     */

    [Dependency] private SharedTransformSystem _transform = default!;

    private EntityQuery<PhysicsComponent> _physicsQuery;

    private void InitializeDragQueries()
    {
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
    }

    private void InitializeDragEvents()
    {
        SubscribeLocalEvent<ActiveScpHoldableComponent, AttemptMobCollideEvent>(OnHeldAttemptMobCollide);
        SubscribeLocalEvent<ActiveScpHoldableComponent, AttemptMobTargetCollideEvent>(OnHeldAttemptMobTargetCollide);
        SubscribeLocalEvent<ActiveScpHoldableComponent, PreventCollideEvent>(OnHeldPreventCollide);
        SubscribeLocalEvent<ActiveScpHolderComponent, PreventCollideEvent>(OnHolderPreventCollide);
    }

    private void UpdateSoftDrag(
        Entity<ActiveScpHoldableComponent> held,
        ScpHoldableComponent holdable,
        float maintenanceRange,
        float desiredDistance)
    {
        if (!_physicsQuery.TryComp(held, out var heldPhysics))
            return;

        var aggregateDesiredVelocity = Vector2.Zero;
        var contributingHolderCount = 0;

        foreach (var holderUid in held.Comp.Holders)
        {
            if (!_activeHolderQuery.TryComp(holderUid, out var holder))
                continue;

            if (holder.Target != held)
                continue;

            Vector2 desiredVelocity;
            if (!TryGetHolderCursorDesiredVelocity((holderUid, holder), held, holdable, maintenanceRange, heldPhysics, out desiredVelocity) &&
                !TryGetHolderMovementDesiredVelocity((holderUid, holder), held, holdable, maintenanceRange, desiredDistance, heldPhysics, out desiredVelocity))
            {
                continue;
            }

            aggregateDesiredVelocity += desiredVelocity;
            contributingHolderCount++;
        }

        if (contributingHolderCount == 0)
        {
            ZeroHeldVelocity(held);
            return;
        }

        ApplyHeldVelocity(held, aggregateDesiredVelocity / contributingHolderCount, heldPhysics, holdable);
    }

    private bool TryGetHolderMovementDesiredVelocity(
        Entity<ActiveScpHolderComponent> holder,
        Entity<ActiveScpHoldableComponent> held,
        ScpHoldableComponent holdable,
        float maintenanceRange,
        float desiredDistance,
        PhysicsComponent heldPhysics,
        out Vector2 desiredVelocity)
    {
        desiredVelocity = Vector2.Zero;

        if (!_container.IsInSameOrNoContainer(holder.Owner, held.Owner))
            return false;

        if (!_interaction.InRangeUnobstructed(holder.Owner, held.Owner, maintenanceRange))
            return false;

        var holderCoords = _transform.GetMapCoordinates(holder.Owner);
        var heldCoords = _transform.GetMapCoordinates(held.Owner);

        if (holderCoords.MapId != heldCoords.MapId)
            return false;

        var offset = heldCoords.Position - holderCoords.Position;
        var distance = offset.Length();
        var holderVelocity = _physicsQuery.TryComp(holder, out var holderPhysics)
            ? holderPhysics.LinearVelocity
            : Vector2.Zero;
        var velocityDirectionThresholdSquared = holdable.SoftDragVelocityDirectionThreshold * holdable.SoftDragVelocityDirectionThreshold;
        var direction = GetSoftDragDirection(holder.Owner, holdable, holderVelocity, offset, distance, velocityDirectionThresholdSquared);
        var desiredPosition = holderCoords.Position + direction * desiredDistance;
        var correction = desiredPosition - heldCoords.Position;
        var correctionDistance = correction.Length();

        if (correctionDistance <= holdable.SoftDragSettleTolerance)
        {
            desiredVelocity = holderVelocity.LengthSquared() > velocityDirectionThresholdSquared
                ? holderVelocity
                : Vector2.Zero;
            return true;
        }

        var correctionDirection = correction / correctionDistance;
        var correctionSpeed = Math.Min(correctionDistance / GetSoftDragCatchUpTime(holdable), holdable.SoftDragMaximumCorrectionSpeed);
        desiredVelocity = holderVelocity + correctionDirection * correctionSpeed;

        var relativeVelocity = heldPhysics.LinearVelocity - holderVelocity;
        var awaySpeed = MathF.Max(0f, -Vector2.Dot(relativeVelocity, correctionDirection));
        if (awaySpeed > 0f)
            desiredVelocity += correctionDirection * awaySpeed * holdable.SoftDragAwayVelocityStrength;

        return true;
    }

    private static float GetDesiredSoftDragDistance(ScpHoldableComponent holdable)
    {
        return GetBaseSoftDragDistance(holdable);
    }

    private static float GetHoldMaintenanceRange(ScpHoldableComponent holdable, float desiredSoftDragDistance)
    {
        return MathF.Max(MathF.Max(holdable.HoldRange, SharedInteractionSystem.InteractionRange), desiredSoftDragDistance + holdable.SoftDragSnapTolerance);
    }

    private static float GetBaseSoftDragDistance(ScpHoldableComponent holdable)
    {
        return Math.Clamp(holdable.HoldRange * holdable.SoftDragDistanceFactor, holdable.SoftDragMinimumDistance, holdable.SoftDragMaximumDistance);
    }

    private float GetSoftDragCatchUpTime(ScpHoldableComponent holdable)
    {
        return MathF.Max((float)_timing.TickPeriod.TotalSeconds, holdable.SoftDragCatchUpTime);
    }

    private Vector2 GetSoftDragDirection(EntityUid holderUid, ScpHoldableComponent holdable, Vector2 holderVelocity, Vector2 offset, float distance, float velocityDirectionThresholdSquared)
    {
        if (distance > holdable.SoftDragSnapTolerance)
            return offset / distance;

        if (holderVelocity.LengthSquared() > velocityDirectionThresholdSquared)
            return -Vector2.Normalize(holderVelocity);

        return Transform(holderUid).LocalRotation.ToWorldVec();
    }

    private void ApplyHeldVelocity(EntityUid uid, Vector2 desiredVelocity, PhysicsComponent physics, ScpHoldableComponent holdable)
    {
        if (Vector2.DistanceSquared(physics.LinearVelocity, desiredVelocity) > holdable.SoftDragVelocityTolerance * holdable.SoftDragVelocityTolerance)
            _physics.SetLinearVelocity(uid, desiredVelocity, body: physics);

        if (!MathHelper.CloseTo(physics.AngularVelocity, 0f))
            _physics.SetAngularVelocity(uid, 0f, body: physics);
    }

    private void ZeroHeldVelocity(EntityUid uid)
    {
        if (!_physicsQuery.TryComp(uid, out var physics))
            return;

        if (physics.LinearVelocity == Vector2.Zero && MathHelper.CloseTo(physics.AngularVelocity, 0f))
            return;

        _physics.SetLinearVelocity(uid, Vector2.Zero, body: physics);
        _physics.SetAngularVelocity(uid, 0f, body: physics);
    }

    private static void OnHeldAttemptMobCollide(Entity<ActiveScpHoldableComponent> ent, ref AttemptMobCollideEvent args)
    {
        args.Cancelled = true;
    }

    private static void OnHeldAttemptMobTargetCollide(Entity<ActiveScpHoldableComponent> ent, ref AttemptMobTargetCollideEvent args)
    {
        args.Cancelled = true;
    }

    private void OnHeldPreventCollide(Entity<ActiveScpHoldableComponent> ent, ref PreventCollideEvent args)
    {
        if (args.Cancelled)
            return;

        if (_activeHolderQuery.TryComp(args.OtherEntity, out var holder) &&
            holder.Target == ent)
        {
            args.Cancelled = true;
        }
    }

    private void OnHolderPreventCollide(Entity<ActiveScpHolderComponent> ent, ref PreventCollideEvent args)
    {
        if (args.Cancelled)
            return;

        if (ent.Comp.Target == null)
            return;

        if (ent.Comp.Target != args.OtherEntity)
            return;

        args.Cancelled = true;
    }
}

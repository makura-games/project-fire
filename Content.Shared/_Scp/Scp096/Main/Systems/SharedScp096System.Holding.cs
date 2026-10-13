using System.Diagnostics;
using System.Numerics;
using Content.Shared._Scp.Holding;
using Content.Shared._Scp.Holding.Components;
using Content.Shared._Scp.Holding.Systems;
using Content.Shared._Scp.Scp096.Main.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._Scp.Scp096.Main.Systems;

public abstract partial class SharedScp096System
{
    [Dependency] private SharedScpHoldingSystem _holding = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private void InitializeHolding()
    {
        SubscribeLocalEvent<Scp096Component, ScpHoldAttemptEvent>(OnHoldAttempt);
        SubscribeLocalEvent<Scp096Component, ScpHoldBreakoutEvent>(OnHoldBreakout);
    }

    private void OnHoldAttempt(Entity<Scp096Component> ent, ref ScpHoldAttemptEvent args)
    {
        if (IsInHoldRestrictedState(ent.Owner))
            args.Cancelled = true;
    }

    private void OnHoldBreakout(Entity<Scp096Component> ent, ref ScpHoldBreakoutEvent args)
    {
        if (_timing.ApplyingState)
            return;

        if (!args.WasFullHold && !IsInHoldRestrictedState(ent.Owner))
            return;

        if (!TryComp<ActiveScpHoldableComponent>(ent.Owner, out var held))
            return;

        var scpPosition = _transform.GetWorldPosition(ent.Owner);
        var holderCount = held.Holders.Count;
        if (holderCount == 0)
            return;

        var holders = new (EntityUid HolderUid, Vector2 Position)[holderCount];
        for (var i = 0; i < holderCount; i++)
        {
            var holderUid = held.Holders[i];
            holders[i] = (holderUid, _transform.GetWorldPosition(holderUid));
        }

        for (var i = 0; i < holderCount; i++)
        {
            var holder = holders[i];
            ApplyHoldBreakoutEffects(ent, holder.HolderUid, holder.Position, scpPosition, i, holderCount);
        }
    }

    protected bool IsInHoldRestrictedState(EntityUid uid)
    {
        return HasComp<ActiveScp096HeatingUpComponent>(uid)
            || HasComp<ActiveScp096RageComponent>(uid)
            || HasComp<ActiveScp096WithoutFaceComponent>(uid);
    }

    protected void TryBreakOutOfHold(EntityUid uid)
    {
        _holding.TryForceBreakOut((uid, (ActiveScpHoldableComponent?) null));
    }

    private void ApplyHoldBreakoutEffects(
        Entity<Scp096Component> ent,
        EntityUid holderUid,
        Vector2 holderPosition,
        Vector2 scpPosition,
        int holderIndex,
        int holderCount)
    {
        _damageable.TryChangeDamage(holderUid, ent.Comp.HoldBreakoutDamage, origin: ent.Owner);
        _stun.TryUpdateParalyzeDuration(holderUid, ent.Comp.HoldBreakoutParalyzeTime);

        if (!TryComp<PhysicsComponent>(holderUid, out var physics))
            return;

        var direction = GetHoldBreakoutDirection(holderPosition, scpPosition, holderIndex, holderCount);
        _physics.ApplyLinearImpulse(holderUid, direction * physics.Mass * ent.Comp.HoldBreakoutImpulse, body: physics);
    }

    private static Vector2 GetHoldBreakoutDirection(Vector2 holderPosition, Vector2 scpPosition, int holderIndex, int holderCount)
    {
        if (holderCount <= 0)
            return Vector2.Zero;

        var direction = holderPosition - scpPosition;
        if (direction.LengthSquared() >= 0.001f)
            return Vector2.Normalize(direction);

        var angle = 2f * MathF.PI * holderIndex / holderCount;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }
}

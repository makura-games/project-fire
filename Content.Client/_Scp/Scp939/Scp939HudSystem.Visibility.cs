using Content.Shared._Scp.Scp939;
using Content.Shared.Standing;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Physics.Events;
using Robust.Shared.Random;

namespace Content.Client._Scp.Scp939;

public sealed partial class Scp939HudSystem
{
    [Dependency] private IRobustRandom _random = default!;

    private void InitializeVisibility()
    {
        SubscribeLocalEvent((Entity<ActiveScp939VisibilityComponent> ent, ref StartCollideEvent args)
            => OnCollide(ent, args.OtherEntity));
        SubscribeLocalEvent((Entity<ActiveScp939VisibilityComponent> ent, ref EndCollideEvent args)
            => OnCollide(ent, args.OtherEntity));
        SubscribeLocalEvent<ActiveScp939VisibilityComponent, AfterAutoHandleStateEvent>(OnVisibilityStateUpdated);

        SubscribeLocalEvent<ActiveScp939VisibilityComponent, MoveEvent>(OnMove);

        SubscribeLocalEvent<ActiveScp939VisibilityComponent, ThrowEvent>(OnThrow);
        SubscribeLocalEvent<ActiveScp939VisibilityComponent, StoodEvent>(OnStood);
        SubscribeLocalEvent<ActiveScp939VisibilityComponent, MeleeAttackEvent>(OnMeleeAttack);
        SubscribeLocalEvent<ActiveScp939VisibilityComponent, DownedEvent>(OnDown);
    }

    private void OnVisibilityStateUpdated(Entity<ActiveScp939VisibilityComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (ent.Comp.LastHandledVisibilityResetCounter == ent.Comp.VisibilityResetCounter)
            return;

        ent.Comp.LastHandledVisibilityResetCounter = ent.Comp.VisibilityResetCounter;
        ent.Comp.VisibilityAcc = Scp939VisibilityComponent.InitialVisibilityAcc;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!IsActive)
            return;

        _lastUpdateTime += frameTime;
        if (_lastUpdateTime < UpdateInterval)
            return;

        var delta = _lastUpdateTime;
        _lastUpdateTime = 0f;

        var query = EntityQueryEnumerator<ActiveScp939VisibilityComponent>();
        while (query.MoveNext(out _, out var visibilityComponent))
        {
            if (visibilityComponent.VisibilityAcc >= visibilityComponent.HideTime)
                continue;

            visibilityComponent.VisibilityAcc = MathF.Min(visibilityComponent.VisibilityAcc + delta, visibilityComponent.HideTime);
        }
    }

    private void OnMove(Entity<ActiveScp939VisibilityComponent> ent, ref MoveEvent args)
    {
        if (!IsActive)
            return;

        // В зависимости от наличие защит или проблем со зрением у 939 изменяется то, насколько хорошо мы видим жертву
        if (ModifyAcc(ent.Comp, out var modifier)) // Если зрение затруднено
        {
            ent.Comp.VisibilityAcc *= modifier;
        }
        else if (_scp939ProtectionQuery.HasComp(ent)) // Если имеется защита(тихое хождение)
        {
            return;
        }
        else // Если со зрением все ок
        {
            ent.Comp.VisibilityAcc = 0;
        }

        if (!_movementSpeedQuery.TryComp(ent, out var speedModifierComponent)
            || !_physicsQuery.TryComp(ent, out var physicsComponent))
        {
            return;
        }

        var currentVelocity = physicsComponent.LinearVelocity.Length();

        if (speedModifierComponent.BaseWalkSpeed > currentVelocity)
            ent.Comp.VisibilityAcc = ent.Comp.HideTime / 2f;
    }


    private void OnCollide(Entity<ActiveScp939VisibilityComponent> ent, EntityUid otherEntity)
    {
        if (!IsActive)
            return;

        if (!HasComp<Scp939Component>(otherEntity))
            return;

        MobDidSomething(ent);
    }

    private void OnThrow(Entity<ActiveScp939VisibilityComponent> ent, ref ThrowEvent args)
    {
        if (!IsActive)
            return;

        MobDidSomething(ent);
    }

    private void OnStood(Entity<ActiveScp939VisibilityComponent> ent, ref StoodEvent args)
    {
        if (!IsActive)
            return;

        MobDidSomething(ent);
    }

    private void OnMeleeAttack(Entity<ActiveScp939VisibilityComponent> ent, ref MeleeAttackEvent args)
    {
        if (!IsActive)
            return;

        MobDidSomething(ent);
    }

    private void OnDown(Entity<ActiveScp939VisibilityComponent> ent, ref DownedEvent args)
    {
        if (!IsActive)
            return;

        MobDidSomething(ent);
    }

    private void MobDidSomething(Entity<ActiveScp939VisibilityComponent> ent)
    {
        ent.Comp.VisibilityAcc = Scp939VisibilityComponent.InitialVisibilityAcc;
    }

    // TODO: Переделать под статус эффект и добавить его в панель статус эффектов, а то непонятно игруну
    /// <summary>
    /// Если вдруг собачка плохо видит
    /// </summary>
    private bool ModifyAcc(ActiveScp939VisibilityComponent visibilityComponent, out int modifier)
    {
        // 1 = отсутствие модификатора
        modifier = 1;

        if (_scp939Component == null)
            return false;

        if (!_scp939Component.PoorEyesight)
            return false;

        modifier = _random.Next(visibilityComponent.MinValue, visibilityComponent.MaxValue);

        return true;
    }
}

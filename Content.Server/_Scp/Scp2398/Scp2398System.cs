using System.Threading;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Events;
using Robust.Server.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server._Scp.Scp2398;

public sealed partial class Scp2398System : EntitySystem
{
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mob = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<Scp2398Component, MeleeHitEvent>(OnMeleeHitEvent);
        SubscribeLocalEvent<Scp2398Component, ThrowDoHitEvent>(OnThrowHitEvent);
    }

    private void OnMeleeHitEvent(EntityUid uid, Scp2398Component component, MeleeHitEvent args)
    {
        foreach (var hitEntity in args.HitEntities)
        {
            TryExplode(uid, hitEntity);
        }
    }

    private void OnThrowHitEvent(EntityUid uid, Scp2398Component component, ThrowDoHitEvent args)
    {
        // По какой-то странной причине, оно не выдает что я ожидаю от него
        // if (_physics.GetMapLinearVelocity(uid).Length() <= component.TriggerThrowSpeed)
        //     return;
        TryExplode(uid, args.Target);
    }

    private void TryExplode(EntityUid uid, EntityUid target)
    {
        // В оригинальной статье не указано, работает ли бита на мертвых. Так что она у нас не работает.
        if (!HasComp<MobStateComponent>(target) || _mob.IsDead(target) || _mob.IsCritical(target))
            return;

        Explode(target);
    }

    private void Explode(EntityUid target)
    {
        if (!TryComp<PhysicsComponent>(target, out var physics))
            return;

        var coords = _transform.GetMapCoordinates(target);
        Timer.Spawn(_timing.TickPeriod,
            () => _explosion.QueueExplosion(coords,
                ExplosionSystem.DefaultExplosionPrototypeId,
            physics.Mass * 2,
            10,
                10000,
            target,
            maxTileBreak: 0),
            CancellationToken.None);

        // _mob.ChangeMobState(target, MobState.Dead);  // Оно должно гарантированно убивать цель

        // Если надо будет - можно и гибать :)
        // _body.GibBody(target);
    }
}

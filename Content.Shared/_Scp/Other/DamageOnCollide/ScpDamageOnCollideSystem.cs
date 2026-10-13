using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.Shared._Scp.Other.DamageOnCollide;

public sealed partial class ScpDamageOnCollideSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<MobStateComponent> _mobStateQuery;
    private EntityQuery<ScpDamageOnCollideComponent> _damageCollideQuery;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpDamageOnCollideComponent, StartCollideEvent>(OnCollide);

        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _mobStateQuery = GetEntityQuery<MobStateComponent>();
        _damageCollideQuery = GetEntityQuery<ScpDamageOnCollideComponent>();
    }

    private void OnCollide(Entity<ScpDamageOnCollideComponent> ent, ref StartCollideEvent args)
    {
        TryApplyDamage(ent!, args.OtherEntity);
    }

    /// <summary>
    /// Пытается нанести урон цели, используя параметры компонента <see cref="ScpDamageOnCollideComponent"/>.
    /// Перебирает все наборы параметров и применяет первый подходящий (по вайтлисту, блеклисту,
    /// состоянию моба и проверке скорости). При успехе наносит урон и проигрывает звуки.
    /// </summary>
    /// <param name="ent">Сущность с компонентом ScpDamageOnCollide (источник урона)</param>
    /// <param name="target">Цель, которой наносится урон</param>
    /// <param name="requireVelocity">
    /// Если true — проверяет наличие скорости для параметров с RequiresVelocity.
    /// Передавайте false при вызове из способностей (например, прыжок SCP-173), где скорость не нужна.
    /// </param>
    /// <returns>true, если урон был нанесён; false, если ни один набор параметров не подошёл</returns>
    public bool TryApplyDamage(Entity<ScpDamageOnCollideComponent?> ent, EntityUid target, bool requireVelocity = true)
    {
        if (!_damageCollideQuery.Resolve(ent, ref ent.Comp))
            return false;

        foreach (var param in ent.Comp.Params)
        {
            if (!CheckParameter(ent, target, param, requireVelocity))
                continue;

            _damageable.TryChangeDamage(target, param.Damage, ignoreVariance: !param.UseVariance, ignoreResistances: param.IgnoreResistances);

            _audio.PlayPredicted(param.TargetSound, target, ent);
            _audio.PlayPredicted(param.EntitySound, ent, ent);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Проверяет, подходит ли сущность под параметры
    /// </summary>
    /// <param name="collider">Сущность, которая совершает столкновение</param>
    /// <param name="target">Сущность для проверки</param>
    /// <param name="param">Параметры, указанные в компоненте</param>
    /// <param name="requireVelocity">Нужна ли проверка скорости</param>
    /// <returns>Подходит или нет</returns>
    private bool CheckParameter(EntityUid collider, EntityUid target, DamageOnCollideParameters param, bool requireVelocity = true)
    {
        if (!_whitelist.CheckBoth(target, param.Blacklist, param.Whitelist))
            return false;

        if (requireVelocity && param.RequiresVelocity && !CheckVelocity(collider))
            return false;

        if (param.RequiredMobStates != null && !CheckMobState(target, param.RequiredMobStates))
            return false;

        return true;
    }

    private bool CheckVelocity(EntityUid collider)
    {
        if (!_physicsQuery.TryComp(collider, out var physics))
            return false;

        if (physics.LinearVelocity.IsLengthZero())
            return false;

        return true;
    }

    private bool CheckMobState(EntityUid target, List<MobState> states)
    {
        if (!_mobStateQuery.TryComp(target, out var mobState))
            return false;

        if (!states.Contains(mobState.CurrentState))
            return false;

        return true;
    }
}

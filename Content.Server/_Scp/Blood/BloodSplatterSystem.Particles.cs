using System.Diagnostics;
using System.Numerics;
using Content.Shared._Scp.Animations.Offset;
using Content.Shared._Scp.Blood;
using Robust.Server.GameObjects;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Scp.Blood;

// TODO: Рефактор и создание LiquidParticle с целью унификации в сторону партиклов с любыми жидкостями,
// любыми источниками жидкости, настройками и прочим.
// + API для вызова из других систем.
public sealed partial class BloodSplatterSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PhysicsSystem _physics = default!;

    private EntityQuery<BloodParticleComponent> _particleQuery;

    private void InitializeParticles()
    {
        SubscribeLocalEvent<BloodParticleComponent, MapInitEvent>(OnInit);

        _particleQuery = GetEntityQuery<BloodParticleComponent>();
    }

    private void OnInit(Entity<BloodParticleComponent> ent, ref MapInitEvent args)
    {
        Debug.Assert(ent.Comp.MoveTimes != 0);

        ent.Comp.FlyTime += ent.Comp.FlyTime * _random.NextFloat(0f, ent.Comp.FlyTimeVariation);
        ent.Comp.FlyTimeEnd = _timing.CurTime + ent.Comp.FlyTime;
        Dirty(ent);

        if (TryComp<OffsetAnimationComponent>(ent, out var offsetAnimation))
        {
            offsetAnimation.AnimationTime = ent.Comp.FlyTime;
            Dirty(ent.Owner, offsetAnimation);
        }
    }

    private void UpdateParticles()
    {
        var query = EntityQueryEnumerator<BloodParticleComponent, FixturesComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var particle, out var fixtures, out var physics))
        {
            UpdateParticlesMoving(uid, particle, fixtures, physics);
            UpdateParticlesLanding(uid, particle);
        }
    }

    /// <summary>
    /// Двигаем частичку в нужном направлении и с нужной скорость, используя метод интервального движения.
    /// </summary>
    private void UpdateParticlesMoving(EntityUid uid,
        BloodParticleComponent particle,
        FixturesComponent fixtures,
        PhysicsComponent physics)
    {
        if (!particle.Velocity.HasValue)
            return;

        if (_timing.CurTime < particle.NextMoveTime)
            return;

        _physics.ApplyLinearImpulse(uid, particle.Velocity.Value, fixtures, physics);
        particle.NextMoveTime = _timing.CurTime + particle.MoveCooldown;
    }

    /// <summary>
    /// Проверяем, достигла ли частичка конца своего полета.
    /// Если да - спавним маленькую лужицу крови и удаляем частичку.
    /// </summary>
    private void UpdateParticlesLanding(EntityUid uid, BloodParticleComponent particle)
    {
        if (!particle.FlyTimeEnd.HasValue)
            return;

        if (_timing.CurTime < particle.FlyTimeEnd)
            return;

        SpawnBloodEntity((uid, particle));
    }

    public void SpawnBloodParticles(Entity<BloodSplattererComponent> ent, EntityUid target, Angle baseAngle, float spreadRadians, Vector2? distanceOverride = null)
    {
        var count = _random.Next(ent.Comp.Amount.X, ent.Comp.Amount.Y);
        if (count <= 0)
            return;

        _audio.PlayPvs(ent.Comp.ParticleSpawnedSound, target);
        var coords = _transform.GetMoverCoordinates(target);

        for (var i = 0; i < count; i++)
        {
            var proto = _random.Pick(ent.Comp.Particles);
            var particle = Spawn(proto, coords);

            if (!_particleQuery.TryComp(particle, out var particleComponent))
            {
                Log.Error($"Found blood PARTICLE without {nameof(BloodParticleComponent)}, prototype: {proto}");
                continue;
            }

            // Если кровь закончилась
            if (!TryTakeBlood(target, ent.Comp.BloodToTakePerParticle, particle))
            {
                QueueDel(particle);
                return;
            }

            CalculateMove((particle, particleComponent), distanceOverride ?? ent.Comp.Distance, baseAngle, spreadRadians);
        }
    }

    /// <summary>
    /// Спавнит лужицу крови на месте, куда упала капля по истечению времени полета.
    /// </summary>
    private void SpawnBloodEntity(Entity<BloodParticleComponent> ent)
    {
        var proto = _random.Pick(ent.Comp.BloodEntities);
        var uid = Spawn(proto, Transform(ent).Coordinates);

        if (!_solution.TryGetSolution(uid, SolutionName, out var solutionEntity, out _))
        {
            Log.Error($"Found blood PUDDLE without any solution, prototype: {proto}");

            QueueDel(uid);
            QueueDel(ent);
            return;
        }

        if (!_solution.TryGetSolution(ent.Owner, SolutionName, out _, out var particleSolution))
        {
            Log.Error($"Found blood PARTICLE without any solution, prototype: {proto}");

            QueueDel(uid);
            QueueDel(ent);
            return;
        }

        _audio.PlayPvs(ent.Comp.LandSound, uid);

        _solution.TryAddSolution(solutionEntity.Value, particleSolution);
        QueueDel(ent);
    }

    /// <summary>
    /// Рассчитывает параметры для перемещения частички крови на основе времени полета и количества промежутков перемещения.
    /// </summary>
    private void CalculateMove(Entity<BloodParticleComponent> ent, Vector2 distance, Angle baseAngle, float spreadRadians)
    {
        // Задаем количество раз, когда частику крови будет толкать физика.
        // Это количество зависит от времени полета и указанного числа промежутков.
        ent.Comp.MoveCooldown = ent.Comp.FlyTime / ent.Comp.MoveTimes;

        // Рассчитываем нужную скорость движения исходя из расстояния, которое нужно пройти,
        // И количества промежутков, которые у нас будут.
        ent.Comp.Speed = distance / ent.Comp.MoveTimes;

        // Вычисляем вектор движения частички
        var randomOffset = _random.NextFloat(-spreadRadians / 2f, spreadRadians / 2f);
        var angle = baseAngle + randomOffset;

        if (ent.Comp.Speed == Vector2.Zero)
        {
            Log.Error($"Found blood PARTICLE with zero speed {ToPrettyString(ent)}");
            QueueDel(ent);
            return;
        }

        var speed = _random.NextFloat(ent.Comp.Speed.X, ent.Comp.Speed.Y);
        var direction = new Vector2((float) Math.Cos(angle.Theta), (float) Math.Sin(angle.Theta));

        ent.Comp.Velocity = direction * speed;
    }
}

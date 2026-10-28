using System.Numerics;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared.Projectiles;
using Content.Shared._Scp.ZLevels;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;

namespace Content.Server._Scp.ZLevels;

/// <summary>
/// Transfers fired projectiles after source-floor contacts at the ledge are processed.
/// </summary>
public sealed partial class ZLevelProjectileSystem : EntitySystem
{
    [Dependency] private ZLevelShootingSystem _zLevelShooting = default!;
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private EntityQuery<ZLevelProjectileComponent> _routeQuery = default!;

    private const float Epsilon = 0.001f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PhysicsUpdateAfterSolveEvent>(OnAfterSolve);
    }

    private void OnAfterSolve(ref PhysicsUpdateAfterSolveEvent args)
    {
        if (args.Prediction)
            return;

        var query = EntityQueryEnumerator<ZLevelProjectileComponent, ProjectileComponent, PhysicsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var route, out var projectile, out var body, out var xform))
        {
            if (projectile.ProjectileSpent || TerminatingOrDeleted(uid))
            {
                RemCompDeferred<ZLevelProjectileComponent>(uid);
                continue;
            }

            var plan = route.Plan;
            if (xform.MapUid != (route.Transitioned ? plan.TargetMap : plan.SourceMap) ||
                TerminatingOrDeleted(plan.TargetMap))
            {
                Expire((uid, projectile), body);
                continue;
            }

            var position = _transform.GetWorldPosition(xform);
            var velocity = _physics.GetMapLinearVelocity(uid, body);

            if (route.AtLimit)
            {
                _transform.SetWorldPosition(uid, route.PreviousPosition);
                Expire((uid, projectile), body);
                continue;
            }

            // Рикошет до края отменяет выбранный переход, сохраняя обычный полёт по исходному этажу.
            if (!route.Transitioned && velocity.LengthSquared() > Epsilon * Epsilon &&
                Vector2.Dot(Vector2.Normalize(velocity), route.Direction) < 0.99f)
            {
                RemCompDeferred<ZLevelProjectileComponent>(uid);
                continue;
            }

            if (route.AtEdge)
            {
                // Контакты последнего участка исходного этажа уже обработаны этим шагом физики.
                _transform.SetWorldPosition(uid, plan.Transition);
                if (!_zLevels.TryMove(uid, plan.Offset) || xform.MapUid != plan.TargetMap)
                {
                    Expire((uid, projectile), body);
                    continue;
                }
                _transform.AttachToGridOrMap(uid, xform);
                var newVelocity = _physics.GetMapLinearVelocity(uid, body);
                _physics.SetLinearVelocity(uid, body.LinearVelocity + velocity - newVelocity, body: body);
                route.Transitioned = true;
                route.AtEdge = false;
                route.PreviousPosition = plan.Transition;
                route.DistanceTravelled = plan.TransitionDistance;

                if (float.IsPositiveInfinity(plan.MaxDistance))
                    RemCompDeferred<ZLevelProjectileComponent>(uid);
                continue;
            }

            if (!route.Transitioned &&
                Vector2.Dot(position - route.Origin, route.Direction) >= plan.TransitionDistance)
            {
                // Оставляем снаряд у края до следующей проверки контактов, не пропуская цель перед проёмом.
                _transform.SetWorldPosition(uid, plan.Transition);
                route.PreviousPosition = plan.Transition;
                route.DistanceTravelled = plan.TransitionDistance;
                route.AtEdge = true;
                continue;
            }

            var travelled = Vector2.Distance(route.PreviousPosition, position);
            if (route.DistanceTravelled + travelled >= plan.MaxDistance)
            {
                var remaining = MathF.Max(0f, plan.MaxDistance - Epsilon - route.DistanceTravelled);
                var end = travelled > Epsilon
                    ? route.PreviousPosition + (position - route.PreviousPosition) * (remaining / travelled)
                    : position;
                _transform.SetWorldPosition(uid, end);
                // Проверка контактов идёт перед движением: последнему допустимому участку нужен ещё один проход.
                route.PreviousPosition = end;
                route.DistanceTravelled = plan.MaxDistance - Epsilon;
                route.AtLimit = true;
                continue;
            }

            route.DistanceTravelled += travelled;
            route.PreviousPosition = position;
        }
    }

    /// <summary>
    /// Configures a route using the actual velocity of an individual projectile or pellet.
    /// </summary>
    public bool TryConfigure(EntityUid projectile, EntityUid user, MapCoordinates target)
    {
        var from = _transform.GetMapCoordinates(projectile);
        var velocity = _physics.GetMapLinearVelocity(projectile);
        if (from.MapId == target.MapId || velocity.LengthSquared() <= Epsilon * Epsilon)
            return false;

        var direction = Vector2.Normalize(velocity);
        var aimDistance = Vector2.Distance(from.Position, target.Position);
        var actualTarget = new MapCoordinates(from.Position + direction * aimDistance, target.MapId);
        if (!_zLevelShooting.TryGetShotPlan(user, from, actualTarget, out var plan))
            return false;

        var route = EnsureComp<ZLevelProjectileComponent>(projectile);
        route.Plan = plan;
        route.Origin = from.Position;
        route.Direction = direction;
        route.PreviousPosition = from.Position;
        route.DistanceTravelled = 0f;
        route.Transitioned = false;
        route.AtEdge = false;
        route.AtLimit = false;
        return true;
    }

    /// <summary>
    /// Expires a projectile before a delayed collision can damage targets beyond its range.
    /// </summary>
    public bool TryExpireBeyondRange(Entity<ProjectileComponent> projectile, PhysicsComponent body)
    {
        if (!CanExpireBeyondRange(projectile))
            return false;

        Expire(projectile, body);
        return true;
    }

    private bool CanExpireBeyondRange(EntityUid projectile)
    {
        if (!_routeQuery.TryComp(projectile, out var route) || !route.Transitioned)
            return false;

        var position = _transform.GetWorldPosition(projectile);
        return route.DistanceTravelled + Vector2.Distance(route.PreviousPosition, position) > route.Plan.MaxDistance;
    }

    private void Expire(Entity<ProjectileComponent> projectile, PhysicsComponent body)
    {
        projectile.Comp.ProjectileSpent = true;
        Dirty(projectile);
        _physics.SetCanCollide(projectile, false, body: body);
        if (projectile.Comp.ImpactEffect is { } effect)
        {
            var coordinates = Transform(projectile).Coordinates;
            RaiseNetworkEvent(new ImpactEffectEvent(effect, GetNetCoordinates(coordinates)),
                Filter.Pvs(coordinates, entityMan: EntityManager));
        }
        QueueDel(projectile);
    }
}

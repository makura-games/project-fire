using Content.Shared._Scp.ZLevels;
using Content.Shared.Damage.Components;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Robust.Shared.Map;
using Robust.Shared.Physics;

namespace Content.Shared.Weapons.Hitscan.Systems;

public sealed partial class HitscanBasicRaycastSystem
{
    // Разделение межэтажного луча и сохранение дальности при отражениях.
    [Dependency] private EntityQuery<ZLevelHitscanComponent> _zLevelHitscanQuery = default!;

    private bool TryTraceZLevelHitscan(
        Entity<HitscanBasicRaycastComponent> ent,
        HitscanTraceEvent args,
        EntityUid ignored,
        out ZLevelHitscanTrace trace)
    {
        trace = default;
        if (!_zLevelHitscanQuery.TryComp(ent, out var zHitscan))
            return false;

        var remaining = zHitscan.RemainingDistance ?? zHitscan.Plan.MaxDistance;
        var range = MathF.Max(0f, MathF.Min(ent.Comp.MaxDistance, remaining));
        var origin = _transform.ToMapCoordinates(args.FromCoordinates);
        var inContainer = _container.IsEntityOrParentInContainer(args.Shooter ?? args.Gun);

        if (!zHitscan.TransitionHandled)
        {
            // Отражение меняет направление луча и не должно повторно использовать исходный переход.
            zHitscan.TransitionHandled = true;
            var sourceRange = MathF.Min(range, zHitscan.Plan.TransitionDistance);
            var sourceHit = TraceZLevelSegment(ent, args, ignored, inContainer, origin, sourceRange);
            if (sourceHit != null || range <= sourceRange)
            {
                var distance = sourceHit?.Distance ?? sourceRange;
                // Луч, перехваченный до края, остаётся на исходном этаже с обычной дальностью отражения.
                zHitscan.RemainingDistance = sourceHit != null ? float.PositiveInfinity : MathF.Max(0f, remaining - distance);
                trace = new ZLevelHitscanTrace(sourceHit, args.FromCoordinates, distance, true);
                return true;
            }

            FireEffects(args.FromCoordinates, sourceRange, args.ShotDirection.ToAngle(), ent,
                showImpact: false, wholeSegment: true);

            var targetOrigin = new MapCoordinates(zHitscan.Plan.Transition, zHitscan.Plan.TargetMapId);
            var targetHit = TraceZLevelSegment(ent, args, ignored, inContainer, targetOrigin, range - sourceRange);
            var targetDistance = targetHit?.Distance ?? range - sourceRange;
            zHitscan.RemainingDistance = MathF.Max(0f, remaining - sourceRange - targetDistance);
            trace = new ZLevelHitscanTrace(
                targetHit,
                _transform.ToCoordinates(targetOrigin),
                targetDistance,
                false);
            return true;
        }

        var hit = TraceZLevelSegment(ent, args, ignored, inContainer, origin, range);
        var traveled = hit?.Distance ?? range;
        zHitscan.RemainingDistance = MathF.Max(0f, remaining - traveled);
        trace = new ZLevelHitscanTrace(hit, args.FromCoordinates, traveled, true);
        return true;
    }

    private RayCastResults? TraceZLevelSegment(
        Entity<HitscanBasicRaycastComponent> ent,
        HitscanTraceEvent args,
        EntityUid ignored,
        bool inContainer,
        MapCoordinates origin,
        float range)
    {
        if (range <= 0f)
            return null;

        var ray = new CollisionRay(origin.Position, args.ShotDirection, (int) ent.Comp.CollisionMask);
        var hits = _physics.IntersectRay(origin.MapId, ray, range, ignored, false);
        foreach (var hit in hits)
        {
            if (inContainer || hit.HitEntity == args.Target ||
                CompOrNull<RequireProjectileTargetComponent>(hit.HitEntity)?.Active != true)
            {
                return hit;
            }
        }

        return null;
    }

    private readonly record struct ZLevelHitscanTrace(
        RayCastResults? Result,
        EntityCoordinates EffectFrom,
        float Distance,
        bool ShowMuzzle);
}

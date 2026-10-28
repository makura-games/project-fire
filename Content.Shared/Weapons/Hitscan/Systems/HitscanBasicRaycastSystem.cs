using System.Numerics;
using Content.Shared.Administration.Logs;
using Content.Shared.Damage.Components;
using Content.Shared.Database;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Shared.Weapons.Hitscan.Systems;

public sealed partial class HitscanBasicRaycastSystem : EntitySystem
{
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private ISharedAdminLogManager _log = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    [Dependency] private EntityQuery<HitscanBasicVisualsComponent> _visualsQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HitscanBasicRaycastComponent, HitscanTraceEvent>(OnHitscanFired);
    }

    private void OnHitscanFired(Entity<HitscanBasicRaycastComponent> ent, ref HitscanTraceEvent args)
    {
        var shooter = args.Shooter ?? args.Gun;
        // Sunrise edit start - ignore shooter's mech for hitscan raycast
        var ignored = shooter;
        if (TryComp<Content.Shared.Mech.Components.MechPilotComponent>(ignored, out var pilot))
        {
            ignored = pilot.Mech;
        }
        // Sunrise edit end
        // Fire edit start - проверяем препятствия на обоих этажах, чтобы луч не проходил сквозь стены до проёма.
        RayCastResults? result;
        float distanceTried;
        var effectFrom = args.FromCoordinates;
        var showMuzzle = true;
        var zLevelTrace = TryTraceZLevelHitscan(ent, args, ignored, out var trace);
        if (zLevelTrace)
        {
            result = trace.Result;
            distanceTried = trace.Distance;
            effectFrom = trace.EffectFrom;
            showMuzzle = trace.ShowMuzzle;
        }
        else
        {
            var mapCords = _transform.ToMapCoordinates(args.FromCoordinates);
            var ray = new CollisionRay(mapCords.Position, args.ShotDirection, (int) ent.Comp.CollisionMask);
            var rayCastResults = _physics.IntersectRay(mapCords.MapId, ray, ent.Comp.MaxDistance, ignored, false);

            var target = args.Target;
            // Находясь в контейнере, используем первое пересечение; иначе учитываем требование прицеливания.
            result = _container.IsEntityOrParentInContainer(shooter)
                ? rayCastResults.FirstOrNull()
                : rayCastResults.FirstOrNull(hit => hit.HitEntity == target
                                                    || CompOrNull<RequireProjectileTargetComponent>(hit.HitEntity)?.Active != true);

            distanceTried = result?.Distance ?? ent.Comp.MaxDistance;
        }
        // Fire edit end

        // Do visuals without an event. They should always happen and putting it on the attempt event is weird!
        // If more stuff gets added here, it should probably be turned into an event.
        FireEffects(effectFrom, distanceTried, args.ShotDirection.ToAngle(), ent.Owner,
            showMuzzle: showMuzzle, wholeSegment: zLevelTrace); // Fire edit - рисуем межэтажный участок целиком, чтобы луч не обрывался у проёма.

        // Admin logging
        if (result?.HitEntity != null)
        {
            _log.Add(LogType.HitScanHit,
                $"{ToPrettyString(shooter):user} hit {ToPrettyString(result.Value.HitEntity):target}"
                + $" using {ToPrettyString(args.Gun):entity}.");
        }

        var data = new HitscanRaycastFiredData
        {
            ShotDirection = args.ShotDirection,
            Gun = args.Gun,
            Shooter = args.Shooter,
            HitEntity = result?.HitEntity,
        };

        var attemptEvent = new AttemptHitscanRaycastFiredEvent { Data = data };
        RaiseLocalEvent(ent, ref attemptEvent);

        if (attemptEvent.Cancelled)
            return;

        var hitEvent = new HitscanRaycastFiredEvent { Data = data };
        RaiseLocalEvent(ent, ref hitEvent);
    }

    /// <summary>
    /// Create visual effects for the fired hitscan weapon.
    /// </summary>
    /// <param name="fromCoordinates">Location to start the effect.</param>
    /// <param name="distance">Distance of the hitscan shot.</param>
    /// <param name="shotAngle">Angle of the shot.</param>
    /// <param name="hitscanUid">The hitscan entity itself.</param>
    /// <param name="showMuzzle">Whether to show a muzzle flash at the start of the ray.</param>
    /// <param name="showImpact">Whether to show an effect at the end of the ray.</param>
    /// <param name="wholeSegment">Draw the complete segment, including short sections at a floor's edge.</param>
    // Fire edit start - позволяем отключать вспышки на проёме и рисовать межэтажные участки целиком.
    private void FireEffects(EntityCoordinates fromCoordinates, float distance, Angle shotAngle, EntityUid hitscanUid,
        bool showMuzzle = true, bool showImpact = true, bool wholeSegment = false)
    // Fire edit end
    {
        if (distance == 0 || !_visualsQuery.TryComp(hitscanUid, out var vizComp))
            return;

        // Sunrise edit start - update sprites tuple type to include Color? and Vector2?
        var sprites = new List<(NetCoordinates coordinates, Angle angle, SpriteSpecifier sprite, float scale, Color? Color, Vector2? Scale)>();
        // Sunrise edit end
        var fromXform = Transform(fromCoordinates.EntityId);

        // We'll get the effects relative to the grid / map of the firer
        // Look you could probably optimise this a bit with redundant transforms at this point.

        var gridUid = fromXform.GridUid;
        if (gridUid != fromCoordinates.EntityId && TryComp(gridUid, out TransformComponent? gridXform))
        {
            var (_, gridRot, gridInvMatrix) = _transform.GetWorldPositionRotationInvMatrix(gridXform);
            var map = _transform.ToMapCoordinates(fromCoordinates);
            fromCoordinates = new EntityCoordinates(gridUid.Value, Vector2.Transform(map.Position, gridInvMatrix));
            shotAngle -= gridRot;
        }
        else
        {
            shotAngle -= _transform.GetWorldRotation(fromXform);
        }

        // Sunrise edit start - custom bullet visuals with color and scale
        var travelSprite = vizComp.TravelFlash;
        Color? color = null;
        Vector2? scale = null;

        if (vizComp.Bullet != null)
        {
            travelSprite = vizComp.Bullet.Sprite;
            color = vizComp.Bullet.SpriteColor;
            scale = vizComp.Bullet.SpriteScale;
        }
        // Sunrise edit end

        if (distance >= 1f || wholeSegment) // Fire edit - рисуем и участки короче метра, чтобы луч не исчезал у проёма.
        {
            if (showMuzzle && vizComp.MuzzleFlash != null) // Fire edit - на другом этаже продолжается тот же луч, поэтому вспышка выстрела у проёма не нужна.
            {
                var coords = fromCoordinates.Offset(shotAngle.ToVec().Normalized() / 2);
                var netCoords = GetNetCoordinates(coords);

                // Sunrise edit start - pass custom color and scale
                sprites.Add((netCoords, shotAngle, vizComp.MuzzleFlash, 1f, color, scale));
                // Sunrise edit end
            }

            if (travelSprite != null)
            {
                var coords = fromCoordinates.Offset(shotAngle.ToVec() * (wholeSegment ? distance : distance + 0.5f) / 2); // Fire edit - не добавляем отступ для вспышки, чтобы участки луча сходились у проёма.
                var netCoords = GetNetCoordinates(coords);

                // Sunrise edit start - pass custom color and scale
                sprites.Add((netCoords, shotAngle, travelSprite, wholeSegment ? distance : distance - 1.5f, color, scale)); // Fire edit - не укорачиваем участок ради вспышек, чтобы у проёма не было разрыва.
                // Sunrise edit end
            }
        }

        if (showImpact && vizComp.ImpactFlash != null) // Fire edit - не создаём эффект попадания при переходе между этажами, поскольку там не было столкновения.
        {
            var coords = fromCoordinates.Offset(shotAngle.ToVec() * distance);
            var netCoords = GetNetCoordinates(coords);

            // Sunrise edit start - pass custom color and scale
            sprites.Add((netCoords, shotAngle.FlipPositive(), vizComp.ImpactFlash, 1f, color, scale));
            // Sunrise edit end
        }

        if (sprites.Count > 0)
        {
            RaiseNetworkEvent(new SharedGunSystem.HitscanEvent
            {
                Sprites = sprites,
            }, Filter.Pvs(fromCoordinates, entityMan: EntityManager));
        }
    }
}

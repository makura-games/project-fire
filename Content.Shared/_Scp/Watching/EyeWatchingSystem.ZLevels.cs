using System.Numerics;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._Scp.Proximity;
using Content.Shared._Scp.Watching.FOV;
using Content.Shared.Examine;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.Interaction;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Watching;

public sealed partial class EyeWatchingSystem
{
    // Отдельная проверка межэтажного зрения не расширяет обычные события наблюдения за SCP.
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private ITileDefinitionManager _tile = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private EntityQuery<TransformComponent> _xformQuery = default!;
    [Dependency] private EntityQuery<CEZLevelViewerComponent> _zViewerQuery = default!;
    [Dependency] private EntityQuery<FieldOfViewComponent> _fovQuery = default!;

    private const float ZPointTraceLength = 0.001f;

    // Те же физические преграды, которыми ProximitySystem отличает стекло от свободной линии зрения.
    private static readonly HashSet<ProtoId<TagPrototype>> ZBarrierTags =
    [
        "Wall", "Window", "Airlock", "GlassAirlock", "HighSecDoor", "Windoor", "Directional",
        "SecureWindoor", "SecurePlasmaWindoor", "SecureUraniumWindoor",
    ];

    private List<Entity<MapGridComponent>> _zSightGrids = [];

    /// <summary>
    /// Checks sight to a concrete target on a different map in the viewer's Z network.
    /// Targets on every floor obey FOV. The upper adjacent floor requires LookUp; lower floors use
    /// the target's projection through every intervening empty or transparent floor.
    /// Distance is measured in shared world XY.
    /// </summary>
    public bool CanSeeAcrossZ(EntityUid viewer, EntityUid target, LineOfSightBlockerLevel type,
        out float distance)
    {
        distance = 0f;
        if (!_xformQuery.TryComp(viewer, out var viewerXform) ||
            !_xformQuery.TryComp(target, out var targetXform) ||
            viewerXform.MapUid is not { } viewerMap || targetXform.MapUid is not { } targetMap ||
            viewerMap == targetMap ||
            !_zLevels.TryGetZLevelOffset(viewerMap, targetMap, out var offset) || offset == 0 ||
            offset > 1 || offset < -CESharedZLevelsSystem.MaxZLevelsBelowRendering)
        {
            return false;
        }

        if (!_zLevels.TryMapOffset(viewerMap, offset, out var level) || level.Owner != targetMap)
            return false;

        if (!_mobState.IsAlive(viewer) || !CanBeWatched(viewer, target) ||
            _insideStorageQuery.HasComp(target))
        {
            return false;
        }

        var from = _transform.GetWorldPosition(viewerXform);
        var to = _transform.GetWorldPosition(targetXform);
        distance = Vector2.Distance(from, to);
        if (!float.IsFinite(distance) || distance > GetVisibilityRange(viewer))
            return false;

        if (_blinking.IsBlind(viewer))
            return false;

        var attempt = new CanSeeAttemptEvent();
        RaiseLocalEvent(viewer, attempt);
        if (attempt.Blind)
            return false;

        if (_fovQuery.HasComp(viewer) && !_fov.IsInFov(viewer, target, null))
            return false;

        if (offset > 0)
        {
            if (!_zViewerQuery.TryComp(viewer, out var zViewer) || !zViewer.LookUp ||
                _zLevels.GetVisibleZLevelsAbove(viewer, viewerMap) == 0)
            {
                return false;
            }

            // Верхний этаж включается только через проем над наблюдателем, как в viewport.
            if (!HasVisibleZProjection(targetXform.MapID, from, type))
                return false;
        }
        else
        {
            // Чужая дыра рядом не раскрывает цель под непрозрачным полом.
            // Проверяем конкретную проекцию через каждое перекрытие, а не наличие сущности в PVS.
            for (var step = 0; step > offset; step--)
            {
                if (!_zLevels.TryMapOffset(viewerMap, step, out _, out var map) ||
                    !HasVisibleZProjection(map.MapId, to, type))
                {
                    return false;
                }
            }
        }

        // Камерный ZLevelOffset не меняет общие XY игровой геометрии.
        // Обычные LOS API принимают точки только одной карты, поэтому трассируем каждый этаж отдельно.
        var direction = Math.Sign(offset);
        for (var step = 0; ; step += direction)
        {
            if (!_zLevels.TryMapOffset(viewerMap, step, out _, out var map) ||
                !HasZLevelLineOfSight(map.MapId, from, to, viewer, target, type))
            {
                return false;
            }

            if (step == offset)
                return true;
        }
    }

    private bool HasVisibleZProjection(MapId mapId, Vector2 position, LineOfSightBlockerLevel type)
    {
        _zSightGrids.Clear();
        _mapManager.FindGridsIntersecting(mapId,
            Box2.CenteredAround(position, new Vector2(ZPointTraceLength)), ref _zSightGrids, approx: true, includeMap: true);

        foreach (var grid in _zSightGrids)
        {
            if (!_map.TryGetTileRef(grid.Owner, grid.Comp, position, out var tile) || tile.Tile.IsEmpty)
                continue;

            if (!CEZLevelOpeningCache.IsOpeningTile(tile.Tile, _tile) || type < LineOfSightBlockerLevel.Transparent)
                return false;
        }

        return true;
    }

    private bool HasZLevelLineOfSight(MapId mapId, Vector2 from, Vector2 to,
        EntityUid viewer, EntityUid target, LineOfSightBlockerLevel type)
    {
        // При совпадении XY короткий луч все равно проверяет стену в точке проекции.
        if (Vector2.DistanceSquared(from, to) < ZPointTraceLength * ZPointTraceLength)
            to = from + new Vector2(ZPointTraceLength, 0f);

        var origin = new MapCoordinates(from, mapId);
        var destination = new MapCoordinates(to, mapId);
        if (!_examine.InRangeUnOccluded(origin, destination, 0f, (viewer, target),
                static (uid, ignored) => uid == ignored.viewer || uid == ignored.target,
                ignoreInsideBlocker: false))
        {
            return false;
        }

        return type >= LineOfSightBlockerLevel.Transparent ||
               _interaction.InRangeUnobstructed(origin, destination, 0f,
                   predicate: uid => uid == viewer || uid == target || !_tag.HasAnyTag(uid, ZBarrierTags));
    }
}

using System.Numerics;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared._Scp.ZLevels;

/// <summary>
/// Validates aiming geometry between adjacent floors without changing world state.
/// </summary>
public sealed partial class ZLevelShootingSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;
    [Dependency] private EntityQuery<TransformComponent> _transformQuery = default!;
    [Dependency] private EntityQuery<CEZLevelViewerComponent> _viewerQuery = default!;

    private const int MaxTraceTiles = 1024;
    private const float BoundaryEpsilon = 0.001f;

    private List<Entity<MapGridComponent>> _grids = [];
    private List<Entity<MapGridComponent>> _edgeGrids = [];
    private readonly List<float> _edgeCuts = [];

    /// <summary>
    /// Finds a shot's route to the requested adjacent floor.
    /// Weapon systems handle entity collisions before and after the transition.
    /// </summary>
    public bool TryGetShotPlan(EntityUid user, MapCoordinates from, MapCoordinates to, out ZLevelShotPlan plan)
    {
        plan = default;
        if (!IsFinite(from.Position) || !IsFinite(to.Position) || from.MapId == to.MapId ||
            from.MapId == MapId.Nullspace || to.MapId == MapId.Nullspace ||
            !_map.TryGetMap(from.MapId, out var sourceMap) || !_map.TryGetMap(to.MapId, out var targetMap) ||
            !_transformQuery.TryComp(user, out var userTransform) || userTransform.MapUid != sourceMap ||
            !_zLevels.TryGetMapNetwork(sourceMap.Value, out _) ||
            !_zLevels.TryGetZLevelOffset(sourceMap.Value, targetMap.Value, out var offset) ||
            offset is not (-1 or 1) ||
            !_zLevels.TryMapOffset(sourceMap.Value, offset, out var adjacentMap) || adjacentMap.Owner != targetMap)
        {
            return false;
        }

        if (offset > 0 && (!_viewerQuery.TryComp(user, out var viewer) || !viewer.LookUp))
            return false;

        var delta = to.Position - from.Position;
        var length = delta.Length();
        if (!float.IsFinite(length) || length <= BoundaryEpsilon)
            return false;

        var openingMapId = offset > 0 ? to.MapId : from.MapId;
        var sourceEmpty = IsEmptyAt(new MapCoordinates(from.Position, openingMapId));
        var targetEmpty = IsEmptyAt(new MapCoordinates(to.Position, openingMapId));
        if (sourceEmpty != (offset > 0) || targetEmpty != (offset < 0))
            return false;

        if (!TryFindBoundary(openingMapId, from.Position, to.Position, offset < 0,
                out var boundaryDistance, out var tileSize, out var distanceFromEdge))
            return false;

        if (offset < 0 && distanceFromEdge > tileSize + BoundaryEpsilon)
            return false;

        // Переносим выстрел со стороны проёма, чтобы округление не помещало его внутрь перекрытия.
        var transitionDistance = MathF.Max(0f, boundaryDistance + (offset < 0 ? BoundaryEpsilon : -BoundaryEpsilon));
        plan = new ZLevelShotPlan(
            sourceMap.Value,
            targetMap.Value,
            to.MapId,
            offset,
            from.Position + delta / length * transitionDistance,
            transitionDistance,
            offset > 0 ? boundaryDistance + 2f * tileSize : float.PositiveInfinity);
        return true;
    }

    /// <summary>
    /// Checks for missing floor tiles. Existing transparent tiles are not physical openings.
    /// </summary>
    public bool IsEmptyAt(MapCoordinates coordinates)
    {
        if (coordinates.MapId == MapId.Nullspace || !IsFinite(coordinates.Position) ||
            !_map.TryGetMap(coordinates.MapId, out _))
        {
            return false;
        }

        return !_mapManager.TryFindGridAt(coordinates, out var gridUid, out var grid) ||
               !_map.TryGetTileRef(gridUid, grid, coordinates.Position, out var tile) || tile.Tile.IsEmpty;
    }

    private bool TryFindBoundary(
        MapId mapId,
        Vector2 from,
        Vector2 to,
        bool enteringOpening,
        out float distance,
        out float tileSize,
        out float distanceFromEdge)
    {
        distance = float.PositiveInfinity;
        tileSize = default;
        distanceFromEdge = float.PositiveInfinity;
        var bounds = new Box2(Vector2.Min(from, to) - new Vector2(BoundaryEpsilon),
            Vector2.Max(from, to) + new Vector2(BoundaryEpsilon));
        _grids.Clear();
        _mapManager.FindGridsIntersecting(mapId, bounds, ref _grids, approx: true, includeMap: true);
        foreach (var grid in _grids)
        {
            if (!TryFindGridBoundary(grid, mapId, from, to, enteringOpening, out var gridDistance, out var gridEdgeDistance) ||
                gridDistance >= distance)
                continue;

            distance = gridDistance;
            tileSize = grid.Comp.TileSize;
            distanceFromEdge = gridEdgeDistance;
        }

        _grids.Clear();
        return float.IsFinite(distance);
    }

    private bool TryFindGridBoundary(
        Entity<MapGridComponent> grid,
        MapId mapId,
        Vector2 from,
        Vector2 to,
        bool enteringOpening,
        out float distance,
        out float distanceFromEdge)
    {
        distance = default;
        distanceFromEdge = float.PositiveInfinity;
        var localFrom = _map.WorldToLocal(grid, grid.Comp, from) / grid.Comp.TileSize;
        var localTo = _map.WorldToLocal(grid, grid.Comp, to) / grid.Comp.TileSize;
        if (!IsFinite(localFrom) || !IsFinite(localTo) ||
            MathF.Abs(localFrom.X) >= int.MaxValue - 1f || MathF.Abs(localFrom.Y) >= int.MaxValue - 1f ||
            MathF.Abs(localTo.X) >= int.MaxValue - 1f || MathF.Abs(localTo.Y) >= int.MaxValue - 1f)
        {
            return false;
        }

        var tile = new Vector2i((int) MathF.Floor(localFrom.X), (int) MathF.Floor(localFrom.Y));
        var endTile = new Vector2i((int) MathF.Floor(localTo.X), (int) MathF.Floor(localTo.Y));
        var crossings = Math.Abs((long) endTile.X - tile.X) + Math.Abs((long) endTile.Y - tile.Y);
        if (crossings > MaxTraceTiles)
            return false;

        var delta = localTo - localFrom;
        // DDA перебирает границы тайлов в локальных координатах сетки, сохраняя поворот и размер тайла.
        var stepX = Math.Sign(delta.X);
        var stepY = Math.Sign(delta.Y);
        var tDeltaX = stepX == 0 ? float.PositiveInfinity : MathF.Abs(1f / delta.X);
        var tDeltaY = stepY == 0 ? float.PositiveInfinity : MathF.Abs(1f / delta.Y);
        var boundaryX = stepX > 0 ? tile.X + 1f : tile.X;
        var boundaryY = stepY > 0 ? tile.Y + 1f : tile.Y;
        var tMaxX = stepX == 0 ? float.PositiveInfinity : (boundaryX - localFrom.X) / delta.X;
        var tMaxY = stepY == 0 ? float.PositiveInfinity : (boundaryY - localFrom.Y) / delta.Y;
        var length = Vector2.Distance(from, to);
        var wasEmpty = !_map.TryGetTileRef(grid, grid.Comp, tile, out var sourceTile) || sourceTile.Tile.IsEmpty;

        for (var crossed = 0; crossed <= crossings; crossed++)
        {
            var t = MathF.Min(tMaxX, tMaxY);
            if (t > 1f || !float.IsFinite(t))
                return false;

            var crossedX = tMaxX <= tMaxY;
            var crossedY = tMaxY <= tMaxX;
            if (crossedX)
            {
                tile.X += stepX;
                tMaxX += tDeltaX;
            }

            if (crossedY)
            {
                tile.Y += stepY;
                tMaxY += tDeltaY;
            }

            var empty = !_map.TryGetTileRef(grid, grid.Comp, tile, out var tileRef) || tileRef.Tile.IsEmpty;
            var changed = empty != wasEmpty;
            wasEmpty = empty;
            if (!changed || empty != enteringOpening)
                continue;

            distance = MathF.Max(0f, t) * length;
            // Проём в одной сетке может быть закрыт полом другой сетки на той же карте.
            var afterBoundary = from + (to - from) / length * (distance + BoundaryEpsilon);
            if (IsEmptyAt(new MapCoordinates(afterBoundary, mapId)) != enteringOpening)
                continue;

            if (enteringOpening)
            {
                // Длина наклонного луча до проёма не определяет, насколько близко стрелок стоит к краю.
                if (crossedX)
                    distanceFromEdge = GetDistanceFromEdge(grid, mapId, from, localFrom, tile, true, stepX);
                if (crossedY)
                {
                    distanceFromEdge = MathF.Min(distanceFromEdge,
                        GetDistanceFromEdge(grid, mapId, from, localFrom, tile, false, stepY));
                }
            }

            return true;
        }

        return false;
    }

    private float GetDistanceFromEdge(
        Entity<MapGridComponent> grid,
        MapId mapId,
        Vector2 from,
        Vector2 localFrom,
        Vector2i openingTile,
        bool vertical,
        int step)
    {
        var plane = (vertical ? openingTile.X : openingTile.Y) + (step < 0 ? 1f : 0f);
        var tangent = vertical ? localFrom.Y : localFrom.X;
        var segment = (int) MathF.Floor(tangent);
        var matrix = _transform.GetWorldMatrix(grid);
        var normal = Vector2.Normalize(Vector2.TransformNormal(
            vertical ? new Vector2(step, 0f) : new Vector2(0f, step), matrix));
        var inset = BoundaryEpsilon / grid.Comp.TileSize;
        var nearestSquared = float.PositiveInfinity;

        // В пределах одного тайла достаточно сегмента под проекцией и двух соседних; зажатие учитывает углы проёма.
        for (var index = segment - 1; index <= segment + 1; index++)
        {
            var closestTangent = Math.Clamp(tangent, index, index + 1f);
            var closest = vertical ? new Vector2(plane, closestTangent) : new Vector2(closestTangent, plane);
            var worldClosest = Vector2.Transform(closest * grid.Comp.TileSize, matrix);
            var distanceSquared = Vector2.DistanceSquared(from, worldClosest);
            if (distanceSquared >= nearestSquared)
                continue;

            var probeTangent = Math.Clamp(closestTangent, index + inset, index + 1f - inset);
            var probe = vertical ? new Vector2(plane, probeTangent) : new Vector2(probeTangent, plane);
            var worldProbe = Vector2.Transform(probe * grid.Comp.TileSize, matrix);
            // Грань должна действительно разделять пол и пустоту, в том числе с учётом других сеток.
            if (!IsPhysicalEdge(mapId, worldProbe, normal))
            {
                var start = vertical ? new Vector2(plane, index) : new Vector2(index, plane);
                var end = vertical ? new Vector2(plane, index + 1f) : new Vector2(index + 1f, plane);
                nearestSquared = MathF.Min(nearestSquared, GetNearestOpenEdgeDistanceSquared(mapId, from,
                    Vector2.Transform(start * grid.Comp.TileSize, matrix),
                    Vector2.Transform(end * grid.Comp.TileSize, matrix), normal));
                continue;
            }

            nearestSquared = distanceSquared;
        }

        return MathF.Sqrt(nearestSquared);
    }

    private bool IsPhysicalEdge(MapId mapId, Vector2 position, Vector2 normal)
    {
        return !IsEmptyAt(new MapCoordinates(position - normal * BoundaryEpsilon, mapId)) &&
               IsEmptyAt(new MapCoordinates(position + normal * BoundaryEpsilon, mapId));
    }

    /// <summary>
    /// Finds the closest open part of a finite edge segment after overlapping grids cover its nearest projection.
    /// Tile boundary intersections partition both probe lines into intervals with constant floor occupancy.
    /// </summary>
    private float GetNearestOpenEdgeDistanceSquared(MapId mapId, Vector2 from, Vector2 start, Vector2 end, Vector2 normal)
    {
        var direction = end - start;
        var lengthSquared = direction.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= 0f)
            return float.PositiveInfinity;

        _edgeCuts.Clear();
        _edgeGrids.Clear();
        try
        {
            _edgeCuts.Add(0f);
            _edgeCuts.Add(1f);
            // Как в TryFindGridAt, расширяем поиск из-за небольшого сжатия границ сетки.
            var margin = new Vector2(0.2f);
            var bounds = new Box2(Vector2.Min(start, end) - margin, Vector2.Max(start, end) + margin);
            _mapManager.FindGridsIntersecting(mapId, bounds, ref _edgeGrids, approx: true, includeMap: true);
            var side = normal * BoundaryEpsilon;
            foreach (var grid in _edgeGrids)
            {
                if (!TryAddEdgeCuts(grid, start - side, end - side) ||
                    !TryAddEdgeCuts(grid, start + side, end + side))
                {
                    return float.PositiveInfinity;
                }
            }

            _edgeCuts.Sort();
            var projection = Vector2.Dot(from - start, direction) / lengthSquared;
            var nearestSquared = float.PositiveInfinity;
            for (var index = 1; index < _edgeCuts.Count; index++)
            {
                var previous = _edgeCuts[index - 1];
                var next = _edgeCuts[index];
                if (next <= previous || !IsPhysicalEdge(mapId, start + direction * ((previous + next) * 0.5f), normal))
                    continue;

                var closest = start + direction * Math.Clamp(projection, previous, next);
                nearestSquared = MathF.Min(nearestSquared, Vector2.DistanceSquared(from, closest));
            }

            return nearestSquared;
        }
        finally
        {
            _edgeGrids.Clear();
            _edgeCuts.Clear();
        }
    }

    private bool TryAddEdgeCuts(Entity<MapGridComponent> grid, Vector2 start, Vector2 end)
    {
        var localStart = _map.WorldToLocal(grid, grid.Comp, start) / grid.Comp.TileSize;
        var localEnd = _map.WorldToLocal(grid, grid.Comp, end) / grid.Comp.TileSize;
        if (!IsFinite(localStart) || !IsFinite(localEnd) ||
            MathF.Abs(localStart.X) >= int.MaxValue - 1f || MathF.Abs(localStart.Y) >= int.MaxValue - 1f ||
            MathF.Abs(localEnd.X) >= int.MaxValue - 1f || MathF.Abs(localEnd.Y) >= int.MaxValue - 1f)
        {
            return false;
        }

        return TryAddAxisCuts(localStart.X, localEnd.X) && TryAddAxisCuts(localStart.Y, localEnd.Y);
    }

    private bool TryAddAxisCuts(float start, float end)
    {
        if (start == end)
            return true;

        var first = (int) MathF.Floor(MathF.Min(start, end)) + 1;
        var last = (int) MathF.Ceiling(MathF.Max(start, end));
        if ((long) last - first > MaxTraceTiles)
            return false;

        for (var boundary = first; boundary < last; boundary++)
        {
            var intersection = (boundary - start) / (end - start);
            if (intersection > 0f && intersection < 1f)
                _edgeCuts.Add(intersection);
        }

        return true;
    }

    private static bool IsFinite(Vector2 position)
    {
        return float.IsFinite(position.X) && float.IsFinite(position.Y);
    }
}

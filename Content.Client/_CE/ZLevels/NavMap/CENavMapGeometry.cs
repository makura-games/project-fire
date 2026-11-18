/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using Content.Shared.Atmos;
using Content.Shared.Pinpointer;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Client._CE.ZLevels.NavMap;

/// <summary>
/// Pure geometry data for a single grid's nav-map schematic, in grid-local tile coordinates.
/// No offset/scale/colour is applied here — that is the drawing control's job.
/// </summary>
public sealed class CENavMapGeometryData
{
    /// <summary>
    /// Combined wall line segments (Y already negated, matching the upstream NavMapControl convention).
    /// </summary>
    public readonly List<(Vector2 Start, Vector2 End)> WallLines = new();

    /// <summary>Thin-wall / airlock rectangles, as (leftTop, rightBottom) pairs (Y already negated).
    /// </summary>
    public readonly List<(Vector2 LeftTop, Vector2 RightBottom)> WallRects = new();

    /// <summary>
    /// Floor fill rectangles, as (leftBottom, rightTop) pairs in raw grid coordinates (Y not negated).
    /// </summary>
    public readonly List<(Vector2 Min, Vector2 Max)> FloorRects = new();

    /// <summary>
    /// Outline segments along the outer edge of the floored area, in raw grid coordinates (Y not negated).
    /// </summary>
    public readonly List<(Vector2 Start, Vector2 End)> FloorPerimeter = new();

    public void Clear()
    {
        WallLines.Clear();
        WallRects.Clear();
        FloorRects.Clear();
        FloorPerimeter.Clear();
    }
}

/// <summary>
/// Builds <see cref="CENavMapGeometryData"/> from a grid's <see cref="NavMapComponent"/>.
/// The algorithm is ported verbatim from <c>Content.Client.Pinpointer.UI.NavMapControl</c>
/// (walls / thin walls / airlocks / floor tiles), factored out so it can be reused by any
/// CE control that needs to draw more than one grid.
/// </summary>
public static class CENavMapGeometry
{
    private const float FullWallInstep = 0.165f;
    private const float ThinWallThickness = 0.165f;
    private const float ThinDoorThickness = 0.30f;

    private const int SouthMask = (int)AtmosDirection.South << (int)NavMapChunkType.Wall;
    private const int EastMask = (int)AtmosDirection.East << (int)NavMapChunkType.Wall;
    private const int WestMask = (int)AtmosDirection.West << (int)NavMapChunkType.Wall;
    private const int NorthMask = (int)AtmosDirection.North << (int)NavMapChunkType.Wall;

    public static void Build(NavMapComponent nav, MapGridComponent grid, CENavMapGeometryData into)
    {
        into.Clear();

        BuildFloorTiles(nav, grid, into);
        BuildFloorPerimeter(nav, grid, into);
        BuildWallLines(nav, grid, into);
        BuildAirlocks(nav, grid, into);
    }

    private static bool IsFloored(NavMapComponent nav, Vector2i gridTile)
    {
        var chunkOrigin = SharedMapSystem.GetChunkIndices(gridTile, SharedNavMapSystem.ChunkSize);
        if (!nav.Chunks.TryGetValue(chunkOrigin, out var chunk))
            return false;

        var rel = SharedMapSystem.GetChunkRelative(gridTile, SharedNavMapSystem.ChunkSize);
        return (chunk.TileData[SharedNavMapSystem.GetTileIndex(rel)] & SharedNavMapSystem.FloorMask) != 0;
    }

    /// <summary>
    /// Emits an outline segment for every floored-tile edge that borders a non-floored tile.
    /// </summary>
    private static void BuildFloorPerimeter(NavMapComponent nav, MapGridComponent grid, CENavMapGeometryData data)
    {
        var size = grid.TileSize;

        foreach (var chunk in nav.Chunks.Values)
        {
            var baseTile = chunk.Origin * SharedNavMapSystem.ChunkSize;

            for (var i = 0; i < SharedNavMapSystem.ArraySize; i++)
            {
                if ((chunk.TileData[i] & SharedNavMapSystem.FloorMask) == 0)
                    continue;

                var t = baseTile + SharedNavMapSystem.GetTileFromIndex(i);

                var x0 = t.X * size;
                var x1 = x0 + size;
                var y0 = t.Y * size;
                var y1 = y0 + size;

                if (!IsFloored(nav, t + new Vector2i(-1, 0)))
                    data.FloorPerimeter.Add((new Vector2(x0, y0), new Vector2(x0, y1)));

                if (!IsFloored(nav, t + new Vector2i(1, 0)))
                    data.FloorPerimeter.Add((new Vector2(x1, y0), new Vector2(x1, y1)));

                if (!IsFloored(nav, t + new Vector2i(0, -1)))
                    data.FloorPerimeter.Add((new Vector2(x0, y0), new Vector2(x1, y0)));

                if (!IsFloored(nav, t + new Vector2i(0, 1)))
                    data.FloorPerimeter.Add((new Vector2(x0, y1), new Vector2(x1, y1)));
            }
        }
    }

    /// <summary>
    /// Floor fill is derived straight from the nav-map's floor bits (authoritative and always
    /// replicated), not from grid fixtures. Runs within each chunk are merged into horizontal
    /// rectangles to keep the rect count low.
    /// </summary>
    private static void BuildFloorTiles(NavMapComponent nav, MapGridComponent grid, CENavMapGeometryData data)
    {
        var size = grid.TileSize;

        foreach (var chunk in nav.Chunks.Values)
        {
            var baseTile = chunk.Origin * SharedNavMapSystem.ChunkSize;

            for (var y = 0; y < SharedNavMapSystem.ChunkSize; y++)
            {
                var runStart = -1;

                for (var x = 0; x <= SharedNavMapSystem.ChunkSize; x++)
                {
                    var floored = x < SharedNavMapSystem.ChunkSize &&
                                  (chunk.TileData[SharedNavMapSystem.GetTileIndex(new Vector2i(x, y))] &
                                   SharedNavMapSystem.FloorMask) != 0;

                    if (floored)
                    {
                        if (runStart < 0)
                            runStart = x;

                        continue;
                    }

                    if (runStart < 0)
                        continue;

                    var minX = (baseTile.X + runStart) * size;
                    var maxX = (baseTile.X + x) * size;
                    var minY = (baseTile.Y + y) * size;
                    var maxY = minY + size;

                    data.FloorRects.Add((new Vector2(minX, minY), new Vector2(maxX, maxY)));
                    runStart = -1;
                }
            }
        }
    }

    private static void BuildWallLines(NavMapComponent nav, MapGridComponent grid, CENavMapGeometryData data)
    {
        // Dictionaries used to combine collinear wall lines.
        var horizLines = new Dictionary<Vector2i, Vector2i>();
        var horizLinesReversed = new Dictionary<Vector2i, Vector2i>();
        var vertLines = new Dictionary<Vector2i, Vector2i>();
        var vertLinesReversed = new Dictionary<Vector2i, Vector2i>();

        foreach (var (chunkOrigin, chunk) in nav.Chunks)
        {
            for (var i = 0; i < SharedNavMapSystem.ArraySize; i++)
            {
                var tileData = chunk.TileData[i] & SharedNavMapSystem.WallMask;
                if (tileData == 0)
                    continue;

                tileData >>= (int)NavMapChunkType.Wall;

                var relativeTile = SharedNavMapSystem.GetTileFromIndex(i);
                var tile = (chunk.Origin * SharedNavMapSystem.ChunkSize + relativeTile) * grid.TileSize;

                if (tileData != SharedNavMapSystem.AllDirMask)
                {
                    AddRectForThinWall(data, tileData, tile);
                    continue;
                }

                tile = tile with { Y = -tile.Y };
                NavMapChunk? neighborChunk;

                // North edge
                var neighborData = 0;
                if (relativeTile.Y != SharedNavMapSystem.ChunkSize - 1)
                    neighborData = chunk.TileData[i + 1];
                else if (nav.Chunks.TryGetValue(chunkOrigin + Vector2i.Up, out neighborChunk))
                    neighborData = neighborChunk.TileData[i + 1 - SharedNavMapSystem.ChunkSize];

                if ((neighborData & SouthMask) == 0)
                {
                    AddOrUpdateNavMapLine(tile + new Vector2i(0, -grid.TileSize),
                        tile + new Vector2i(grid.TileSize, -grid.TileSize),
                        horizLines,
                        horizLinesReversed);
                }

                // East edge
                neighborData = 0;
                if (relativeTile.X != SharedNavMapSystem.ChunkSize - 1)
                    neighborData = chunk.TileData[i + SharedNavMapSystem.ChunkSize];
                else if (nav.Chunks.TryGetValue(chunkOrigin + Vector2i.Right, out neighborChunk))
                {
                    neighborData =
                        neighborChunk.TileData[i + SharedNavMapSystem.ChunkSize - SharedNavMapSystem.ArraySize];
                }

                if ((neighborData & WestMask) == 0)
                {
                    AddOrUpdateNavMapLine(tile + new Vector2i(grid.TileSize, -grid.TileSize),
                        tile + new Vector2i(grid.TileSize, 0),
                        vertLines,
                        vertLinesReversed);
                }

                // South edge
                neighborData = 0;
                if (relativeTile.Y != 0)
                    neighborData = chunk.TileData[i - 1];
                else if (nav.Chunks.TryGetValue(chunkOrigin + Vector2i.Down, out neighborChunk))
                    neighborData = neighborChunk.TileData[i - 1 + SharedNavMapSystem.ChunkSize];

                if ((neighborData & NorthMask) == 0)
                {
                    AddOrUpdateNavMapLine(tile, tile + new Vector2i(grid.TileSize, 0), horizLines, horizLinesReversed);
                }

                // West edge
                neighborData = 0;
                if (relativeTile.X != 0)
                    neighborData = chunk.TileData[i - SharedNavMapSystem.ChunkSize];
                else if (nav.Chunks.TryGetValue(chunkOrigin + Vector2i.Left, out neighborChunk))
                {
                    neighborData =
                        neighborChunk.TileData[i - SharedNavMapSystem.ChunkSize + SharedNavMapSystem.ArraySize];
                }

                if ((neighborData & EastMask) == 0)
                {
                    AddOrUpdateNavMapLine(tile + new Vector2i(0, -grid.TileSize), tile, vertLines, vertLinesReversed);
                }

                // Diagonal line for interiors.
                data.WallLines.Add((tile + new Vector2(0, -grid.TileSize), tile + new Vector2(grid.TileSize, 0)));
            }
        }

        foreach (var (origin, terminal) in horizLines)
        {
            data.WallLines.Add((origin, terminal));
        }

        foreach (var (origin, terminal) in vertLines)
        {
            data.WallLines.Add((origin, terminal));
        }
    }

    private static void BuildAirlocks(NavMapComponent nav, MapGridComponent grid, CENavMapGeometryData data)
    {
        foreach (var chunk in nav.Chunks.Values)
        {
            for (var i = 0; i < SharedNavMapSystem.ArraySize; i++)
            {
                var tileData = chunk.TileData[i] & SharedNavMapSystem.AirlockMask;
                if (tileData == 0)
                    continue;

                tileData >>= (int)NavMapChunkType.Airlock;

                var relative = SharedNavMapSystem.GetTileFromIndex(i);
                var tile = (chunk.Origin * SharedNavMapSystem.ChunkSize + relative) * grid.TileSize;

                if (tileData != SharedNavMapSystem.AllDirMask)
                {
                    AddRectForThinAirlock(data, tileData, tile);
                    continue;
                }

                data.WallRects.Add((new Vector2(tile.X + FullWallInstep, -tile.Y - FullWallInstep),
                    new Vector2(tile.X - FullWallInstep + 1f, -tile.Y + FullWallInstep - 1)));

                data.WallLines.Add((new Vector2(tile.X + 0.5f, -tile.Y - FullWallInstep),
                    new Vector2(tile.X + 0.5f, -tile.Y + FullWallInstep - 1)));
            }
        }
    }

    private static void AddRectForThinWall(CENavMapGeometryData data, int tileData, Vector2i tile)
    {
        var leftTop = new Vector2(-0.5f, 0.5f - ThinWallThickness);
        var rightBottom = new Vector2(0.5f, 0.5f);

        for (var i = 0; i < SharedNavMapSystem.Directions; i++)
        {
            var dirMask = 1 << i;
            if ((tileData & dirMask) == 0)
                continue;

            var tilePosition = new Vector2(tile.X + 0.5f, -tile.Y - 0.5f);
            var angle = -((AtmosDirection)dirMask).ToAngle();
            data.WallRects.Add((angle.RotateVec(leftTop) + tilePosition, angle.RotateVec(rightBottom) + tilePosition));
        }
    }

    private static void AddRectForThinAirlock(CENavMapGeometryData data, int tileData, Vector2i tile)
    {
        var leftTop = new Vector2(-0.5f + FullWallInstep, 0.5f - FullWallInstep - ThinDoorThickness);
        var rightBottom = new Vector2(0.5f - FullWallInstep, 0.5f - FullWallInstep);
        var centreTop = new Vector2(0f, 0.5f - FullWallInstep - ThinDoorThickness);
        var centreBottom = new Vector2(0f, 0.5f - FullWallInstep);

        for (var i = 0; i < SharedNavMapSystem.Directions; i++)
        {
            var dirMask = 1 << i;
            if ((tileData & dirMask) == 0)
                continue;

            var tilePosition = new Vector2(tile.X + 0.5f, -tile.Y - 0.5f);
            var angle = -((AtmosDirection)dirMask).ToAngle();
            data.WallRects.Add((angle.RotateVec(leftTop) + tilePosition, angle.RotateVec(rightBottom) + tilePosition));
            data.WallLines.Add(
                (angle.RotateVec(centreTop) + tilePosition, angle.RotateVec(centreBottom) + tilePosition));
        }
    }

    /// <summary>
    /// Greedily merges collinear axis-aligned segments while inserting a new one. Mirrors the
    /// <c>protected</c> method of the same name on the upstream <c>NavMapControl</c>; exposed here so
    /// other CE map overlays (e.g. power cable networks) can reuse the same line-combining pass.
    /// </summary>
    public static void AddOrUpdateNavMapLine(
        Vector2i origin,
        Vector2i terminus,
        Dictionary<Vector2i, Vector2i> lookup,
        Dictionary<Vector2i, Vector2i> lookupReversed)
    {
        Vector2i foundTermius;
        Vector2i foundOrigin;

        if (origin == terminus)
            return;

        // Does our new line end at the beginning of an existing line?
        if (lookup.Remove(terminus, out foundTermius))
        {
            DebugTools.Assert(lookupReversed[foundTermius] == terminus);

            // Does our new line start at the end of an existing line?
            if (lookupReversed.Remove(origin, out foundOrigin))
            {
                // Our new line just connects two existing lines
                DebugTools.Assert(lookup[foundOrigin] == origin);
                lookup[foundOrigin] = foundTermius;
                lookupReversed[foundTermius] = foundOrigin;
            }
            else
            {
                // Our new line precedes an existing line, extending it further to the left
                lookup[origin] = foundTermius;
                lookupReversed[foundTermius] = origin;
            }

            return;
        }

        // Does our new line start at the end of an existing line?
        if (lookupReversed.Remove(origin, out foundOrigin))
        {
            // Our new line just extends an existing line further to the right
            DebugTools.Assert(lookup[foundOrigin] == origin);
            lookup[foundOrigin] = terminus;
            lookupReversed[terminus] = foundOrigin;
            return;
        }

        // Completely disconnected line segment.
        lookup.Add(origin, terminus);
        lookupReversed.Add(terminus, origin);
    }
}

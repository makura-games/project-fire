/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Linq;
using Robust.Shared.Analyzers;
using System.Numerics;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared._CE.ZLevels.Roof;

/// <summary>
/// Systems that automatically covers tiles with roofs (or removes roofs)
/// if there is a tile on one of the levels above in the ZLevels Map or Grid network.
/// </summary>
public abstract partial class CESharedZLevelsRoofSystem : EntitySystem
{
    [Dependency] protected CESharedZLevelsSystem ZLevel = null!;
    [Dependency] protected SharedRoofSystem Roof = null!;
    [Dependency] protected SharedMapSystem Map = null!;
    [Dependency] protected ITileDefinitionManager TilDefMan = null!;

    [Dependency] protected EntityQuery<MapGridComponent> GridQuery = default!;
    [Dependency] protected EntityQuery<RoofComponent> RoofQuery = default!;
    [Dependency] protected EntityQuery<CEZMapComponent> ZMapQuery = default!;
    [Dependency] protected EntityQuery<TransformComponent> XformQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CEZLevelRoofComponent, TileChangedEvent>(OnTileChanged);
    }

    private void OnTileChanged(Entity<CEZLevelRoofComponent> ent, ref TileChangedEvent args)
    {
        if (!GridQuery.TryComp(ent, out var currentMapGrid))
            return;
        if (!RoofQuery.TryComp(ent, out var currentRoof))
            return;

        if (args.Changes.Length == 0)
            return;

        if (ZMapQuery.TryComp(ent, out var zLevelMapComp))
            OnMapTileChanged(ent, currentMapGrid, currentRoof, zLevelMapComp, args);
        else
            OnGridTileChanged(ent, currentMapGrid, currentRoof, args);
    }

    /// <summary>
    /// Planetary map path: propagate roof state down through the z-map network.
    /// </summary>
    private void OnMapTileChanged(
        EntityUid mapUid,
        MapGridComponent currentMapGrid,
        RoofComponent currentRoof,
        CEZMapComponent zLevelMapComp,
        TileChangedEvent args)
    {
        var mapsBelow = ZLevel.GetAllMapsBelow((mapUid, zLevelMapComp));
        if (mapsBelow.Count == 0)
            return;

        var mapsAbove = ZLevel.GetAllMapsAbove((mapUid, zLevelMapComp));
        Dictionary<Vector2i, (bool BlocksLight, bool BlocksWeather)> roofMap = new();
        foreach (var change in args.Changes)
        {
            var indices = change.GridIndices;
            var blocksLight = Roof.IsRooved((mapUid, currentMapGrid, currentRoof), indices) || TileBlocksLight(change.NewTile);
            var blocksWeather = Roof.IsWeatherOccluding((mapUid, currentMapGrid, currentRoof), indices) || !change.NewTile.IsEmpty;

            // После загрузки карта с пустой клеткой может ещё не иметь маски крыши над ней.
            foreach (var mapAbove in mapsAbove)
            {
                if (blocksLight && blocksWeather)
                    break;

                if (!GridQuery.TryComp(mapAbove, out var mapGridAbove) ||
                    !Map.TryGetTile(mapGridAbove, indices, out var tile) || tile.IsEmpty)
                {
                    continue;
                }

                blocksLight |= TileBlocksLight(tile);
                blocksWeather = true;
            }

            roofMap[indices] = (blocksLight, blocksWeather);
        }

        foreach (var mapBelow in mapsBelow)
        {
            if (!GridQuery.TryComp(mapBelow, out var mapGridBelow))
                continue;

            var roofBelow = EnsureComp<RoofComponent>(mapBelow);

            foreach (var (indices, coverage) in roofMap)
            {
                Roof.SetRoof((mapBelow, mapGridBelow, roofBelow), indices, coverage.BlocksLight,
                    occludeWeather: coverage.BlocksWeather);

                if (Map.TryGetTile(mapGridBelow, indices, out var tile) && !tile.IsEmpty)
                    roofMap[indices] = (coverage.BlocksLight || TileBlocksLight(tile), true);
            }
        }
    }

    private void OnGridTileChanged(
        EntityUid gridUid,
        MapGridComponent currentMapGrid,
        RoofComponent currentRoof,
        TileChangedEvent args)
    {
        if (!ZLevel.TryGetGridNetwork(gridUid, out var network))
            return;

        if (ZLevel.TryGetGridZDepth(gridUid) is not { } ownDepth)
            return;

        var otherGrids = network.Comp.Grids
            .Select(g => (Grid: g, Depth: ZLevel.TryGetGridZDepth(g)))
            .Where(x => x.Depth is { } d && d != ownDepth)
            .OrderByDescending(x => x.Depth!.Value)
            .ToList();

        if (otherGrids.Count == 0 || otherGrids[^1].Depth >= ownDepth)
            return;

        foreach (var change in args.Changes)
        {
            var worldTile = ZLevel.GridTileToWorldTile(gridUid, currentMapGrid, change.GridIndices);
            var blocksLight = Roof.IsRooved((gridUid, currentMapGrid, currentRoof), change.GridIndices) ||
                              TileBlocksLight(change.NewTile);
            var blocksWeather = Roof.IsWeatherOccluding((gridUid, currentMapGrid, currentRoof), change.GridIndices) ||
                                !change.NewTile.IsEmpty;

            foreach (var (otherGrid, depth) in otherGrids)
            {
                if (!GridQuery.TryComp(otherGrid, out var otherMapGrid))
                    continue;

                if (!TryWorldTileToLocalTile(otherGrid, otherMapGrid, worldTile, out var localTile))
                    continue;

                if (!Map.TryGetTileRef(otherGrid, otherMapGrid, localTile, out var tileRef) || tileRef.Tile.IsEmpty)
                    continue;

                if (depth < ownDepth)
                {
                    var otherRoof = EnsureComp<RoofComponent>(otherGrid);
                    Roof.SetRoof((otherGrid, otherMapGrid, otherRoof), localTile, blocksLight, occludeWeather: blocksWeather);
                }

                blocksLight |= TileBlocksLight(tileRef.Tile);
                blocksWeather = true;
            }
        }
    }

    protected bool TileBlocksLight(Tile tile) => !CEZLevelOpeningCache.IsOpeningTile(tile, TilDefMan);

    /// <summary>
    /// Resolves a world tile (shared X,Y convention across Z-level maps — see
    /// <see cref="CESharedZLevelsSystem"/>'s <c>TryMove</c>) into <paramref name="grid"/>'s own local
    /// tile index. Z-levels are separate MapIds, so this deliberately tags the shared (X,Y) with
    /// <paramref name="gridUid"/>'s own MapId rather than the caller's, exploiting that convention.
    /// Non-throwing: <paramref name="gridUid"/> could have been deleted between being read out of the
    /// z-grid network and this call (e.g. a ZCollapse that ate the whole grid).
    /// </summary>
    private bool TryWorldTileToLocalTile(EntityUid gridUid, MapGridComponent grid, Vector2i worldTile, out Vector2i localTile)
    {
        localTile = default;
        if (!XformQuery.TryGetComponent(gridUid, out var xform))
            return false;

        var worldPos = new Vector2(worldTile.X + 0.5f, worldTile.Y + 0.5f);
        localTile = Map.TileIndicesFor(gridUid, grid, new MapCoordinates(worldPos, xform.MapID));
        return true;
    }
}

/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using Robust.Shared.Analyzers;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.Maps;
using Content.Shared.Toggleable;
using JetBrains.Annotations;
using Robust.Shared.Map;

namespace Content.Shared._CE.ZLevels.Core.EntitySystems;

public abstract partial class CESharedZLevelsSystem
{
    private void InitializeViewSubscriptions()
    {
        SubscribeLocalEvent<CEZLevelViewerComponent, ToggleActionEvent>(OnToggleLookUp);
    }

    [Dependency] protected ITileDefinitionManager TilDefMan = null!;

    private void OnToggleLookUp(Entity<CEZLevelViewerComponent> entity, ref ToggleActionEvent args)
    {
        if (args.Handled)
            return;

        if (args.Action.Owner != entity.Comp.ActionEntity)
            return;

        args.Handled = true;

        entity.Comp.LookUp = !entity.Comp.LookUp;
        DirtyField(entity, entity.Comp, nameof(CEZLevelViewerComponent.LookUp));

        _actions.SetToggled(entity.Comp.ActionEntity, entity.Comp.LookUp);
    }

    /// <summary>
    /// Calculates how many z-levels above the entity's current position are visible (i.e. not blocked by an opaque tile),
    /// up to <see cref="MaxZLevelsAboveRendering"/>.
    /// </summary>
    public int GetVisibleZLevelsAbove(EntityUid ent, Entity<CEZMapComponent?>? currentMapUid = null)
    {
        currentMapUid ??= Transform(ent).MapUid;

        if (currentMapUid is null)
            return 0;

        if (!TryMapUp(currentMapUid.Value, out var checkingMap))
            return 0;

        var worldPos = _transform.GetWorldPosition(ent);
        var visibleLevels = 0;

        for (var i = 1; i <= MaxZLevelsAboveRendering; i++)
        {
            // No grid or no chunk at this position means there's simply nothing there (open space),
            // not an opaque roof, so it shouldn't block sight upward. Mirrors HasOpaqueAbove below.
            if (_mapManager.TryFindGridAt(checkingMap, worldPos, out var gridUid, out var grid) &&
                _map.TryGetTileRef(gridUid, grid, worldPos, out var tileRef))
            {
                var tileDef = (ContentTileDefinition)TilDefMan[tileRef.Tile.TypeId];
                if (!tileDef.Transparent)
                    break;
            }

            visibleLevels++;

            if (i == MaxZLevelsAboveRendering || !TryMapUp(checkingMap.AsNullable(), out checkingMap))
                break;
        }

        return visibleLevels;
    }

    /// <summary>
    /// Checks whether any grid on the map above has an opaque (non-transparent) tile at the given world position.
    /// </summary>
    [PublicAPI]
    public bool HasOpaqueAbove(Vector2 worldPos, Entity<CEZMapComponent?> currentMap)
    {
        if (!TryMapUp(currentMap, out var mapAboveUid))
            return false;

        if (!_mapManager.TryFindGridAt(mapAboveUid, worldPos, out var gridUid, out var grid))
            return false;

        if (!_map.TryGetTileRef(gridUid, grid, worldPos, out var tileRef))
            return false;

        return !((ContentTileDefinition)TilDefMan[tileRef.Tile.TypeId]).Transparent;
    }

    public bool TryFindZShotOpening(
        EntityUid sourceMap,
        EntityUid targetMap,
        int offset,
        Vector2 from,
        Vector2 to,
        out Vector2 opening,
        bool preferOpeningAwayFromSource = false,
        float maxSourceDistanceFromOpeningEdgeTiles = float.PositiveInfinity)
    {
        opening = default;
        if (offset == 0)
            return false;

        var openingMap = offset < 0 ? sourceMap : targetMap;
        if (!_gridQuery.TryComp(openingMap, out var grid))
            return false;

        var sourceTile = preferOpeningAwayFromSource
            ? _map.WorldToTile(openingMap, grid, from)
            : default;

        var fallbackOpening = Vector2.Zero;
        var hasFallbackOpening = false;

        var maxSourceDistanceFromOpeningCenter = float.IsPositiveInfinity(maxSourceDistanceFromOpeningEdgeTiles)
            ? float.PositiveInfinity
            : grid.TileSize * (0.5f + Math.Max(0f, maxSourceDistanceFromOpeningEdgeTiles));

        var maxSourceDistanceSquared = maxSourceDistanceFromOpeningCenter * maxSourceDistanceFromOpeningCenter;
        var selectedOpening = Vector2.Zero;

        var localFrom = _map.WorldToLocal(openingMap, grid, from) / grid.TileSize;
        var localTo = _map.WorldToLocal(openingMap, grid, to) / grid.TileSize;

        var localDelta = localTo - localFrom;

        var currentTile = new Vector2i((int) MathF.Floor(localFrom.X), (int) MathF.Floor(localFrom.Y));
        var endTile = new Vector2i((int) MathF.Floor(localTo.X), (int) MathF.Floor(localTo.Y));

        var stepX = Math.Sign(localDelta.X);
        var stepY = Math.Sign(localDelta.Y);

        var tDeltaX = stepX == 0 ? float.PositiveInfinity : MathF.Abs(1f / localDelta.X);
        var tDeltaY = stepY == 0 ? float.PositiveInfinity : MathF.Abs(1f / localDelta.Y);

        var nextBoundaryX = stepX > 0 ? currentTile.X + 1f : currentTile.X;
        var nextBoundaryY = stepY > 0 ? currentTile.Y + 1f : currentTile.Y;

        var tMaxX = stepX == 0 ? float.PositiveInfinity : (nextBoundaryX - localFrom.X) / localDelta.X;
        var tMaxY = stepY == 0 ? float.PositiveInfinity : (nextBoundaryY - localFrom.Y) / localDelta.Y;

        while (true)
        {
            if (TryUseOpeningTile(currentTile))
            {
                opening = selectedOpening;
                return true;
            }

            if (currentTile == endTile)
                break;

            if (tMaxX < tMaxY)
            {
                currentTile += new Vector2i(stepX, 0);
                tMaxX += tDeltaX;
                continue;
            }

            if (tMaxY < tMaxX)
            {
                currentTile += new Vector2i(0, stepY);
                tMaxY += tDeltaY;
                continue;
            }

            currentTile += new Vector2i(stepX, stepY);
            tMaxX += tDeltaX;
            tMaxY += tDeltaY;
        }

        if (!hasFallbackOpening)
            return false;

        opening = fallbackOpening;
        return true;

        bool TryUseOpeningTile(Vector2i tile)
        {
            if (_map.TryGetTileRef(openingMap, grid, tile, out var tileRef) && !CEZLevelOpeningCache.IsOpeningTile(tileRef.Tile, TilDefMan))
                return false;

            var openingCenter = _map.ToCenterCoordinates(openingMap, tile, grid).Position;
            if (Vector2.DistanceSquared(from, openingCenter) > maxSourceDistanceSquared)
                return false;

            if (preferOpeningAwayFromSource && tile == sourceTile)
            {
                if (hasFallbackOpening)
                    return false;

                fallbackOpening = openingCenter;
                hasFallbackOpening = true;

                return false;
            }

            selectedOpening = openingCenter;
            return true;
        }
    }
}

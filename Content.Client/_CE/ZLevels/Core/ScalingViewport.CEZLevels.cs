/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using Content.Client._CE.ZLevels.Core;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared.Maps;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client.Viewport;

public sealed partial class ScalingViewport
{
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private ITileDefinitionManager _tile = default!;
    [Dependency] private IMapManager _map = default!;

    private CEClientZLevelsSystem? _zLevels;
    private SharedMapSystem? _mapSystem;

    private EntityQuery<TransformComponent>? _xformQuery;
    private EntityQuery<MapComponent>? _mapQuery;

    private IEye? _fallbackEye;

    /// <summary>
    /// We are looking for at least one empty tile on the screen.
    /// This is used to ensure that it makes sense to draw the z-planes and that they are visible.
    /// </summary>
    public bool TryFindEmptyTiles(EntityUid mapUid)
    {
        if (_xformQuery is null || !_xformQuery.Value.TryComp(mapUid, out var xform))
            return true;

        var drawBox = GetDrawBox();
        var mapId = xform.MapID;

        var corners = new[]
        {
            _eyeManager.ScreenToMap(drawBox.BottomLeft).Position,
            _eyeManager.ScreenToMap(drawBox.BottomRight).Position,
            _eyeManager.ScreenToMap(drawBox.TopLeft).Position,
            _eyeManager.ScreenToMap(drawBox.TopRight).Position
        };

        float minX = float.MaxValue, minY = float.MaxValue;

        foreach (var c in corners)
        {
            if (c.X < minX)
                minX = c.X;
            if (c.Y < minY)
                minY = c.Y;
        }

        var mapCoordsBottomLeft = new MapCoordinates(new Vector2(minX, minY), mapId);

        if (_mapSystem is null || !_map.TryFindGridAt(mapUid, mapCoordsBottomLeft.Position, out var gridUid, out var grid))
            return true;

        var minTileX = int.MaxValue;
        var minTileY = int.MaxValue;
        var maxTileX = int.MinValue;
        var maxTileY = int.MinValue;

        foreach (var corner in corners)
        {
            var indices = _mapSystem.TileIndicesFor(gridUid, grid, new MapCoordinates(corner, mapId));
            minTileX = Math.Min(minTileX, indices.X);
            minTileY = Math.Min(minTileY, indices.Y);
            maxTileX = Math.Max(maxTileX, indices.X);
            maxTileY = Math.Max(maxTileY, indices.Y);
        }

        for (var x = minTileX - 1; x <= maxTileX + 1; x++)
        {
            for (var y = minTileY - 1; y <= maxTileY + 1; y++)
            {
                var tile = _mapSystem.GetTileRef(gridUid, grid, new Vector2i(x, y));
                var tileDef = (ContentTileDefinition)_tile[tile.Tile.TypeId];
                if (tileDef.Transparent || tile.Tile.IsEmpty)
                    return true;
            }
        }

        return false;
    }

    private void RenderZLevels(IClydeViewport viewport, IRenderHandle handle) // Fire edit - контекст рисования нужен для наложения размытого верхнего этажа на остальную сцену.
    {
        if (_eye is null)
            return;

        viewport.ClearColor = Color.Black;
        _fallbackEye = _eye;

        // Cache frequently accessed components/systems
        _xformQuery ??= _entityManager.GetEntityQuery<TransformComponent>();
        _mapQuery ??= _entityManager.GetEntityQuery<MapComponent>();

        // Cache systems and components
        _zLevels ??= _entityManager.System<CEClientZLevelsSystem>();
        _mapSystem ??= _entityManager.System<SharedMapSystem>();

        if (_player.LocalEntity is null)
        {
            RenderZLevelViewport(viewport); // Fire edit - предпросмотр размещения виден только на своей карте, без копий на соседних этажах.
            return;
        }

        if (!_entityManager.TryGetComponent<CEZLevelViewerComponent>(_player.LocalEntity.Value, out var zLevelViewer))
        {
            RenderZLevelViewport(viewport); // Fire edit - предпросмотр размещения виден только на своей карте, без копий на соседних этажах.
            return;
        }

        if (!_xformQuery.Value.TryComp(_player.LocalEntity, out var playerXform))
        {
            RenderZLevelViewport(viewport); // Fire edit - предпросмотр размещения виден только на своей карте, без копий на соседних этажах.
            return;
        }

        if (playerXform.MapUid is null)
        {
            RenderZLevelViewport(viewport); // Fire edit - предпросмотр размещения виден только на своей карте, без копий на соседних этажах.
            return;
        }

        var lookUp = 0;
        // Fire edit start - по действию взгляда вверх показываем только ближайший верхний этаж.
        if (zLevelViewer.LookUp)
            lookUp = Math.Min(1, _zLevels.GetVisibleZLevelsAbove(_player.LocalEntity.Value, playerXform.MapUid));
        // Fire edit end

        var lowestDepth = 0;
        for (var i = 0; i >= -CESharedZLevelsSystem.MaxZLevelsBelowRendering; i--)
        {
            var checkingMap = playerXform.MapUid.Value;

            if (i != 0)
            {
                if (!_zLevels.TryMapOffset(playerXform.MapUid.Value, i, out var mapUidBelow))
                    continue;

                checkingMap = mapUidBelow;
            }

            lowestDepth = i;

            if (!TryFindEmptyTiles(checkingMap))
                break;
        }

        //From the lowest depth to the highest, render each level
        for (var depth = lowestDepth; depth <= lookUp; depth++)
        {
            if (depth == 0)
                viewport.Eye = _fallbackEye;
            else
            {
                if (!_zLevels.TryMapOffset(playerXform.MapUid.Value, depth, out var mapUidBelow))
                    continue;

                if (!_mapQuery.Value.TryComp(mapUidBelow, out var mapComp))
                    continue;

                Angle rotation = _fallbackEye.Rotation * -1;
                var offset = rotation.ToWorldVec() * CEClientZLevelsSystem.ZLevelOffset * depth;

                viewport.Eye = new ZEye(lowestDepth, depth, lookUp)
                {
                    SourceEye = _fallbackEye, // Fire added - сохраняем исходную камеру, чтобы её FOV и эффекты работали и на этом этаже.
                    Position = new MapCoordinates(_fallbackEye.Position.Position, mapComp.MapId),
                    DrawFov = _fallbackEye.DrawFov && depth >= 0,
                    DrawLight = _fallbackEye.DrawLight,
                    Offset = _fallbackEye.Offset + offset,
                    Rotation = _fallbackEye.Rotation,
                    Scale = _fallbackEye.Scale,
                };
            }

            viewport.ClearColor = depth == lowestDepth ? Color.Black : null;
            // Fire edit start - размываем верхний этаж до наложения, чтобы не размывать вместе с ним текущий.
            if (depth > 0)
                RenderUpperZLevel(viewport, handle);
            else
                RenderZLevelViewport(viewport);
            // Fire edit end
        }

        // Restore the Eye
        Eye = _fallbackEye;
        viewport.Eye = Eye;
    }

    public sealed partial class ZEye(int lowest, int depth, int high) : Robust.Shared.Graphics.Eye
    {
        public int LowestDepth = lowest;
        public int Depth = depth;
        public int HighestDepth = high;
    }
}

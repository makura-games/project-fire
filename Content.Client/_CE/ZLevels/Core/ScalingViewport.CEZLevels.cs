/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using Content.Client._CE.ZLevels.Core;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client.Viewport;

public sealed partial class ScalingViewport
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private ITileDefinitionManager _tile = default!;

    private CEClientZLevelsSystem? _zLevels;
    private SharedMapSystem? _mapSystem;

    private EntityQuery<TransformComponent>? _xformQuery;
    private EntityQuery<MapComponent>? _mapQuery;

    private IEye? _fallbackEye;
    private readonly Vector2[] _zLevelViewCorners = new Vector2[4];

    // Fire edit start - проверяем просветы в фактических границах кадра с учётом смещения этажа.
    internal static Vector2 GetZLevelViewOffset(Angle eyeRotation)
    {
        return (-eyeRotation).ToWorldVec() * CESharedZLevelsSystem.ZLevelOffset;
    }

    private bool HasVisibleZLevelOpening(EntityUid mapUid, ReadOnlySpan<Vector2> corners, Vector2 offset)
    {
        if (_mapSystem is not { } map || !map.TryFindGridAt(mapUid, corners[0] + offset, out var gridUid, out var grid))
            return true;

        var minTile = new Vector2i(int.MaxValue, int.MaxValue);
        var maxTile = new Vector2i(int.MinValue, int.MinValue);

        foreach (var corner in corners)
        {
            var tile = map.WorldToTile(gridUid, grid, corner + offset);
            minTile = Vector2i.ComponentMin(minTile, tile);
            maxTile = Vector2i.ComponentMax(maxTile, tile);
        }

        var gridEntity = new Entity<MapGridComponent>(gridUid, grid);
        for (var x = minTile.X - 1; x <= maxTile.X + 1; x++)
        {
            for (var y = minTile.Y - 1; y <= maxTile.Y + 1; y++)
            {
                if (CEZLevelOpeningCache.IsOpeningTile(gridEntity, new Vector2i(x, y), map, _tile))
                    return true;
            }
        }

        return false;
    }
    // Fire edit end

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

        // Fire added start - используем углы буфера, чтобы смещение и масштаб UI не влияли на проверку просветов.
        _zLevelViewCorners[0] = viewport.LocalToWorld(Vector2.Zero).Position;
        _zLevelViewCorners[1] = viewport.LocalToWorld(new Vector2(viewport.Size.X, 0)).Position;
        _zLevelViewCorners[2] = viewport.LocalToWorld(new Vector2(0, viewport.Size.Y)).Position;
        _zLevelViewCorners[3] = viewport.LocalToWorld(viewport.Size).Position;
        var offsetPerLevel = GetZLevelViewOffset(_fallbackEye.Rotation);
        // Fire added end

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

            if (!HasVisibleZLevelOpening(checkingMap, _zLevelViewCorners, offsetPerLevel * i)) // Fire edit
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

                viewport.Eye = new ZEye(lowestDepth, depth, lookUp)
                {
                    SourceEye = _fallbackEye, // Fire added - сохраняем исходную камеру, чтобы её FOV и эффекты работали и на этом этаже.
                    Position = new MapCoordinates(_fallbackEye.Position.Position, mapComp.MapId),
                    DrawFov = _fallbackEye.DrawFov && depth >= 0,
                    DrawLight = _fallbackEye.DrawLight,
                    Offset = _fallbackEye.Offset + offsetPerLevel * depth, // Fire edit
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

using Content.Client._CE.ZLevels.Core;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._Scp.ZLevels;
using Content.Shared.PneumaticCannon;
using Robust.Shared.Map;

namespace Content.Client.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{
    // Выбор видимого этажа под курсором с учётом смещения рендера.
    [Dependency] private CEClientZLevelsSystem _zLevels = default!;
    [Dependency] private ZLevelShootingSystem _zLevelShooting = default!;
    [Dependency] private EntityQuery<CEZLevelViewerComponent> _zLevelViewerQuery = default!;
    [Dependency] private EntityQuery<PneumaticCannonComponent> _pneumaticQuery = default!;

    private EntityCoordinates GetZLevelAimCoordinates(
        EntityUid user,
        EntityUid gun,
        MapCoordinates cursor,
        out MapCoordinates target)
    {
        target = cursor;
        if (_pneumaticQuery.TryComp(gun, out var cannon) && cannon.ThrowItems)
            return TransformSystem.ToCoordinates(user, cursor);

        var userXform = Transform(user);
        if (userXform.MapUid is not { } sourceMap || userXform.MapID != cursor.MapId)
            return TransformSystem.ToCoordinates(user, cursor);

        var from = TransformSystem.GetMapCoordinates(userXform);
        var levelOffset = (-_eyeManager.CurrentEye.Rotation).ToWorldVec() * CESharedZLevelsSystem.ZLevelOffset;

        if (_zLevelViewerQuery.TryComp(user, out var viewer) &&
            viewer.LookUp &&
            _zLevels.GetVisibleZLevelsAbove(user, sourceMap) > 0 &&
            _zLevels.TryMapOffset(sourceMap, 1, out _, out var upperMap))
        {
            var upperTarget = new MapCoordinates(cursor.Position + levelOffset, upperMap.MapId);
            if (_zLevelShooting.TryGetShotPlan(user, from, upperTarget, out _))
                target = upperTarget;
        }

        if (target.MapId == cursor.MapId &&
            _zLevelShooting.IsEmptyAt(cursor) &&
            _zLevels.TryMapOffset(sourceMap, -1, out _, out var lowerMap))
        {
            var lowerTarget = new MapCoordinates(cursor.Position - levelOffset, lowerMap.MapId);
            if (_zLevelShooting.TryGetShotPlan(user, from, lowerTarget, out _))
                target = lowerTarget;
        }

        if (target.MapId == cursor.MapId)
            return TransformSystem.ToCoordinates(user, cursor);

        // Соседний этаж имеет собственную систему координат; сохраняем привязку к его гриду.
        if (MapManager.TryFindGridAt(target.MapId, target.Position, out var grid, out _))
            return TransformSystem.ToCoordinates(grid, target);

        return TransformSystem.ToCoordinates(target);
    }
}

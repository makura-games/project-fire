using Content.Server._Scp.ZLevels;
using Content.Shared._Scp.ZLevels;
using Robust.Shared.Map;

namespace Content.Server.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{
    // Проверка выбранного этажа и подключение маршрутов снарядов на сервере.
    [Dependency] private ZLevelShootingSystem _zLevelShooting = default!;
    [Dependency] private ZLevelProjectileSystem _zLevelProjectiles = default!;

    private MapCoordinates ValidateZLevelAim(EntityUid? user, MapCoordinates from, MapCoordinates to)
    {
        if (from.MapId == to.MapId || user is { } shooter &&
            _zLevelShooting.TryGetShotPlan(shooter, from, to, out _))
        {
            return to;
        }

        return new MapCoordinates(to.Position, from.MapId);
    }
}

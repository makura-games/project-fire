using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared.Light.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared.Weather;

public abstract partial class SharedWeatherSystem
{
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;

    private bool HasZLevelRoofAbove(Entity<MapGridComponent, RoofComponent?> grid, TileRef tile)
    {
        if (grid.Comp2 is { } roof &&
            (HasComp<CEZMapComponent>(grid) || HasComp<CEZGridComponent>(grid)) &&
            _roof.IsWeatherOccluding((grid.Owner, grid.Comp1, roof), tile.GridIndices))
        {
            return true;
        }

        if (Transform(grid).MapUid is not { } mapUid)
            return false;

        var position = _mapSystem.GridTileToWorld(grid, grid.Comp1, tile.GridIndices).Position;
        return _zLevels.HasTileAbove(position, (mapUid, null));
    }
}

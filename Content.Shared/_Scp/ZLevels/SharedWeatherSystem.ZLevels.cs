using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared.Weather;

public abstract partial class SharedWeatherSystem
{
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;

    private bool HasZLevelRoofAbove(Entity<MapGridComponent> grid, TileRef tile)
    {
        if (Transform(grid).MapUid is not { } mapUid)
            return false;

        var position = _mapSystem.GridTileToWorld(grid, grid.Comp, tile.GridIndices).Position;
        return _zLevels.HasTileAbove(position, (mapUid, null));
    }
}

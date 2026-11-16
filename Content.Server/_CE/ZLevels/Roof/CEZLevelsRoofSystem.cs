/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared._CE.ZLevels.Roof;

namespace Content.Server._CE.ZLevels.Roof;

public sealed partial class CEZLevelsRoofSystem : CESharedZLevelsRoofSystem
{
    private readonly HashSet<Vector2i> _lightBlockingTiles = new();
    private readonly HashSet<Vector2i> _weatherBlockingTiles = new();

    public override void Initialize()
    {
        base.Initialize();

        InitializeGridsSubscriptions();
        InitializeMapsSubscriptions();

        InitMaps();
        InitGrids();
    }
}

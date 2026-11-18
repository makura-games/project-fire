using Robust.Client.Graphics;
using Robust.Client.Placement;
using Robust.Shared.Map;

namespace Content.Client.Viewport;

public sealed partial class ScalingViewport
{
    [Dependency] private IPlacementManager _placement = default!;

    private void RenderZLevelViewport(IClydeViewport viewport)
    {
        MapId? placementMap = null;
        if (_placement.CurrentMode is { } mode &&
            _xformQuery is { } xformQuery &&
            xformQuery.TryComp(mode.MouseCoords.EntityId, out var xform))
        {
            placementMap = xform.MapID;
        }

        RenderZLevelViewport(viewport, _placement, placementMap);
    }

    internal static void RenderZLevelViewport(
        IClydeViewport viewport,
        IPlacementManager placement,
        MapId? placementMap)
    {
        var mode = placement.CurrentMode;
        if (mode == null ||
            (viewport.Eye is { } eye && eye is not ZEye && placementMap == eye.Position.MapId))
        {
            viewport.Render();
            return;
        }

        // Режим размещения хранит координаты основной карты и не должен рисоваться на соседних этажах.
        placement.CurrentMode = null;
        try
        {
            viewport.Render();
        }
        finally
        {
            placement.CurrentMode = mode;
        }
    }
}

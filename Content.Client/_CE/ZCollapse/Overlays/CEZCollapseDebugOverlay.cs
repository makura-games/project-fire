/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using Content.Client._CE.ZCollapse;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._CE.ZCollapse.Overlays;

/// <summary>
/// Renders the ZCollapse stability snapshot cached by <see cref="CEZCollapseClientSystem"/>: a
/// white(stable)-to-red(0-1 durability) filled square per tile in world space, plus the numeric
/// stability value in screen space. Truly-empty tiles are never present in the snapshot at all (the
/// server only sends entries for tiles that physically exist), so every entry received here gets
/// drawn — a 0 means the tile exists but should collapse.
/// </summary>
public sealed partial class CEZCollapseDebugOverlay : Overlay
{
    /// <summary>Stability value that renders fully white — fixed so colors stay comparable across grids/frames.</summary>
    private const int WhiteCap = 20;

    /// <summary>
    /// Where stability=1 sits on the red-to-white gradient. A tile at 0 is about to collapse; a tile
    /// at 1 isn't, even though numerically adjacent — so 0 alone renders pure red, and 1 already jumps
    /// most of the way to white, making that boundary the one that visually pops instead of a smooth
    /// gradient that reads 0 and 1 as nearly the same color.
    /// </summary>
    private const float AliveFloorT = 0.6f;

    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IResourceCache _cache = default!;

    private readonly CEZCollapseClientSystem _collapse;
    private readonly SharedTransformSystem _transform;
    private readonly SharedMapSystem _mapSystem;

    private readonly Font _font;

    public override OverlaySpace Space => OverlaySpace.WorldSpace | OverlaySpace.ScreenSpace;

    public CEZCollapseDebugOverlay()
    {
        IoCManager.InjectDependencies(this);

        _collapse = _entityManager.System<CEZCollapseClientSystem>();
        _transform = _entityManager.System<SharedTransformSystem>();
        _mapSystem = _entityManager.System<SharedMapSystem>();

        _font = _cache.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 8);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        switch (args.Space)
        {
            case OverlaySpace.WorldSpace:
                DrawFill(args);
                break;
            case OverlaySpace.ScreenSpace:
                DrawText(args);
                break;
        }
    }

    private void DrawFill(in OverlayDrawArgs args)
    {
        if (_collapse.Grids == null)
            return;

        var handle = args.WorldHandle;

        foreach (var (netGrid, tiles) in _collapse.Grids)
        {
            var gridUid = _entityManager.GetEntity(netGrid);
            if (!_entityManager.TryGetComponent<TransformComponent>(gridUid, out var gridXform) || gridXform.MapID != args.MapId)
                continue;

            handle.SetTransform(_transform.GetWorldMatrix(gridUid));

            foreach (var (tile, stability) in tiles)
            {
                var t = stability <= 0
                    ? 0f
                    : AliveFloorT + (1f - AliveFloorT) * Math.Clamp((stability - 1f) / (WhiteCap - 1f), 0f, 1f);
                var color = Color.InterpolateBetween(Color.Red, Color.White, t).WithAlpha(0.35f);
                handle.DrawRect(Box2.FromDimensions(new Vector2(tile.X, tile.Y), new Vector2(1, 1)), color);
            }
        }

        handle.SetTransform(Matrix3x2.Identity);
    }

    private void DrawText(in OverlayDrawArgs args)
    {
        if (_collapse.Grids == null || args.ViewportControl == null)
            return;

        var handle = args.ScreenHandle;

        foreach (var (netGrid, tiles) in _collapse.Grids)
        {
            var gridUid = _entityManager.GetEntity(netGrid);
            if (!_entityManager.TryGetComponent<MapGridComponent>(gridUid, out var grid) ||
                !_entityManager.TryGetComponent<TransformComponent>(gridUid, out var gridXform) ||
                gridXform.MapID != args.MapId)
            {
                continue;
            }

            foreach (var (tile, stability) in tiles)
            {
                var worldPos = _mapSystem.GridTileToWorldPos(gridUid, grid, tile);
                var screenPos = args.ViewportControl.WorldToScreen(worldPos);
                handle.DrawString(_font, screenPos, stability.ToString(), Color.Black);
            }
        }
    }
}
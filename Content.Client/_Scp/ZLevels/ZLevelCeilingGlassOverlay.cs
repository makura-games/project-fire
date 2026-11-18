using System.Numerics;
using Content.Client._CE.ZLevels.Core;
using Content.Client.Viewport;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.Maps;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._Scp.ZLevels;

internal sealed partial class ZLevelCeilingGlassOverlay : Overlay
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;

    private static readonly Color CeilingTint = Color.White.WithAlpha(0.45f);

    private readonly IEntityManager _entities;
    private readonly CEClientZLevelsSystem _zLevels;
    private readonly SharedMapSystem _map;
    private readonly SharedTransformSystem _transform;
    private readonly EntityLookupSystem _lookup;
    private List<Entity<MapGridComponent>> _grids = new();
    private EntityUid _upperMap;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public ZLevelCeilingGlassOverlay(IEntityManager entities, CEClientZLevelsSystem zLevels)
    {
        IoCManager.InjectDependencies(this);
        _entities = entities;
        _zLevels = zLevels;
        _map = entities.System<SharedMapSystem>();
        _transform = entities.System<SharedTransformSystem>();
        _lookup = entities.System<EntityLookupSystem>();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_player.LocalEntity is not { } player ||
            !_entities.TryGetComponent(player, out EyeComponent? eye) ||
            args.Viewport.Eye != eye.Eye ||
            !_entities.TryGetComponent(player, out CEZLevelViewerComponent? viewer) ||
            !_zLevels.TryMapUp(args.MapUid, out var upperMap))
        {
            return false;
        }

        // При взгляде вверх стекло уже рисуется в отдельном кадре верхнего этажа.
        if (viewer.LookUp && _zLevels.GetVisibleZLevelsAbove(player, args.MapUid) > 0)
            return false;

        _upperMap = upperMap;
        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye is not { } eye)
            return;

        var offset = ScalingViewport.GetZLevelViewOffset(eye.Rotation);
        var bounds = args.WorldBounds;
        bounds.Box = bounds.Box.Translated(offset);
        bounds.Origin += offset;

        _grids.Clear();
        _map.FindGridsIntersecting(_upperMap, bounds, ref _grids, approx: true);

        var handle = args.WorldHandle;
        try
        {
            foreach (var grid in _grids)
            {
                var matrix = _transform.GetWorldMatrix(grid.Owner);
                matrix.Translation -= offset;
                handle.SetTransform(matrix);

                var tiles = _map.GetTilesIntersecting(grid.Owner, grid.Comp, bounds);
                while (tiles.MoveNext(out var tileRef))
                {
                    if (_tiles[tileRef.Tile.TypeId] is not ContentTileDefinition { Transparent: true, Sprite: { } sprite } ||
                        !_resources.TryGetResource<TextureResource>(sprite, out var texture))
                    {
                        continue;
                    }

                    var tileSize = EyeManager.PixelsPerMeter;
                    var variant = tileRef.Tile.Variant < texture.Texture.Width / tileSize ? tileRef.Tile.Variant : 0;
                    var region = UIBox2.FromDimensions(new Vector2(variant * tileSize, 0), new Vector2(tileSize));
                    var localBounds = _lookup.GetLocalBounds(tileRef, grid.Comp.TileSize);
                    handle.DrawTextureRectRegion(texture.Texture, localBounds, CeilingTint, region);
                }
            }
        }
        finally
        {
            handle.SetTransform(Matrix3x2.Identity);
        }
    }
}

/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using Content.Client.Pinpointer.UI;
using Content.Client.Resources;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared.Pinpointer;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Collections;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Client._CE.ZLevels.NavMap;

/// <summary>
/// Reusable control that draws every z-level map of a z-network as a vertically stacked set of
/// nav-map contours, to imitate height. Built to the same "generic base + subclass hooks"
/// standard as <see cref="NavMapControl"/>: override <see cref="UpdateNavMap"/> to decode extra
/// per-level data, subscribe <see cref="PostLevelDrawingAction"/> to draw in a level's transformed
/// space, and use <see cref="LevelToScreen"/> to project map coordinates for a given depth.
/// </summary>
[UsedImplicitly, Virtual]
public partial class CEZLevelsNavMapControl : MapGridControl
{
    [Dependency] private IResourceCache _cache = default!;

    private readonly SharedTransformSystem _transform;
    private readonly CESharedZLevelsSystem _zLevels;

    // --- Public / overridable surface (mirrors NavMapControl) ---

    /// <summary>The console/device that owns this control. Subclasses read extra components off it.</summary>
    public EntityUid? Owner;

    private EntityUid? _mapUid;

    /// <summary>Any map entity that belongs to the target z-network (usually the console's own map).</summary>
    public EntityUid? MapUid
    {
        get => _mapUid;
        set
        {
            if (_mapUid == value)
                return;

            _mapUid = value;
            _activeDepthInitialised = false;
        }
    }

    /// <summary>The currently selected z-level. Levels above are hidden by default, levels below are dimmed.</summary>
    public int ActiveDepth { get; private set; }

    public event Action<int>? ActiveDepthChanged;

    /// <summary>Invoked once per drawn level, after its contour, in that level's transformed screen space.</summary>
    public event Action<DrawingHandleScreen, CEZLevelRender>? PostLevelDrawingAction;

    /// <summary>Invoked once after every level and overlay has been drawn.</summary>
    public event Action<DrawingHandleScreen>? PostDrawingAction;

    /// <summary>Fired on click-select of a tracked entity (or null on right-click / miss).</summary>
    public event Action<NetEntity?>? TrackedEntitySelectedAction;

    /// <summary>Textured markers; the control resolves each blip's depth from its coordinates' map.</summary>
    public Dictionary<NetEntity, NavMapBlip> TrackedEntities = new();

    /// <summary>Simple blinking dots (legacy overlay, same semantics as NavMapControl).</summary>
    public Dictionary<EntityCoordinates, (bool Visible, Color Color)> TrackedCoordinates = new();

    public IReadOnlyDictionary<int, CEZLevelRender> Levels => _levels;

    // --- Theming / tunables (no magic numbers inline) ---

    public Color WallColor = new(120, 178, 224);
    public Color TileColor = new(58, 100, 128);
    public Color FloorEdgeColor = new(72, 126, 166);

    protected Color BackgroundColor = Color.Black;

    protected float LevelHeightOffset = 1.0f;
    protected Vector2 LevelOffsetDir = new(0f, 1f);
    protected float DepthDimStep = 0.35f;
    protected float MinLevelBrightness = 0.45f;
    protected float RotateSpeed = 1.2f;
    protected int MaxLevelsBelow = CESharedZLevelsSystem.MaxZLevelsBelowRendering;
    protected int MaxLevelsAbove = 0;
    protected float UpdateTime = 1.0f;
    protected float MinDragDistance = 5f;
    protected float MaxSelectableDistance = 32f;

    // Dragging is rotation-aware and handled in this class, not by the base MapGridControl.
    protected override bool Draggable => false;
    private bool _dragging;

    // --- Internals ---

    private readonly Dictionary<int, CEZLevelRender> _levels = new();
    private readonly List<int> _sortedDepths = new();
    private readonly Dictionary<Color, Color> _sRgbLookup = new();

    private PhysicsComponent? _activeGridPhysics;
    private bool _activeDepthInitialised;
    private float _updateTimer;

    private Angle _rotation = Angle.Zero;
    private bool _rotatingLeft;
    private bool _rotatingRight;
    private bool _recenterRotation;

    private readonly TextureButton _levelUp = new()
    {
        HorizontalAlignment = HAlignment.Center,
        Margin = new Thickness(2f),
    };

    private readonly TextureButton _levelDown = new()
    {
        HorizontalAlignment = HAlignment.Center,
        Margin = new Thickness(2f),
    };

    private readonly TextureButton _rotateLeft = new()
    {
        Margin = new Thickness(2f),
    };

    private readonly TextureButton _rotateRight = new()
    {
        Margin = new Thickness(2f),
    };

    private readonly Button _recenter = new()
    {
        Text = Loc.GetString("navmap-recenter"),
        Margin = new Thickness(2f),
        Disabled = true,
    };

    public CEZLevelsNavMapControl() : base(8f, 128f, 48f)
    {
        IoCManager.InjectDependencies(this);

        _transform = EntManager.System<SharedTransformSystem>();
        _zLevels = EntManager.System<CESharedZLevelsSystem>();

        HorizontalExpand = true;
        VerticalExpand = true;
        RectClipContent = true;

        _levelUp.TextureNormal = _cache.GetTexture("/Textures/Interface/NavMap/beveled_arrow_north.png");
        _levelDown.TextureNormal = _cache.GetTexture("/Textures/Interface/NavMap/beveled_arrow_south.png");
        _rotateLeft.TextureNormal = _cache.GetTexture("/Textures/Interface/NavMap/beveled_arrow_west.png");
        _rotateRight.TextureNormal = _cache.GetTexture("/Textures/Interface/NavMap/beveled_arrow_east.png");

        var panel = new PanelContainer
        {
            StyleClasses = { StyleClass.PanelDark },
            Children =
            {
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    Margin = new Thickness(4f),
                    Children =
                    {
                        _levelUp,
                        _levelDown,
                        new BoxContainer
                        {
                            Orientation = BoxContainer.LayoutOrientation.Horizontal,
                            HorizontalAlignment = HAlignment.Center,
                            Children = { _rotateLeft, _rotateRight },
                        },
                        _recenter,
                    },
                },
            },
        };

        AddChild(panel);
        SetAnchorAndMarginPreset(panel, LayoutPreset.TopRight, margin: 8);

        _levelUp.OnPressed += _ => SetActiveDepth(ActiveDepth + 1);
        _levelDown.OnPressed += _ => SetActiveDepth(ActiveDepth - 1);

        _rotateLeft.OnButtonDown += _ => _rotatingLeft = true;
        _rotateLeft.OnButtonUp += _ => _rotatingLeft = false;
        _rotateRight.OnButtonDown += _ => _rotatingRight = true;
        _rotateRight.OnButtonUp += _ => _rotatingRight = false;

        _recenter.OnPressed += _ =>
        {
            Recentering = true;
            _recenterRotation = _rotation != Angle.Zero;
        };

        ForceNavMapUpdate();
    }

    public void ForceNavMapUpdate()
    {
        _updateTimer = 0f;
        UpdateNavMap();
    }

    public void SetActiveDepth(int depth)
    {
        if (!TryGetNetwork(out var network))
            return;

        depth = Math.Clamp(depth, network.Comp.SortedMin, network.Comp.SortedMax);

        if (depth != ActiveDepth)
        {
            ActiveDepth = depth;
            ActiveDepthChanged?.Invoke(ActiveDepth);
            ForceNavMapUpdate();
        }

        UpdateControls(network);
    }

    private void UpdateControls(Entity<CEZMapNetworkComponent> network)
    {
        _levelUp.Disabled = ActiveDepth >= network.Comp.SortedMax;
        _levelDown.Disabled = ActiveDepth <= network.Comp.SortedMin;
    }

    private bool TryGetNetwork(out Entity<CEZMapNetworkComponent> network)
    {
        network = default;
        return _mapUid is { } m && _zLevels.TryGetMapNetwork(m, out network);
    }

    private bool TryGetMapAtDepth(Entity<CEZMapNetworkComponent> network, int depth, out EntityUid mapEnt)
    {
        mapEnt = default;
        var comp = network.Comp;
        var idx = depth - comp.SortedMin;

        if (idx >= 0 && idx < comp.SortedZLevels.Count)
        {
            mapEnt = comp.SortedZLevels[idx];
            return mapEnt.IsValid();
        }

        if (comp.ZLevels.TryGetValue(depth, out var maybe) && maybe is { } valid)
        {
            mapEnt = valid;
            return true;
        }

        return false;
    }

    /// <summary>Whether a given depth should be built and drawn. Override to e.g. also show levels above.</summary>
    protected virtual bool ShouldDrawLevel(int depth)
    {
        return depth >= ActiveDepth - MaxLevelsBelow && depth <= ActiveDepth + MaxLevelsAbove;
    }

    /// <summary>Colour multiplier for a level. Active level is full-bright; lower levels fade (but never below <see cref="MinLevelBrightness"/>).</summary>
    protected virtual Color GetLevelModulate(int depth)
    {
        if (depth >= ActiveDepth)
            return Color.White;

        var f = MathF.Max(MathF.Pow(1f - DepthDimStep, ActiveDepth - depth), MinLevelBrightness);
        return new Color(f, f, f, 1f);
    }

    protected virtual void UpdateNavMap()
    {
        _levels.Clear();
        _activeGridPhysics = null;

        if (!TryGetNetwork(out var network))
            return;

        if (!_activeDepthInitialised)
        {
            if (EntManager.TryGetComponent<CEZMapComponent>(_mapUid, out var ownZMap))
                ActiveDepth = ownZMap.Depth;

            ActiveDepth = Math.Clamp(ActiveDepth, network.Comp.SortedMin, network.Comp.SortedMax);
            _activeDepthInitialised = true;
        }

        UpdateControls(network);

        for (var depth = ActiveDepth - MaxLevelsBelow; depth <= ActiveDepth + MaxLevelsAbove; depth++)
        {
            if (!ShouldDrawLevel(depth) || !TryGetMapAtDepth(network, depth, out var mapEnt))
                continue;

            if (!EntManager.TryGetComponent<NavMapComponent>(mapEnt, out var nav) ||
                !EntManager.TryGetComponent<MapGridComponent>(mapEnt, out var grid))
                continue;

            var render = new CEZLevelRender { Depth = depth, MapUid = mapEnt, Grid = grid };
            CENavMapGeometry.Build(nav, grid, render.Geometry);
            _levels[depth] = render;

            if (depth == ActiveDepth)
                EntManager.TryGetComponent(mapEnt, out _activeGridPhysics);
        }
    }

    /// <summary>
    /// Projects a point in a z-level grid's local (map) coordinates to a screen position,
    /// applying pan, zoom, projection rotation, and this level's vertical height offset.
    /// The vertical offset stays screen-vertical regardless of rotation, so spinning the
    /// projection reads as rotating a solid 3D stack.
    /// </summary>
    public Vector2 LevelToScreen(int depth, Vector2 mapPos)
    {
        var off = Offset;
        if (_activeGridPhysics != null)
            off += _activeGridPhysics.LocalCenter;

        var local = new Vector2(mapPos.X - off.X, -(mapPos.Y - off.Y));

        if (_rotation != Angle.Zero)
            local = _rotation.RotateVec(local);

        return ScalePosition(local) + LevelOffsetDir * (LevelHeightOffset * MinimapScale * (ActiveDepth - depth));
    }

    /// <summary>
    /// Switches the active level to whatever z-level <paramref name="coordinates"/> sits on and pans it to view centre.
    /// </summary>
    public void CenterToCoordinates(EntityCoordinates coordinates)
    {
        var mapUid = _transform.GetMap(coordinates);
        if (mapUid == null || !EntManager.TryGetComponent<CEZMapComponent>(mapUid, out var zMap))
            return;

        SetActiveDepth(zMap.Depth);

        if (!_levels.TryGetValue(zMap.Depth, out var render))
            return;

        var mapPos = _transform.ToMapCoordinates(coordinates);
        if (mapPos.MapId == MapId.Nullspace)
            return;

        var gridXform = EntManager.GetComponent<TransformComponent>(render.MapUid);
        var local = Vector2.Transform(mapPos.Position, _transform.GetInvWorldMatrix(gridXform));

        Offset = local - (_activeGridPhysics?.LocalCenter ?? Vector2.Zero);
        Recentering = false;
    }

    private Color CachedSrgb(Color color)
    {
        if (!_sRgbLookup.TryGetValue(color, out var srgb))
        {
            srgb = Color.ToSrgb(color);
            _sRgbLookup[color] = srgb;
        }

        return srgb;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_rotatingLeft != _rotatingRight)
        {
            _recenterRotation = false;
            _rotation = (_rotation + new Angle((_rotatingRight ? 1f : -1f) * RotateSpeed * args.DeltaSeconds))
                .Reduced();
        }
        else if (_recenterRotation)
        {
            var remaining = _rotation.Reduced().Theta;
            var step = RotateSpeed * 1.75f * args.DeltaSeconds;

            if (Math.Abs(remaining) <= step)
            {
                _rotation = Angle.Zero;
                _recenterRotation = false;
            }
            else
            {
                _rotation = new Angle(remaining - Math.Sign(remaining) * step);
            }
        }

        _updateTimer += args.DeltaSeconds;
        if (_updateTimer < UpdateTime)
            return;

        _updateTimer -= UpdateTime;
        UpdateNavMap();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        handle.DrawRect(PixelSizeBox, BackgroundColor);

        _recenter.Disabled = DrawRecenter() && _rotation == Angle.Zero;

        if (_levels.Count == 0)
            return;

        _sortedDepths.Clear();
        _sortedDepths.AddRange(_levels.Keys);
        _sortedDepths.Sort();

        var quad = new Vector2[4];

        // Ascending order: lower levels first so the active level paints on top.
        foreach (var depth in _sortedDepths)
        {
            var render = _levels[depth];
            var mod = GetLevelModulate(depth);
            var wallColor = CachedSrgb(WallColor * mod);
            var tileColor = TileColor;
            var edgeColor = CachedSrgb(FloorEdgeColor * mod);

            foreach (var (min, max) in render.Geometry.FloorRects)
            {
                quad[0] = LevelToScreen(depth, min);
                quad[1] = LevelToScreen(depth, new Vector2(max.X, min.Y));
                quad[2] = LevelToScreen(depth, max);
                quad[3] = LevelToScreen(depth, new Vector2(min.X, max.Y));

                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, quad, tileColor);
            }

            if (render.Geometry.FloorPerimeter.Count > 0)
            {
                var edges = new ValueList<Vector2>(render.Geometry.FloorPerimeter.Count * 2);
                foreach (var (a, b) in render.Geometry.FloorPerimeter)
                {
                    edges.Add(LevelToScreen(depth, a));
                    edges.Add(LevelToScreen(depth, b));
                }

                handle.DrawPrimitives(DrawPrimitiveTopology.LineList, edges.Span, edgeColor);
            }

            if (render.Geometry.WallLines.Count > 0)
            {
                var lines = new ValueList<Vector2>(render.Geometry.WallLines.Count * 2);
                foreach (var (a, b) in render.Geometry.WallLines)
                {
                    lines.Add(LevelToScreen(depth, new Vector2(a.X, -a.Y)));
                    lines.Add(LevelToScreen(depth, new Vector2(b.X, -b.Y)));
                }

                handle.DrawPrimitives(DrawPrimitiveTopology.LineList, lines.Span, wallColor);
            }

            if (render.Geometry.WallRects.Count > 0)
            {
                var rects = new ValueList<Vector2>(render.Geometry.WallRects.Count * 8);
                foreach (var (lt, rb) in render.Geometry.WallRects)
                {
                    // Project all four corners so the rect stays correct under projection rotation.
                    var a = LevelToScreen(depth, new Vector2(lt.X, -lt.Y));
                    var b = LevelToScreen(depth, new Vector2(rb.X, -lt.Y));
                    var c = LevelToScreen(depth, new Vector2(rb.X, -rb.Y));
                    var d = LevelToScreen(depth, new Vector2(lt.X, -rb.Y));

                    rects.Add(a);
                    rects.Add(b);
                    rects.Add(b);
                    rects.Add(c);
                    rects.Add(c);
                    rects.Add(d);
                    rects.Add(d);
                    rects.Add(a);
                }

                handle.DrawPrimitives(DrawPrimitiveTopology.LineList, rects.Span, wallColor);
            }

            // Draw this level's blips within its own pass, so higher levels can occlude them.
            DrawTrackedOverlaysForLevel(handle, depth);

            PostLevelDrawingAction?.Invoke(handle, render);
        }

        PostDrawingAction?.Invoke(handle);
    }

    private void DrawTrackedOverlaysForLevel(DrawingHandleScreen handle, int depth)
    {
        if (TrackedCoordinates.Count == 0 && TrackedEntities.Count == 0)
            return;

        var blinkFrequency = 1f;
        var lit = Timing.RealTime.TotalSeconds % blinkFrequency > blinkFrequency / 2f;

        foreach (var (coord, value) in TrackedCoordinates)
        {
            if (!value.Visible || !lit)
                continue;

            if (!TryProjectCoordinates(coord, out var position, out var coordDepth) || coordDepth != depth)
                continue;

            handle.DrawCircle(position, MathF.Sqrt(MinimapScale) * 2f, value.Color);
        }

        foreach (var blip in TrackedEntities.Values)
        {
            if (blip.Blinks && !lit)
                continue;

            if (blip.Texture == null)
                continue;

            if (!TryProjectCoordinates(blip.Coordinates, out var position, out var blipDepth) || blipDepth != depth)
                continue;

            var size = new Vector2(blip.Texture.Width, blip.Texture.Height) * blip.Scale * 0.075f *
                       MathF.Sqrt(MinimapScale);
            handle.DrawTextureRect(blip.Texture, new UIBox2(position - size, position + size), blip.Color);
        }
    }

    /// <summary>Resolves an <see cref="EntityCoordinates"/> to a screen position on whatever z-level it sits on.</summary>
    private bool TryProjectCoordinates(EntityCoordinates coord, out Vector2 screen, out int depth)
    {
        screen = default;
        depth = 0;

        var mapUid = _transform.GetMap(coord);
        if (mapUid == null || !EntManager.TryGetComponent<CEZMapComponent>(mapUid, out var zMap))
            return false;

        depth = zMap.Depth;
        if (!_levels.TryGetValue(depth, out var render) || !ShouldDrawLevel(depth))
            return false;

        var mapPos = _transform.ToMapCoordinates(coord);
        if (mapPos.MapId == MapId.Nullspace)
            return false;

        var gridXform = EntManager.GetComponent<TransformComponent>(render.MapUid);
        var local = Vector2.Transform(mapPos.Position, _transform.GetInvWorldMatrix(gridXform));
        screen = LevelToScreen(depth, local);
        return true;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function == EngineKeyFunctions.Use)
        {
            _dragging = true;
            StartDragPosition = args.PointerLocation.Position;
        }
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        if (!_dragging || MinimapScale <= 0f)
            return;

        Recentering = false;

        // Undo the projection (rotation + zoom) so the content tracks the cursor 1:1 at any rotation.
        var v = (-_rotation).RotateVec(args.Relative / MinimapScale);
        Offset -= new Vector2(v.X, -v.Y);
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function == EngineKeyFunctions.Use)
            _dragging = false;

        if (args.Function == EngineKeyFunctions.UIClick)
        {
            if (TrackedEntitySelectedAction == null || TrackedEntities.Count == 0)
                return;

            if ((StartDragPosition - args.PointerLocation.Position).Length() > MinDragDistance)
                return;

            var clickPos = args.PointerLocation.Position - GlobalPixelPosition;

            var closest = NetEntity.Invalid;
            var closestDistance = float.PositiveInfinity;

            foreach (var (netEntity, blip) in TrackedEntities)
            {
                if (!blip.Selectable)
                    continue;

                if (!TryProjectCoordinates(blip.Coordinates, out var screen, out _))
                    continue;

                var distance = (screen - clickPos).Length();
                if (distance < closestDistance && distance <= MaxSelectableDistance)
                {
                    closest = netEntity;
                    closestDistance = distance;
                }
            }

            if (closest.IsValid())
                TrackedEntitySelectedAction.Invoke(closest);
        }
        else if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            TrackedEntitySelectedAction?.Invoke(null);
        }
    }
}

/// <summary>
/// Per-level bundle held by <see cref="CEZLevelsNavMapControl"/>.
/// </summary>
public sealed class CEZLevelRender
{
    public int Depth;
    public EntityUid MapUid;
    public MapGridComponent Grid = default!;
    public readonly CENavMapGeometryData Geometry = new();
}

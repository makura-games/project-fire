using System.Diagnostics.CodeAnalysis;
using Content.Client._Scp.Scp173.UI;
using Content.Client.Charges;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Actions;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Shared._Scp.SafeTime;
using Content.Shared._Scp.Scp173;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._Scp.Scp173;

// TODO: Единая система для управления виджетами вместе с SafeTimeSystem
public sealed partial class Scp173System : SharedScp173System
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IOverlayManager _overlayMan = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private ChargesSystem _charges = default!;

    private Scp173Overlay _overlay = default!;
    private Scp173UiWidget? _widget;

    private TimeSpan _nextReagentCheck;

    private EntityQuery<Scp173Component> _scp173Query;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Scp173Component, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<Scp173Component, ComponentShutdown>(OnShutdown);

        SubscribeLocalEvent<Scp173Component, LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<Scp173Component, LocalPlayerDetachedEvent>(OnPlayerDetached);

        _scp173Query = GetEntityQuery<Scp173Component>();

        var gameplayStateLoad = _ui.GetUIController<GameplayStateLoadController>();
        gameplayStateLoad.OnScreenLoad += EnsureWidgetExist;
        gameplayStateLoad.OnScreenUnload += RemoveWidget;

        _overlay = new(_transform, _ui.GetUIController<ActionUIController>(), _physics, _charges, this);
        _ui.OnScreenChanged += _ => RecreateWidget();
    }

    private void OnStartup(Entity<Scp173Component> ent, ref ComponentStartup args)
    {
        if (_player.LocalEntity != ent)
            return;

        EnsureWidgetExist();
        _overlayMan.AddOverlay(_overlay);
    }

    private void OnShutdown(Entity<Scp173Component> ent, ref ComponentShutdown args)
    {
        if (_player.LocalEntity != ent)
            return;

        RemoveWidget();
        _overlayMan.RemoveOverlay(_overlay);
    }

    private void OnPlayerAttached(Entity<Scp173Component> ent, ref LocalPlayerAttachedEvent args)
    {
        EnsureWidgetExist();
        _overlayMan.AddOverlay(_overlay);
    }

    private void OnPlayerDetached(Entity<Scp173Component> ent, ref LocalPlayerDetachedEvent args)
    {
        RemoveWidget();
        _overlayMan.RemoveOverlay(_overlay);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_widget == null)
            return;

        if (_timing.CurTime < _nextReagentCheck)
            return;

        _nextReagentCheck = _timing.CurTime + ReagentCheckInterval;

        if (!TryGetPlayerEntity(out var ent))
            return;

        if (!IsContained(ent.Value))
        {
            _widget.Visible = false;
            return;
        }

        SetReagentData(ent.Value, _widget);
        SetSafeTimeData(ent.Value.Owner, _widget);

        _widget.Visible = true;
    }

    private bool TryGetPlayerEntity([NotNullWhen(true)] out Entity<Scp173Component>? ent)
    {
        ent = null;

        if (!_scp173Query.TryComp(_player.LocalEntity, out var scp173))
            return false;

        ent = (_player.LocalEntity.Value, scp173);

        return true;
    }

    private void SetReagentData(Entity<Scp173Component> ent, Scp173UiWidget widget)
    {
        var current = ent.Comp.ReagentVolumeAround.Int();
        widget.ReagentBar.UpdateInfo(current, Scp173Component.MinTotalSolutionVolume, Scp173Component.ExtraMinTotalSolutionVolume);
    }

    private void SetSafeTimeData(Entity<SafeTimeComponent?> ent, Scp173UiWidget widget)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        widget.SafeTime.UpdateSafeTimeInfo(ent.Comp.TimeEnd, ent.Comp.Time);
    }

    private void EnsureWidgetExist()
    {
        if (_ui.ActiveScreen == null)
            return;

        if (_widget != null)
            return;

        if (!TryGetPlayerEntity(out _))
            return;

        var nameScope = _ui.ActiveScreen?.FindNameScope();
        var layoutContainer = nameScope?.Find("ViewportContainer");

        if (layoutContainer == null)
            return;

        _widget = new ();

        var layout = _ui.ActiveScreen is SeparatedChatGameScreen
            ? LayoutContainer.LayoutPreset.TopRight
            : LayoutContainer.LayoutPreset.CenterTop;

        LayoutContainer.SetAnchorAndMarginPreset(_widget, layout, margin: 50);
        layoutContainer.AddChild(_widget);

        _widget.Visible = false;
    }

    private void RemoveWidget()
    {
        if (_widget == null)
            return;

        _widget.Parent?.RemoveChild(_widget);
        _widget.RemoveAllChildren();
        _widget = null;
    }

    private void RecreateWidget()
    {
        RemoveWidget();
        EnsureWidgetExist();
    }

    #region Overlay API

    /// <summary>
    /// Публичная обёртка над <see cref="SharedScp173System.IsImpassableObstacle"/> для использования в оверлее.
    /// </summary>
    public bool CheckImpassableObstacle(EntityUid entity) => IsImpassableObstacle(entity);

    /// <summary>
    /// Публичная обёртка над <see cref="SharedScp173System.ClampTargetToRange"/> для использования в оверлее.
    /// </summary>
    public void ClampTarget(MapCoordinates performerCoords, ref MapCoordinates targetCoords, float maxRange)
        => ClampTargetToRange(performerCoords, ref targetCoords, maxRange);

    #endregion
}

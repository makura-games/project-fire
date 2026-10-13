using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Gameplay;
using Content.Shared._Scp.SafeTime;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._Scp.SafeTime;

// TODO: Единая система для управления виджетами вместе с Scp173System
public sealed partial class SafeTimeSystem : SharedSafeTimeSystem
{
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IGameTiming _timing = default!;

    private SafeTimeWidget? _widget;

    private EntityQuery<SafeTimeComponent> _safeTimeQuery;

    private TimeSpan _nextCheck;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2f);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SafeTimeComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<SafeTimeComponent, ComponentShutdown>(OnShutdown);

        SubscribeLocalEvent<SafeTimeComponent, LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<SafeTimeComponent, LocalPlayerDetachedEvent>(OnPlayerDetached);

        _safeTimeQuery = GetEntityQuery<SafeTimeComponent>();

        var gameplayStateLoad = _ui.GetUIController<GameplayStateLoadController>();
        gameplayStateLoad.OnScreenLoad += EnsureWidgetExist;
        gameplayStateLoad.OnScreenUnload += RemoveWidget;

        _ui.OnScreenChanged += _ => RecreateWidget();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_widget == null)
            return;

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;

        if (!_safeTimeQuery.TryComp(_player.LocalEntity, out var safeTime))
            return;

        _widget.ProgressBar.UpdateSafeTimeInfo(safeTime.TimeEnd, safeTime.Time);
        _widget.Visible = true;
    }

    #region Event handlers

    private void OnStartup(Entity<SafeTimeComponent> ent, ref ComponentStartup args)
    {
        if (_player.LocalEntity != ent)
            return;

        EnsureWidgetExist();
    }

    private void OnShutdown(Entity<SafeTimeComponent> ent, ref ComponentShutdown args)
    {
        if (_player.LocalEntity != ent)
            return;

        RemoveWidget();
    }

    private void OnPlayerAttached(Entity<SafeTimeComponent> ent, ref LocalPlayerAttachedEvent args)
    {
        EnsureWidgetExist();
    }

    private void OnPlayerDetached(Entity<SafeTimeComponent> ent, ref LocalPlayerDetachedEvent args)
    {
        RemoveWidget();
    }

    #endregion

    #region Helpers

    private void EnsureWidgetExist()
    {
        if (_ui.ActiveScreen == null)
            return;

        if (_widget != null)
            return;

        if (!_safeTimeQuery.TryComp(_player.LocalEntity, out var safeTime))
            return;

        if (!safeTime.AddWidget)
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

    #endregion
}

using Content.Client._Scp.Scp096.Ui;
using Content.Client.UserInterface.Systems.Gameplay;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Scp.Scp096;

public sealed partial class Scp096System
{
    [Dependency] private IUserInterfaceManager _ui = default!;

    private Scp096UiWidget? _widget;

    private void InitializeWidget()
    {
        var gameplayStateLoad = _ui.GetUIController<GameplayStateLoadController>();
        gameplayStateLoad.OnScreenLoad += EnsureWidgetExist;
        gameplayStateLoad.OnScreenUnload += RemoveWidget;
    }

    private void ShutdownWidget()
    {
        var gameplayStateLoad = _ui.GetUIController<GameplayStateLoadController>();
        gameplayStateLoad.OnScreenLoad -= EnsureWidgetExist;
        gameplayStateLoad.OnScreenUnload -= RemoveWidget;
    }

    private void EnsureWidgetExist()
    {
        if (_widget != null)
            return;

        if (_ui.ActiveScreen == null)
            return;

        var nameScope = _ui.ActiveScreen?.FindNameScope();
        var layoutContainer = nameScope?.Find("ViewportContainer");

        if (layoutContainer == null)
            return;

        _widget = new Scp096UiWidget();
        LayoutContainer.SetAnchorAndMarginPreset(_widget, LayoutContainer.LayoutPreset.CenterTop, margin: 50);

        layoutContainer.AddChild(_widget);
    }

    private void RemoveWidget()
    {
        if (_widget == null)
            return;

        _widget.Parent?.RemoveChild(_widget);
        _widget.RemoveAllChildren();
        _widget = null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_widget == null)
            return;

        if (!RageQuery.TryComp(_player.LocalEntity, out var rage) || !rage.RageStartTime.HasValue)
        {
            _widget.Visible = false;
            return;
        }

        if (!Scp096Query.TryComp(_player.LocalEntity, out var scp096))
        {
            _widget.Visible = false;
            return;
        }

        _widget.Visible = true;

        var elapsedTime = _timing.CurTime - rage.RageStartTime;
        var remainingTime = rage.RageDuration - elapsedTime.Value;

        _widget.SetData(remainingTime.TotalSeconds, scp096.TargetsCount);
    }
}

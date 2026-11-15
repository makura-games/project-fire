using Content.Client._Scp.UI.Compatibility;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

// ReSharper disable once CheckNamespace
namespace Content.Client.ContextMenu.UI;

public partial class ContextMenuElement
{
    private Control? _parentButton;

    protected override void EnteredTree()
    {
        base.EnteredTree();
        InitializeHoverHandling();
    }

    /// <summary>
    /// Вызывается при добавлении в дерево для настройки обработки наведения.
    /// </summary>
    private void InitializeHoverHandling()
    {
        _parentButton = this;
        SubscribeToButtonEvents();
        UpdateTextColor();
    }

    /// <summary>
    /// Вызывается при удалении из дерева для очистки обработки наведения.
    /// </summary>
    private void CleanupHoverHandling()
    {
        UnsubscribeFromButtonEvents();
    }

    private void SubscribeToButtonEvents()
    {
        if (_parentButton == null)
            return;

        _parentButton.OnMouseEntered += OnParentMouseEntered;
        _parentButton.OnMouseExited += OnParentMouseExited;
    }

    private void UnsubscribeFromButtonEvents()
    {
        if (_parentButton == null)
            return;

        _parentButton.OnMouseEntered -= OnParentMouseEntered;
        _parentButton.OnMouseExited -= OnParentMouseExited;
        _parentButton = null;
    }

    private void OnParentMouseEntered(GUIMouseHoverEventArgs args)
    {
        if (_parentButton is not BaseButton button)
            return;

        HoverColorHelper.SetContentColor(this, HoverColorHelper.GetColorForMouseEnter(button));
    }

    private void OnParentMouseExited(GUIMouseHoverEventArgs args)
    {
        if (_parentButton is not BaseButton button)
            return;

        HoverColorHelper.SetContentColor(this, HoverColorHelper.GetColorForMouseExit(button));
    }

    private void UpdateTextColor()
    {
        if (_parentButton is BaseButton button)
        {
            HoverColorHelper.SetContentColor(this, HoverColorHelper.GetColorForCurrentState(button));
        }
        else
        {
            HoverColorHelper.SetContentColor(this, HoverColorHelper.NormalColor);
        }
    }
}

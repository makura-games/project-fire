using Content.Shared._Scp.NukeCodeConsole;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Scp.NukeCodeConsole;

[UsedImplicitly]
public sealed class NukeCodeConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private NukeCodeConsoleMenu? _menu;

    public NukeCodeConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<NukeCodeConsoleMenu>();

        _menu.OnKeypadButtonPressed += i =>
        {
            SendMessage(new NukeCodeConsoleKeypadMessage(i));
        };
        _menu.OnEnterButtonPressed += () =>
        {
            SendMessage(new NukeCodeConsoleKeypadEnterMessage());
        };
        _menu.OnClearButtonPressed += () =>
        {
            SendMessage(new NukeCodeConsoleKeypadClearMessage());
        };

        _menu.ActivateButton.OnPressed += _ =>
        {
            SendMessage(new NukeCodeConsoleActivateButtonMessage());
        };
        _menu.LockButton.OnPressed += _ =>
        {
            SendMessage(new NukeCodeConsoleLockButtonMessage());
        };

        _menu.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (_menu == null)
            return;

        switch (state)
        {
            case NukeCodeConsoleUiState msg:
                _menu.UpdateState(msg);
                break;
        }
    }
}

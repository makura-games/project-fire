using Content.Client.Eui;
using Content.Shared._Scp.FreeScp;
using Content.Shared.Eui;
using JetBrains.Annotations;
using Robust.Client.Graphics;

namespace Content.Client._Scp.FreeScp;

[UsedImplicitly]
public sealed partial class FreeScpTransferEui : BaseEui
{
    [Dependency] private IClyde _clyde = default!;

    private FreeScpTransferWindow? _window;
    private bool _responded;

    public FreeScpTransferEui()
    {
        IoCManager.InjectDependencies(this);
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not FreeScpTransferEuiState s)
            return;
        _responded = true;
        _window?.Close();
        _responded = false;
        _window = new FreeScpTransferWindow(s.ScpName);

        _window.TransferNowButton.OnPressed += _ => SubmitChoice(FreeScpTransferMessage.Choice.Now);
        _window.WaitButton.OnPressed += _ => SubmitChoice(FreeScpTransferMessage.Choice.Wait);
        _window.DeclineButton.OnPressed += _ => SubmitChoice(FreeScpTransferMessage.Choice.Decline);
        _window.OnClose += () =>
        {
            if (!_responded)
                SubmitChoice(FreeScpTransferMessage.Choice.Decline);
        };

        _clyde.RequestWindowAttention();
        _window.OpenCentered();
    }

    private void SubmitChoice(FreeScpTransferMessage.Choice choice)
    {
        if (_responded)
            return;

        _responded = true;
        SendMessage(new FreeScpTransferMessage { Button = choice });
        _window?.Close();
    }

    public override void Closed()
    {
        _responded = true;
        _window?.Close();
    }
}

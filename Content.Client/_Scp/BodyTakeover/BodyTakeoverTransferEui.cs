using Content.Client.Eui;
using Content.Shared._Scp.BodyTakeover;
using Content.Shared.Eui;
using JetBrains.Annotations;
using Robust.Client.Graphics;

namespace Content.Client._Scp.BodyTakeover;

[UsedImplicitly]
public sealed partial class BodyTakeoverTransferEui : BaseEui
{
    [Dependency] private IClyde _clyde = default!;

    private BodyTakeoverTransferWindow? _window;
    private bool _responded;

    public BodyTakeoverTransferEui()
    {
        IoCManager.InjectDependencies(this);
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not BodyTakeoverTransferEuiState s)
            return;

        _responded = true;
        _window?.Close();
        _responded = false;

        _window = new BodyTakeoverTransferWindow(
            s.EntityName,
            s.TransferDelay,
            onNow: () => SubmitChoice(BodyTakeoverTransferMessage.Choice.Now),
            onWait: () => SubmitChoice(BodyTakeoverTransferMessage.Choice.Wait),
            onDecline: () => SubmitChoice(BodyTakeoverTransferMessage.Choice.Decline));

        _window.OnClose += () =>
        {
            if (!_responded)
                SubmitChoice(BodyTakeoverTransferMessage.Choice.Decline);
        };

        _clyde.RequestWindowAttention();
        _window.OpenCentered();
    }

    private void SubmitChoice(BodyTakeoverTransferMessage.Choice choice)
    {
        if (_responded)
            return;

        _responded = true;
        SendMessage(new BodyTakeoverTransferMessage { Button = choice });
        _window?.Close();
    }

    public override void Closed()
    {
        _responded = true;
        _window?.Close();
    }
}

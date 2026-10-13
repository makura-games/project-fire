using Content.Client.Eui;
using Content.Shared._Scp.BodyTakeover;
using Content.Shared.Eui;
using JetBrains.Annotations;
using Robust.Client.Graphics;

namespace Content.Client._Scp.BodyTakeover;

[UsedImplicitly]
public sealed partial class BodyTakeoverPollEui : BaseEui
{
    [Dependency] private IClyde _clyde = default!;

    private BodyTakeoverPollWindow? _window;
    private bool _responded;

    public BodyTakeoverPollEui()
    {
        IoCManager.InjectDependencies(this);
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not BodyTakeoverPollEuiState s)
            return;

        _responded = true;
        _window?.Close();
        _responded = false;

        _window = new BodyTakeoverPollWindow(
            s.EntityName,
            onAccept: () => SubmitChoice(BodyTakeoverPollMessage.Choice.Accept),
            onDecline: () => SubmitChoice(BodyTakeoverPollMessage.Choice.Decline));

        _window.OnClose += () =>
        {
            if (!_responded)
                SubmitChoice(BodyTakeoverPollMessage.Choice.Decline);
        };

        _clyde.RequestWindowAttention();
        _window.OpenCentered();
    }

    private void SubmitChoice(BodyTakeoverPollMessage.Choice choice)
    {
        if (_responded)
            return;

        _responded = true;
        SendMessage(new BodyTakeoverPollMessage { Button = choice });
        _window?.Close();
    }

    public override void Closed()
    {
        _responded = true;
        _window?.Close();
    }
}

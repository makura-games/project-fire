using Content.Client.Eui;
using Content.Shared._Scp.FreeScp;
using JetBrains.Annotations;
using Robust.Client.Graphics;

namespace Content.Client._Scp.FreeScp;

[UsedImplicitly]
public sealed partial class FreeScpPollEui : BaseEui
{
    [Dependency] private IClyde _clyde = default!;

    private readonly FreeScpPollWindow _window;
    private bool _responded;

    public FreeScpPollEui()
    {
        IoCManager.InjectDependencies(this);
        _window = new FreeScpPollWindow();

        _window.AcceptButton.OnPressed += _ => SubmitChoice(FreeScpPollMessage.Choice.Accept);
        _window.DeclineButton.OnPressed += _ => SubmitChoice(FreeScpPollMessage.Choice.Decline);
        _window.OnClose += () =>
        {
            if (!_responded)
                SubmitChoice(FreeScpPollMessage.Choice.Decline);
        };
    }

    private void SubmitChoice(FreeScpPollMessage.Choice choice)
    {
        if (_responded)
            return;

        _responded = true;
        SendMessage(new FreeScpPollMessage { Button = choice });
        _window.Close();
    }

    public override void Opened()
    {
        _clyde.RequestWindowAttention();
        _window.OpenCentered();
    }

    public override void Closed()
    {
        _responded = true;
        _window.Close();
    }
}

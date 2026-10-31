using Content.Shared._Scp.Amnesiac;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._Scp.Amnesiac;

public sealed partial class AmnesiacSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    private AmnesiacInfoWindow? _window;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<AmnesiacInfoEvent>(OnInfo);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        CloseWindow();
    }

    private void OnInfo(AmnesiacInfoEvent ev)
    {
        var reagent = _prototype.Index(ev.Reagent);

        CloseWindow();

        _window = _ui.CreateWindow<AmnesiacInfoWindow>();
        _window.SetInfo(reagent.LocalizedName, Loc.GetString(ev.ErasedMemory), ev.ConfirmDelay);
        _window.OnConfirmed += () => RaiseNetworkEvent(new AmnesiacConfirmEvent());
        _window.OpenCentered();
    }

    private void CloseWindow()
    {
        if (_window is { Disposed: false })
            _window.Close();

        _window = null;
    }
}

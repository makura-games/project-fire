using Content.Client.UserInterface.Screens;
using Content.Shared.CCVar;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;

namespace Content.Client._Scp.Other;

/// <summary>
/// Простая система, которая спасает игрока от собственной глупости.
/// Если он выставит слишком большой размер Viewport(который по умолчанию большой)
/// а затем выставит разделенный чат, то часть Viewport просто будет неиспользуема, зря сжирая его фпс и портя картинку.
/// </summary>
public sealed partial class AutoSetViewportSizeSystem : EntitySystem
{
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IConfigurationManager _configuration = default!;

    private const int SeparatedScreenDefaultViewportSize = 21;

    public override void Initialize()
    {
        base.Initialize();

        _ui.OnScreenChanged += args => OnScreenChanged(args.New);
    }

    private void OnScreenChanged(UIScreen? newScreen)
    {
        if (newScreen is not SeparatedChatGameScreen)
            return;

        var currentSize = _configuration.GetCVar(CCVars.ViewportWidth);

        if (currentSize <= SeparatedScreenDefaultViewportSize)
            return;

        _configuration.SetCVar(CCVars.ViewportWidth.Name, SeparatedScreenDefaultViewportSize);
    }
}

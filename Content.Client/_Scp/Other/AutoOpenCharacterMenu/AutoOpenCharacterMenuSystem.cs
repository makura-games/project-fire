using Content.Client.UserInterface.Systems.Character;
using Content.Shared._Scp.Other.AutoOpenCharacterMenu;
using Content.Shared._Scp.ScpCCVars;
using Robust.Client.Player;
using Robust.Client.UserInterface;

namespace Content.Client._Scp.Other.AutoOpenCharacterMenu;

public sealed partial class AutoOpenCharacterMenuSystem : SharedAutoOpenCharacterMenuSystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<OpenCharacterMenuRequest>(OnOpenRequest);

        _enabled = Configuration.GetCVar(ScpCCVars.AutoOpenCharacterMenuClientSideEnabled);
        Configuration.OnValueChanged(ScpCCVars.AutoOpenCharacterMenuClientSideEnabled, b => _enabled = b);
    }

    private void OnOpenRequest(OpenCharacterMenuRequest ev)
    {
        if (!_enabled)
            return;

        var playerEntity = GetEntity(ev.Entity);

        if (_player.LocalEntity != playerEntity)
            return;

        _ui.GetUIController<CharacterUIController>().ToggleWindow();
    }
}

using Content.Shared._Scp.Other.ShowMessageOnSpawn;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client._Scp.Other.ShowMessageOnSpawn;

public sealed partial class ShowMessageOnSpawnSystem : EntitySystem
{
    [Dependency] private IPlayerManager _playerManager = default!;
    private ShowMessageOnSpawnWindow? _window;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShowMessageOnSpawnComponent, MapInitEvent>(OnComponentInit);
        SubscribeLocalEvent<ShowMessageOnSpawnComponent, LocalPlayerAttachedEvent>(OnPlayerAttached);
    }

    private void OnComponentInit(Entity<ShowMessageOnSpawnComponent> ent, ref MapInitEvent args)
    {
        if (CanShowMessage(ent))
            ShowMessage(ent);
    }

    private void OnPlayerAttached(Entity<ShowMessageOnSpawnComponent> ent, ref LocalPlayerAttachedEvent args)
    {
        if (CanShowMessage(ent))
            ShowMessage(ent);
    }

    private void ShowMessage(Entity<ShowMessageOnSpawnComponent> ent)
    {
        _window = new ShowMessageOnSpawnWindow(ent.Comp.Message);

        _window?.OpenCentered();
        ent.Comp.MessageShowed = true;

        RaiseNetworkEvent(new ShowMessageOnSpawnWindowOpenedEvent { TargetEntity = GetNetEntity(ent) });
    }

    private bool CanShowMessage(Entity<ShowMessageOnSpawnComponent> ent)
    {
        if (_playerManager.LocalEntity != ent)
            return false;

        if (ent.Comp.MessageShowed)
            return false;

        return true;
    }
}

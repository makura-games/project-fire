using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Overlays.GasMask;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.IoC;
using Robust.Shared.Player;

namespace Content.Client.Overlays.Gasmask;

public sealed partial class GasMaskOverlaySystem : SharedGasMaskOverlaySystem
{
    [Dependency] private IOverlayManager _overlayMan = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private InventorySystem _inventory = default!;

    private GasMaskOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        Log.Info("[GASMASK] Client Initialize");

        _overlay = new GasMaskOverlay();

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<GasMaskOverlayComponent, AfterAutoHandleStateEvent>(OnHandleState);
    }

    private void OnPlayerAttached(LocalPlayerAttachedEvent args)
    {
        Log.Info($"[GASMASK] Player attached: {args.Entity}");
        RefreshOverlay(args.Entity);
    }

    private void OnPlayerDetached(LocalPlayerDetachedEvent args)
    {
        Log.Info("[GASMASK] Player detached");
        Deactivate();
    }

    private void OnHandleState(Entity<GasMaskOverlayComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        Log.Info($"[GASMASK] HandleState on {ent.Owner}");
        if (_player.LocalEntity is not { } localPlayer)
            return;

        if (localPlayer == ent.Owner || ent.Comp.RelayOverlay)
            RefreshOverlay(localPlayer);
    }

    protected override void RefreshOverlay(EntityUid entity)
    {
        Log.Info($"[GASMASK] RefreshOverlay for {entity}");

        if (entity != _player.LocalSession?.AttachedEntity)
        {
            Log.Info("[GASMASK] Not local player, skip");
            return;
        }


        var ev = new RefreshGasMaskOverlayEvent();
        RaiseLocalEvent(entity, ev);
        Log.Info($"[GASMASK] Relay returned {ev.Entities.Count} components");


        if (ev.Entities.Count == 0
            && _inventory.TryGetSlotEntity(entity, "mask", out var maskUid)
            && TryComp<GasMaskOverlayComponent>(maskUid, out var maskComp)
            && maskComp.Enabled
            && !(TryComp<MaskComponent>(maskUid, out var maskState) && maskState.IsToggled))
        {
            Log.Info($"[GASMASK] Direct slot check on {maskUid} -> activating");
            ActivateOverlay(maskComp.TexturePath);
            return;
        }

        if (ev.Entities.Count == 0)
        {
            Log.Info("[GASMASK] No components found, deactivating");
            Deactivate();
            return;
        }

        ActivateOverlay(ev.Entities[0].Comp.TexturePath);
    }

    private void ActivateOverlay(string texturePath)
    {
        Log.Info($"[GASMASK] Activating overlay with texture: {texturePath}");
        _overlay.SetTexture(texturePath);

        if (!_overlayMan.HasOverlay<GasMaskOverlay>())
        {
            Log.Info("[GASMASK] Adding overlay to manager");
            _overlayMan.AddOverlay(_overlay);
        }
    }

    private void Deactivate()
    {
        if (_overlayMan.HasOverlay<GasMaskOverlay>())
        {
            Log.Info("[GASMASK] Removing overlay");
            _overlayMan.RemoveOverlay(_overlay);
        }
    }
}

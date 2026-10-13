using Content.Shared._Scp.Scp939;
using Robust.Client.Graphics;
using Robust.Shared.Player;

namespace Content.Client._Scp.Scp939;

public sealed partial class Scp939HudSystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;

    private void InitializeOverlay()
    {
        SubscribeLocalEvent<Scp939Component, LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<Scp939Component, LocalPlayerDetachedEvent>(OnPlayerDetached);
    }

    private void OnPlayerAttached(Entity<Scp939Component> ent, ref LocalPlayerAttachedEvent args)
    {
        _scp939Component = ent.Comp;
        AddOverlays();
    }

    private void OnPlayerDetached(Entity<Scp939Component> ent, ref LocalPlayerDetachedEvent args)
    {
        _scp939Component = null;
    }

    private void AddOverlays()
    {
        if (_overlaysPresented)
            return;

        _overlayManager.AddOverlay(_setAlphaOverlay);
        _overlayManager.AddOverlay(_resetAlphaOverlay);

        _overlaysPresented = true;
    }

    private void RemoveOverlays()
    {
        if (!_overlaysPresented)
            return;

        _overlayManager.RemoveOverlay(_setAlphaOverlay);
        _overlayManager.RemoveOverlay(_resetAlphaOverlay);

        CachedBaseAlphas.Clear();
        _overlaysPresented = false;
    }
}

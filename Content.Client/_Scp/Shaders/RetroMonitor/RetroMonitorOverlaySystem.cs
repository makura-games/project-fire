using Content.Client._Scp.Shaders.Common;
using Content.Client._Scp.Shaders.Common.Grain;
using Content.Client._Scp.Shaders.Common.Vignette;
using Content.Shared._Scp.RetroMonitor;
using Robust.Shared.Player;

namespace Content.Client._Scp.Shaders.RetroMonitor;

public sealed partial class RetroMonitorOverlaySystem : ComponentOverlaySystem<RetroMonitorOverlay, RetroMonitorViewComponent>
{
    [Dependency] private GrainOverlaySystem _grain = default!;
    [Dependency] private VignetteOverlaySystem _vignette = default!;

    public override void Initialize()
    {
        base.Initialize();

        Overlay = new RetroMonitorOverlay();
    }

    protected override void OnPlayerAttached(Entity<RetroMonitorViewComponent> ent, ref LocalPlayerAttachedEvent args)
    {
        base.OnPlayerAttached(ent, ref args);

        _grain.TryRemoveOverlay();
        _vignette.TryRemoveOverlay();
    }

    protected override void OnPlayerDetached(Entity<RetroMonitorViewComponent> ent, ref LocalPlayerDetachedEvent args)
    {
        base.OnPlayerDetached(ent, ref args);

        _grain.TryAddOverlay();
        _vignette.TryAddOverlay();
    }
}

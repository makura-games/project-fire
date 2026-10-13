using Content.Client._Scp.Scp096.Overlays;
using Content.Shared._Scp.Scp096.Main.Components;
using Content.Shared._Scp.Scp096.Main.Systems;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._Scp.Scp096;

public sealed partial class Scp096System : SharedScp096System
{
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IGameTiming _timing = default!;

    private Scp096TargetsOverlay? _targetsOverlay;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Scp096Component, LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<Scp096Component, LocalPlayerDetachedEvent>(OnPlayerDetached);

        InitializeWidget();
        InitializeRage();

        Log.Level = LogLevel.Info;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        ShutdownWidget();
        RemoveTargetsOverlay();
        ShutdownRage();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        FrameUpdateRage(frameTime);
    }

    private void OnPlayerAttached(Entity<Scp096Component> ent, ref LocalPlayerAttachedEvent args)
    {
        AddTargetsOverlay(ent);
        EnsureWidgetExist();
    }

    private void OnPlayerDetached(Entity<Scp096Component> ent, ref LocalPlayerDetachedEvent args)
    {
        RemoveTargetsOverlay();
        RemoveWidget();
    }

    protected override void OnInit(Entity<Scp096Component> ent, ref ComponentInit args)
    {
        base.OnInit(ent, ref args);

        if (_player.LocalEntity != ent)
            return;

        EnsureWidgetExist();
        AddTargetsOverlay(ent);
    }

    protected override void OnShutdown(Entity<Scp096Component> ent, ref ComponentShutdown args)
    {
        base.OnShutdown(ent, ref args);

        if (_player.LocalEntity != ent)
            return;

        RemoveTargetsOverlay();
        RemoveWidget();
    }

    private void AddTargetsOverlay(Entity<Scp096Component> ent)
    {
        if (_targetsOverlay != null)
            return;

        _targetsOverlay = new(ent);
        _overlay.AddOverlay(_targetsOverlay);
    }

    private void RemoveTargetsOverlay()
    {
        if (_targetsOverlay == null)
            return;

        _overlay.RemoveOverlay(_targetsOverlay);
        _targetsOverlay.Dispose();
        _targetsOverlay = null;
    }
}

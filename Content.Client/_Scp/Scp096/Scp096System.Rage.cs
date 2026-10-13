using Content.Client._Scp.Shaders.Common;
using Content.Client._Scp.Shaders.Scp096.Rage;
using Content.Shared._Scp.Scp096.Main.Components;
using Robust.Shared.Player;

namespace Content.Client._Scp.Scp096;

public sealed partial class Scp096System
{
    [Dependency] private CompatibilityModeActiveWarningSystem _compatibility = default!;

    private Scp096RageOverlay? _rageOverlay;

    private void InitializeRage()
    {
        SubscribeLocalEvent<ActiveScp096HeatingUpComponent, LocalPlayerAttachedEvent>(OnHeatingUpPlayerAttached);
        SubscribeLocalEvent<ActiveScp096HeatingUpComponent, LocalPlayerDetachedEvent>(OnHeatingUpPlayerDetached);

        SubscribeLocalEvent<ActiveScp096RageComponent, LocalPlayerAttachedEvent>(OnRagePlayerAttached);
        SubscribeLocalEvent<ActiveScp096RageComponent, LocalPlayerDetachedEvent>(OnRagePlayerDetached);
    }

    private void ShutdownRage()
    {
        RemoveRageOverlay();
    }

    #region Update

    private void FrameUpdateRage(float frameTime)
    {
        InterpolateIntensity();
    }

    private void InterpolateIntensity()
    {
        if (_rageOverlay == null)
            return;

        if (!HeatingUpQuery.TryComp(_player.LocalEntity, out var comp))
            return;

        if (comp.RageHeatUpEnd == null)
            return;

        var remaining = comp.RageHeatUpEnd.Value - _timing.CurTime;
        var total = comp.RageHeatUp;

        if (remaining <= TimeSpan.Zero)
        {
            _rageOverlay.Intensity = (float)comp.OverlayIntensityMax;
            return;
        }

        var t = 1f - (remaining / total); // 0 → 1
        t = Math.Clamp(t, 0f, 1f);
        t = Math.Pow(t, 3);

        var intensity = MathHelper.Lerp(
            comp.OverlayIntensityMin,
            comp.OverlayIntensityMax,
            t);

        _rageOverlay.Intensity = (float)intensity;
    }

    #endregion

    #region Heating up

    protected override void OnHeatingUpStart(Entity<ActiveScp096HeatingUpComponent> ent, ref ComponentStartup args)
    {
        base.OnHeatingUpStart(ent, ref args);

        if (_player.LocalEntity != ent)
            return;

        AddRageOverlay((float) ent.Comp.OverlayIntensityMin);
    }

    protected override void OnHeatingUpShutdown(Entity<ActiveScp096HeatingUpComponent> ent, ref ComponentShutdown args)
    {
        base.OnHeatingUpShutdown(ent, ref args);

        if (_player.LocalEntity != ent)
            return;

        RemoveRageOverlay();
    }

    private void OnHeatingUpPlayerAttached(Entity<ActiveScp096HeatingUpComponent> ent,
        ref LocalPlayerAttachedEvent args)
    {
        AddRageOverlay((float) ent.Comp.OverlayIntensityMin);
    }

    private void OnHeatingUpPlayerDetached(Entity<ActiveScp096HeatingUpComponent> ent,
        ref LocalPlayerDetachedEvent args)
    {
        RemoveRageOverlay();
    }

    #endregion

    #region Rage

    protected override void OnRageStart(Entity<ActiveScp096RageComponent> ent, ref ComponentStartup args)
    {
        base.OnRageStart(ent, ref args);

        if (_player.LocalEntity != ent)
            return;

        AddRageOverlay();
    }

    protected override void OnRageShutdown(Entity<ActiveScp096RageComponent> ent, ref ComponentShutdown args)
    {
        base.OnRageShutdown(ent, ref args);

        if (_player.LocalEntity != ent)
            return;

        RemoveRageOverlay();
    }

    private void OnRagePlayerAttached(Entity<ActiveScp096RageComponent> ent, ref LocalPlayerAttachedEvent args)
    {
        AddRageOverlay();
    }

    private void OnRagePlayerDetached(Entity<ActiveScp096RageComponent> ent, ref LocalPlayerDetachedEvent args)
    {
        RemoveRageOverlay();
    }

    #endregion

    private void AddRageOverlay(float intensity = 1f)
    {
        if (_rageOverlay is not null)
            return;

        if (!_compatibility.ShouldUseShaders)
            return;

        _rageOverlay = new(intensity);
        _overlay.AddOverlay(_rageOverlay);
    }

    private void RemoveRageOverlay()
    {
        if (_rageOverlay is null)
            return;

        _overlay.RemoveOverlay(_rageOverlay);
        _rageOverlay.Dispose();
        _rageOverlay = null;
    }
}

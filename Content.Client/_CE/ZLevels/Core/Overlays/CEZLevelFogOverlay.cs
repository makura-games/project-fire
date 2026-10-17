using System.Numerics;
using Content.Client.Viewport;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._CE.ZLevels.Core.Overlays;

/// <summary>
/// Draws a drifting noise fog over every z-level below the eye. Drawn
/// <see cref="OverlaySpace.WorldSpaceBelowFOV"/> - above entities but below lighting/FOV - so
/// the engine's own lighting pass applies to the fog same as the rest of the scene.
/// </summary>
public sealed partial class CEZLevelFogOverlay : Overlay
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IEntityManager _entity = default!;
    [Dependency] private IGameTiming _timing = default!;
    private readonly ShaderInstance? _fogShader;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    private readonly ProtoId<ShaderPrototype> _zFogShader = "CEZFog";

    private static readonly Vector2 FogWindVelocity = new(-0.4f, 0.15f);
    private const float FogEvolutionSpeed = 0.15f;

    // Flat opacity, same for every level regardless of distance below the eye.
    private const float FogAmount = 0.045f;

    // Every level samples the same world_pos each frame, so this offsets the sample per depth
    // to keep stacked levels from showing an identical fog pattern.
    private const float FogDepthSeedScale = 37.1f;

    public CEZLevelFogOverlay()
    {
        IoCManager.InjectDependencies(this);
        _fogShader = _proto.Index(_zFogShader).InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye is not ScalingViewport.ZEye zeye)
            return false;

        if (zeye.Depth >= 0)
            return false;

        if (args.MapId == MapId.Nullspace)
            return false;

        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye is not ScalingViewport.ZEye zeye)
            return;

        var fogColor = new Vector3(0, 0, 1); //Default blue

        if (_entity.TryGetComponent<MapLightComponent>(args.MapUid, out var mapLight))
        {
            fogColor = new Vector3(
                mapLight.AmbientLightColor.R,
                mapLight.AmbientLightColor.G,
                mapLight.AmbientLightColor.B);
        }

        var bounds = args.WorldBounds;
        var time = (float) _timing.CurTime.TotalSeconds;
        var windOffset = FogWindVelocity * time;
        var seedOffset = new Vector2(zeye.Depth * FogDepthSeedScale);

        _fogShader?.SetParameter("FOG_COLOR", fogColor);
        _fogShader?.SetParameter("corner_bl", bounds.BottomLeft);
        _fogShader?.SetParameter("corner_br", bounds.BottomRight);
        _fogShader?.SetParameter("corner_tl", bounds.TopLeft);
        _fogShader?.SetParameter("corner_tr", bounds.TopRight);
        _fogShader?.SetParameter("wind_offset", windOffset);
        _fogShader?.SetParameter("seed_offset", seedOffset);
        _fogShader?.SetParameter("time_z", FogEvolutionSpeed * time);
        _fogShader?.SetParameter("fog_amount", FogAmount);

        var worldHandle = args.WorldHandle;
        worldHandle.UseShader(_fogShader);
        worldHandle.DrawRect(bounds, Color.White);
        worldHandle.UseShader(null);
    }
}

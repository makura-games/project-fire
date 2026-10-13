using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Scp.Shaders.Warping;

public sealed partial class WarpOverlay : Overlay
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override bool RequestScreenTexture => true;
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    private readonly ShaderInstance _shader;
    private static readonly ProtoId<ShaderPrototype> ShaderProtoId = "Warping";

    private readonly TimeSpan _startTime;

    private const float StartIntensity = 0.5f;
    private const float FinalIntensity = 10.0f;
    private const float IntensityIncrement = 0.1f;
    private const double TimeInterval = 6.0d;

    public WarpOverlay(TimeSpan startedTime)
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypeManager.Index(ShaderProtoId).InstanceUnique();

        _startTime = startedTime;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var handle = args.WorldHandle;
        var viewport = args.WorldBounds;

        // Вычисление прошедшего времени
        var elapsedTime = _timing.CurTime - _startTime;

        // Вычисление интенсивности эффекта
        var intensity = StartIntensity + (float)(elapsedTime.TotalSeconds / TimeInterval) * IntensityIncrement;
        intensity = Math.Clamp(intensity, StartIntensity, FinalIntensity);

        // Применяем интенсивность в шейдер
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("warp_intensity", intensity);
        _shader.SetParameter("time_scale", 5.0f);

        handle.UseShader(_shader);
        handle.DrawRect(viewport, Color.White);
        handle.UseShader(null);
    }

    protected override void DisposeBehavior()
    {
        base.DisposeBehavior();

        _shader.Dispose();
    }
}

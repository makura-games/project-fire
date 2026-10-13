using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using System.Numerics;
using Content.Client.Graphics;
using Robust.Client.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._Scp.Shaders.FieldOfView.Overlays;

/// <summary>
/// Рисует конус видимости и небольшой круг вокруг персонажа
/// </summary>
public sealed partial class FieldOfViewConeOverlay : Overlay
{
    [Dependency] private IEntityManager _ent = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly FieldOfViewOverlayManagementSystem _fovManagement;
    private readonly TransformSystem _transform;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _shader;
    private readonly ShaderInstance _blurXShader;
    private readonly ShaderInstance _blurYShader;

    private static readonly ProtoId<ShaderPrototype> ViewconeShaderProtoId = "Viewcone";
    private static readonly ProtoId<ShaderPrototype> BlurryXShaderProtoId = "BlurryVisionX";
    private static readonly ProtoId<ShaderPrototype> BlurryYShaderProtoId = "BlurryVisionY";

    private readonly OverlayResourceCache<CachedResources> _resources = new ();

    private TimeSpan _nextUpdate = TimeSpan.Zero;

    /// <summary>
    /// Размер текстуры размытия.
    /// </summary>
    public float BlurScale = 0.7f;

    /// <summary>
    /// Прозрачность конуса
    /// </summary>
    public float Opacity = 0.85f;

    /// <summary>
    /// Whether blur is enabled outside the field of view
    /// </summary>
    public bool BlurEnabled = true;

    private static readonly Vector2 OffsetVectorFix = new (1, -1);

    /// <summary>
    /// Дополнительный отступ в метрах для радиуса и размытия конуса
    /// </summary>
    private const float AdditionalMarginMeters = 0.4f;

    public FieldOfViewConeOverlay()
    {
        IoCManager.InjectDependencies(this);

        _shader = _proto.Index(ViewconeShaderProtoId).InstanceUnique();
        _blurXShader = _proto.Index(BlurryXShaderProtoId).InstanceUnique();
        _blurYShader = _proto.Index(BlurryYShaderProtoId).InstanceUnique();

        _fovManagement = _ent.System<FieldOfViewOverlayManagementSystem>();
        _transform = _ent.System<TransformSystem>();
    }

    protected override void DisposeBehavior()
    {
        base.DisposeBehavior();

        _shader.Dispose();
        _blurXShader.Dispose();
        _blurYShader.Dispose();

        _resources.Dispose();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_fovManagement.PlayerEntity.HasValue)
            return false;

        var player = _fovManagement.PlayerEntity.Value;

        if (args.Viewport.Eye != player.Comp1.Eye)
            return false;

        var res = _resources.GetForViewport(args.Viewport, static _ => new CachedResources());

        if (!BlurEnabled)
        {
            res.BackBuffer?.Dispose();
            res.BackBuffer = null;
            res.BlurPass?.Dispose();
            res.BlurPass = null;
            return true;
        }

        var size = (Vector2i)(args.Viewport.Size * BlurScale);

        if (res.BackBuffer == null || res.BackBuffer.Size != size)
        {
            res.BackBuffer?.Dispose();
            res.BackBuffer = _clyde.CreateRenderTarget(size, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "fov-backbuffer");

            res.BlurPass?.Dispose();
            res.BlurPass = _clyde.CreateRenderTarget(size, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "fov-blurpass");
        }

        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null || !_fovManagement.PlayerEntity.HasValue)
            return;

        var res = _resources.GetForViewport(args.Viewport, static _ => new CachedResources());
        if (BlurEnabled && (res.BackBuffer == null || res.BlurPass == null))
            return;

        var (uid, eye, fov, xform) = _fovManagement.PlayerEntity.Value;

        var handle = args.WorldHandle;
        var viewport = args.WorldBounds;

        if (BlurEnabled && _timing.RealTime >= _nextUpdate)
        {
            if (res.BackBuffer == null || res.BlurPass == null)
                return;

            var backBuffer = res.BackBuffer;
            var blurPass = res.BlurPass;
            var viewportBounds = new Box2(Vector2.Zero, blurPass.Size);

            handle.RenderInRenderTarget(blurPass, () =>
            {
                _blurXShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
                handle.UseShader(_blurXShader);
                handle.DrawRect(viewportBounds, Color.White);
            }, Color.Transparent);

            handle.RenderInRenderTarget(backBuffer, () =>
            {
                _blurYShader.SetParameter("SCREEN_TEXTURE", blurPass.Texture);
                handle.UseShader(_blurYShader);
                handle.DrawRect(viewportBounds, Color.White);
            }, Color.Transparent);

            _nextUpdate = _timing.RealTime + _fovManagement.UpdateInterval;
        }

        var offset = GetOffset(uid, xform, eye);
        var blurredTexture = BlurEnabled && res.BackBuffer != null ? res.BackBuffer.Texture : ScreenTexture;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("BLURRED_TEXTURE", blurredTexture);
        _shader.SetParameter("coneOpacity", Opacity);

        _shader.SetParameter("ViewAngle", (float) fov.CurrentAngle.Theta);
        _shader.SetParameter("ConeAngle", fov.Angle);
        _shader.SetParameter("ConeFeather", fov.AngleFeather);
        _shader.SetParameter("ConeIgnoreRadius", (fov.ConeIgnoreRadius + AdditionalMarginMeters) * EyeManager.PixelsPerMeter / eye.Zoom.X);
        _shader.SetParameter("ConeIgnoreFeather", (fov.ConeIgnoreFeather + AdditionalMarginMeters) * EyeManager.PixelsPerMeter / eye.Zoom.X);
        _shader.SetParameter("Offset", offset);

        handle.UseShader(_shader);
        handle.DrawRect(viewport, Color.White);
        handle.UseShader(null);
    }

    private Vector2 GetOffset(EntityUid uid, TransformComponent xform, EyeComponent eye)
    {
        if (eye.Offset == Vector2.Zero)
            return Vector2.Zero;

        // Так как смещение задано в координатах карты, а нам нужны экранные
        // то мы должны сделать обратную операцию и вернуться к координатам персонажа
        // переконвертировать их в экранные координаты и снова высчитать смещение

        var playerCoords = _transform.GetMapCoordinates(uid, xform);
        var playerCoordsWithOffset = new MapCoordinates(playerCoords.Position + eye.Offset, playerCoords.MapId);

        var localCoords = _eye.MapToScreen(playerCoords);
        var localWithOffset = _eye.MapToScreen(playerCoordsWithOffset);

        var offset = localCoords.Position - localWithOffset.Position;

        // внутри преображения мировых координат в локальные зачем-то есть это умножение и оно ломает Y координату
        // Отменяем это говно повторным умножением.
        offset *= OffsetVectorFix;

        return offset;
    }

    private sealed class CachedResources : IDisposable
    {
        public IRenderTexture? BlurPass;
        public IRenderTexture? BackBuffer;

        public void Dispose()
        {
            BlurPass?.Dispose();
            BackBuffer?.Dispose();
        }
    }
}

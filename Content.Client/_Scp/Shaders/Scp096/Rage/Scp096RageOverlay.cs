using System.Numerics;
using Content.Shared._Scp.Scp096.Main.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Scp.Shaders.Scp096.Rage;

public sealed partial class Scp096RageOverlay : Overlay
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly SpriteSystem _sprite;
    private readonly TransformSystem _transform;

    /// <summary>
    /// Скорость пульсаций(сердцебиения).
    /// </summary>
    private const float TargetPulseSpeed = 8f;

    /// <summary>
    /// Насколько будет увеличен спрайт?
    /// Это добавляется к текущему скейлу спрайта.(1 -> 1.15)
    /// </summary>
    private const float MaxScaleIncrease = 0.15f;

    public float Intensity;

    private static readonly Color MinColor = Color.Red.WithAlpha(0.6f);
    private static readonly Color MaxColor = Color.Red.WithAlpha(1.0f);

    private readonly ShaderInstance _shader;
    private static readonly ProtoId<ShaderPrototype> ShaderProtoId = "Scp096Rage";

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    public Scp096RageOverlay(float intensity = 1.0f)
    {
        IoCManager.InjectDependencies(this);

        _sprite = _entManager.System<SpriteSystem>();
        _transform = _entManager.System<TransformSystem>();

        _shader = _prototype.Index(ShaderProtoId).InstanceUnique();

        Intensity = intensity;

        ZIndex = 100;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        // 1. Отрисовка полноэкранного шейдера ярости
        var worldHandle = args.WorldHandle;
        var viewport = args.WorldBounds;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("RageIntensity", Intensity);
        _shader.SetParameter("Time", (float) _timing.RealTime.TotalSeconds);

        worldHandle.UseShader(_shader);
        worldHandle.DrawRect(viewport, Color.White);
        worldHandle.UseShader(null);

        // 2. Отрисовка целей поверх шейдера (Highlight)
        // Шейдер обесцвечивает мир, поэтому мы рисуем цели поверх него,
        // чтобы они оставались яркими и цветными (с красным оттенком).
        DrawTargets(worldHandle, in args);
    }

    private void DrawTargets(DrawingHandleWorld handle, in OverlayDrawArgs args)
    {
        var eyeRot = args.Viewport.Eye?.Rotation ?? Angle.Zero;

        var time = (float)_timing.RealTime.TotalSeconds;

        // Синусоида от 0.0 до 1.0
        var pulse = (MathF.Sin(time * TargetPulseSpeed) + 1f) / 2f;

        // Интерполяция цвета от тусклого к яркому
        var rageColor = Color.InterpolateBetween(MinColor, MaxColor, pulse);

        // Множитель размера (от 1.0 до 1.15)
        var scaleMulti = 1.0f + (pulse * MaxScaleIncrease);
        var rageScaleVec = new Vector2(scaleMulti, scaleMulti);

        var query = _entManager.AllEntityQueryEnumerator<Scp096TargetComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var sprite, out var xform))
        {
            if (!sprite.Visible || MathHelper.CloseTo(sprite.Color.A, 0f))
                continue;

            var (targetPos, targetRot) = _transform.GetWorldPositionRotation(xform);

            var oldColor = sprite.Color;
            var oldScale = sprite.Scale;
            var ent = (uid, sprite);

            _sprite.SetColor(ent, rageColor);
            _sprite.SetScale(ent, oldScale * rageScaleVec);

            _sprite.RenderSprite(ent, handle, eyeRot, targetRot, targetPos);

            _sprite.SetColor(ent, oldColor);
            _sprite.SetScale(ent, oldScale);
        }
    }

    protected override void DisposeBehavior()
    {
        base.DisposeBehavior();

        _shader.Dispose();
    }
}

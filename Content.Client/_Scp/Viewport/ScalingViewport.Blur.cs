using System;
using System.Numerics;
using Content.Client.Graphics;
using Robust.Client.Graphics;
using Robust.Shared.Graphics;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Client.Viewport;

public sealed partial class ScalingViewport
{
    [Dependency] private IPrototypeManager _proto = default!;

    private static readonly ProtoId<ShaderPrototype> ZLevelBlurShader = "CEZBlur";

    private readonly OverlayResourceCache<UpperZLevelResources> _upperZLevelResources = new();

    private void RenderUpperZLevel(IClydeViewport viewport, IRenderHandle handle)
    {
        var resources = _upperZLevelResources.GetForViewport(viewport, CreateUpperZLevelResources);
        var upperViewport = resources.Viewport;
        upperViewport.Eye = viewport.Eye;
        upperViewport.RenderScale = viewport.RenderScale;
        RenderZLevelViewport(upperViewport);

        var ambientColor = new Vector3(0, 0, 1);
        if (viewport.Eye is { } eye &&
            _mapSystem is { } mapSystem &&
            _entityManager.TryGetComponent<MapLightComponent>(mapSystem.GetMapOrInvalid(eye.Position.MapId), out var mapLight))
        {
            ambientColor = new Vector3(
                mapLight.AmbientLightColor.R,
                mapLight.AmbientLightColor.G,
                mapLight.AmbientLightColor.B);
        }

        var shader = resources.BlurShader;
        shader.SetParameter("SCREEN_TEXTURE", upperViewport.RenderTarget.Texture);
        shader.SetParameter("BLUR_COLOR", ambientColor);

        // В выборку размытия попадает только верхний этаж; текущий сохраняет резкость.
        handle.RenderInRenderTarget(viewport.RenderTarget, () =>
        {
            var screen = handle.DrawingHandleScreen;
            var modulate = screen.Modulate;
            screen.Modulate = Color.White;
            handle.SetScissor(null);
            screen.SetTransform(Matrix3x2.Identity);
            screen.UseShader(shader);
            try
            {
                screen.DrawTextureRect(upperViewport.RenderTarget.Texture,
                    UIBox2.FromDimensions(Vector2.Zero, viewport.Size));
            }
            finally
            {
                screen.UseShader(null);
                screen.Modulate = modulate;
            }
        }, null);
    }

    private UpperZLevelResources CreateUpperZLevelResources(IClydeViewport viewport)
    {
        var upperViewport = _clyde.CreateViewport(viewport.Size,
            new TextureSampleParameters
            {
                Filter = StretchMode == ScalingViewportStretchMode.Bilinear,
            },
            name: "upper-z-level");
        // Color.Transparent содержит белый RGB, который проявляется в стекле и на границах размытия.
        upperViewport.ClearColor = new Color(0f, 0f, 0f, 0f);

        return new UpperZLevelResources(upperViewport, _proto.Index(ZLevelBlurShader).InstanceUnique());
    }

    protected override void ExitedTree()
    {
        _upperZLevelResources.Dispose();
        base.ExitedTree();
    }

    private sealed class UpperZLevelResources(IClydeViewport viewport, ShaderInstance blurShader) : IDisposable
    {
        public IClydeViewport Viewport { get; } = viewport;
        public ShaderInstance BlurShader { get; } = blurShader;

        public void Dispose()
        {
            BlurShader.Dispose();
            Viewport.Dispose();
        }
    }
}

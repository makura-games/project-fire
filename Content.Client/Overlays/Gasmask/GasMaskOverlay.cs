using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.IoC;
using Robust.Shared.Log;

namespace Content.Client.Overlays.Gasmask;

public sealed partial class GasMaskOverlay : Overlay
{
    [Dependency] private IResourceCache _resCache = default!;
    [Dependency] private ILogManager _logManager = default!;

    private ISawmill _log = default!;
    private Texture? _texture;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public GasMaskOverlay()
    {
        IoCManager.InjectDependencies(this);
        _log = _logManager.GetSawmill("gasmask.overlay");
    }

    public void SetTexture(string texturePath)
    {
        if (!_resCache.TryGetResource<TextureResource>(texturePath, out var texture))
        {
            _log.Error($"[GASMASK] Failed to load texture: {texturePath}");
            return;
        }

        _texture = texture;
        _log.Info($"[GASMASK] Texture loaded: {texturePath}");
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_texture == null)
            return;

        args.ScreenHandle.DrawTextureRect(_texture, args.ViewportBounds);
    }
}

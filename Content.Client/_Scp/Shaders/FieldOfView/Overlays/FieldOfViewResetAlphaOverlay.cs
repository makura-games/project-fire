using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._Scp.Shaders.FieldOfView.Overlays;

/// <summary>
///     After <see cref="FieldOfViewSetAlphaOverlay"/> has run, resets the alpha of affected entities
///     back to normal.
/// </summary>
public sealed partial class FieldOfViewResetAlphaOverlay : Overlay
{
    [Dependency] private IEntityManager _ent = default!;

    private readonly FieldOfViewOverlayManagementSystem _cone;
    private readonly SpriteSystem _sprite;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public FieldOfViewResetAlphaOverlay()
    {
        IoCManager.InjectDependencies(this);

        _cone = _ent.System<FieldOfViewOverlayManagementSystem>();
        _sprite = _ent.System<SpriteSystem>();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        base.BeforeDraw(in args);

        if (_cone.CachedBaseAlphas.Count == 0)
            return false;

        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        foreach (var (ent, baseAlpha) in _cone.CachedBaseAlphas)
        {
            _sprite.SetColor(ent!, ent.Comp.Color.WithAlpha(baseAlpha));
        }

        _cone.CachedBaseAlphas.Clear();
    }
}


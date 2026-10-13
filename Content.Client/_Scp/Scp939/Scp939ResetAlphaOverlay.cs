using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._Scp.Scp939;

public sealed partial class Scp939ResetAlphaOverlay : Overlay
{
    [Dependency] private IEntityManager _ent = default!;

    private readonly Scp939HudSystem _hud;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public Scp939ResetAlphaOverlay()
    {
        IoCManager.InjectDependencies(this);

        _hud = _ent.System<Scp939HudSystem>();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _hud.CachedBaseAlphas.Count > 0 && _hud.CanDraw(in args);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        _hud.RestoreCachedBaseAlphas();
    }
}

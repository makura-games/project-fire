using Content.Shared._Scp.Scp939;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._Scp.Scp939;

public sealed partial class Scp939SetAlphaOverlay : Overlay
{
    [Dependency] private IEntityManager _ent = default!;

    private readonly Scp939HudSystem _hud;
    private readonly TransformSystem _transform;
    private readonly SpriteSystem _sprite;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    public Scp939SetAlphaOverlay()
    {
        IoCManager.InjectDependencies(this);

        _hud = _ent.System<Scp939HudSystem>();
        _transform = _ent.System<TransformSystem>();
        _sprite = _ent.System<SpriteSystem>();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _hud.CanDraw(in args);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        _hud.CachedBaseAlphas.Clear();

        var query = _ent.EntityQueryEnumerator<ActiveScp939VisibilityComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var visibility, out var sprite, out var xform))
        {
            if (xform.MapID != args.MapId || !sprite.Visible)
                continue;

            var worldPosition = _transform.GetWorldPosition(xform);
            if (!args.WorldBounds.Contains(worldPosition))
                continue;

            var targetAlpha = Scp939HudSystem.GetVisibility((uid, visibility));
            if (MathF.Abs(sprite.Color.A - targetAlpha) <= 0.01f)
                continue;

            var entity = (uid, sprite);
            _hud.CachedBaseAlphas.Add((entity, sprite.Color.A));
            _sprite.SetColor(entity, sprite.Color.WithAlpha(targetAlpha));
        }
    }
}

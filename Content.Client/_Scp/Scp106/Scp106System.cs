using Content.Client.Alerts;
using Content.Shared._Scp.Scp106.Components;
using Content.Shared._Scp.Scp106.Systems;
using Robust.Client.GameObjects;

namespace Content.Client._Scp.Scp106;

public sealed partial class Scp106System : SharedScp106System
{
    [Dependency] private SpriteSystem _sprite = default!;

    private const int MaxEssence = 100;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Scp106Component, UpdateAlertSpriteEvent>(OnUpdateAlert);
    }

    private void OnUpdateAlert(Entity<Scp106Component> ent, ref UpdateAlertSpriteEvent args)
    {
        if (args.Alert.ID != ent.Comp.Scp106EssenceAlert)
            return;

        var essence = Math.Clamp(ent.Comp.Essence.Int(), 0, MaxEssence);

        _sprite.LayerSetRsiState(args.SpriteViewEnt.AsNullable(), Scp106VisualLayers.Digit1, $"{(essence / 100) % 10}");
        _sprite.LayerSetRsiState(args.SpriteViewEnt.AsNullable(), Scp106VisualLayers.Digit2, $"{(essence / 10) % 10}");
        _sprite.LayerSetRsiState(args.SpriteViewEnt.AsNullable(), Scp106VisualLayers.Digit3, $"{essence % 10}");
    }
}

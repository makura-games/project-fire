using Content.Server.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.Map.Components;

namespace Content.Server.Light.EntitySystems;

/// <inheritdoc/>
public sealed partial class RoofSystem : SharedRoofSystem
{
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;

    public override void Initialize()
    {
        base.Initialize();
        // Fire edit start - для маппинга отключенной крыши
        SubscribeLocalEvent<SetRoofComponent, MapInitEvent>(OnFlagStartup);
        // Fire edit end
    }

    // Fire edit - для маппинга отключенной крыши
    private void OnFlagStartup(Entity<SetRoofComponent> ent, ref MapInitEvent args)
    {
        var xform = Transform(ent.Owner);

        if (_mapGridQuery.TryComp(xform.GridUid, out var grid))
        {
            var index = _maps.LocalToTile(xform.GridUid.Value, grid, xform.Coordinates);
            SetRoof((xform.GridUid.Value, grid, null), index, ent.Comp.Value, ent.Comp.BlockWeather);
        }

        QueueDel(ent.Owner);
    }
}

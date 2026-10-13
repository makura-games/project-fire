using Content.Server.Light.EntitySystems;
using Content.Shared.Light.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map.Components;

namespace Content.Server._Scp.Other.AutoRoof;

/// <summary>
/// Система, автоматически покрывающая станцию крышей при инициализации на ней специальной сущности
/// </summary>
public sealed partial class AutoRoofSystem : EntitySystem
{
    [Dependency] private RoofSystem _roof = default!;
    [Dependency] private MapSystem _map = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AutoRoofComponent, MapInitEvent>(OnInit, before: [typeof(RoofSystem)]);
    }

    private void OnInit(Entity<AutoRoofComponent> ent, ref MapInitEvent args)
    {
        var grid = Transform(ent).GridUid;
        if (!grid.HasValue)
            return;

        BuildRoof(grid.Value);
        QueueDel(ent);
    }

    /// <summary>
    /// Строит крышу над станцией
    /// </summary>
    public void BuildRoof(Entity<MapGridComponent?> grid)
    {
        if (!Resolve(grid, ref grid.Comp))
            return;

        RemComp<ImplicitRoofComponent>(grid);
        EnsureComp<RoofComponent>(grid);

        foreach (var tile in _map.GetAllTiles(grid, grid.Comp))
        {
            _roof.SetRoof(grid, tile.GridIndices, true, true);
        }
    }
}

using System.Threading.Tasks;
using Content.Shared._Scp.Procedural.PostGeneration;
using Content.Shared.Procedural;
using Robust.Shared.Collections;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server.Procedural.DungeonJob;

public sealed partial class DungeonJob
{
    /// <summary>
    /// Генерирует ограниченное количество сущностей в случайных свободных клетках подземелья.
    /// <see cref="LimitedEntityDunGen"/>
    /// </summary>
    private async Task PostGen(LimitedEntityDunGen gen, Dungeon dungeon, IRobustRandom random)
    {
        if (gen.Limit <= 0)
            return;

        var availableTiles = new ValueList<Vector2i>(dungeon.AllTiles);

        for (var i = 0; i < gen.Limit && availableTiles.Count > 0; i++)
        {
            while (availableTiles.Count > 0)
            {
                var tile = availableTiles.RemoveSwap(random.Next(availableTiles.Count));

                if (!_anchorable.TileFree((_gridUid, _grid), tile, DungeonSystem.CollisionLayer, DungeonSystem.CollisionMask))
                    continue;

                _entManager.SpawnAtPosition(gen.Entity, _maps.GridTileToLocal(_gridUid, _grid, tile));
                break;
            }

            await SuspendDungeon();

            if (!ValidateResume())
                return;
        }
    }
}

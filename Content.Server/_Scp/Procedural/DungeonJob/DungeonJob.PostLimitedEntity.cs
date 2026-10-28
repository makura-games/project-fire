using System.Linq;
using System.Threading.Tasks;
using Content.Shared._Scp.Procedural.PostGeneration;
using Content.Shared.Procedural;
using Robust.Shared.Random;

namespace Content.Server.Procedural.DungeonJob;

public sealed partial class DungeonJob
{
    /// <summary>
    /// Generates limited entities in the dungeon based on the specified parameters.
    /// <see cref="LimitedEntityDunGen"/>
    /// </summary>
    private async Task PostGen(LimitedEntityDunGen gen, Dungeon dungeon, Random random)
    {
        if (gen.Limit <= 0 || string.IsNullOrEmpty(gen.Entity))
            return;

        var checkedTiles = new HashSet<Vector2i>();
        var candidateTiles = new List<Vector2i>(dungeon.RoomTiles);
        random.Shuffle(candidateTiles);

        var entitiesSpawned = 0;
        foreach (var tile in candidateTiles.TakeWhile(_ => entitiesSpawned < gen.Limit).Where(tile => checkedTiles.Add(tile) && _anchorable.TileFree(_grid, tile, DungeonSystem.CollisionLayer, DungeonSystem.CollisionMask)))
        {
            if (TrySpawnEntity(tile, gen.Entity))
                entitiesSpawned++;

            if (entitiesSpawned % 10 == 0)
                await SuspendDungeon();
        }
    }

    private bool TrySpawnEntity(Vector2i tile, string entityPrototype)
    {
        var gridPos = _maps.GridTileToLocal(_gridUid, _grid, tile);
        var entityUid = _entManager.SpawnEntity(entityPrototype, gridPos);

        return entityUid != EntityUid.Invalid;
    }
}

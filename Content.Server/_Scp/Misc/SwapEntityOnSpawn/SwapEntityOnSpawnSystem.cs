using Robust.Shared.Random;

namespace Content.Server._Scp.Misc.SwapEntityOnSpawn;

public sealed partial class SwapEntityOnSpawnSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SwapEntityOnSpawnComponent, MapInitEvent>(OnSpawn);
    }

    /// <summary>
    /// Данный нереальный метод создан для того, чтобы с шансом появлялись какие-то другие ентити вместо данного
    /// Все для того, чтобы не трогать код рецептов у виздена и не впихивать туда шанс
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="args"></param>
    private void OnSpawn(Entity<SwapEntityOnSpawnComponent> entity, ref MapInitEvent args)
    {
        foreach (var replacing in entity.Comp.Replace)
        {
            if (!_random.Prob(entity.Comp.Chance))
                return;

            Spawn(replacing, Transform(entity).Coordinates);
            QueueDel(entity);
        }
    }
}

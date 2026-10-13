using Content.Shared.Chemistry.EntitySystems;
using Robust.Shared.Random;

namespace Content.Server._Scp.Research.SwapReagentOnSpawn;

public sealed partial class SwapReagentOnSpawnSystem : EntitySystem
{
    [Dependency] private SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SwapReagentOnSpawnComponent, MapInitEvent>(OnSpawn);
    }

    private void OnSpawn(Entity<SwapReagentOnSpawnComponent> entity, ref MapInitEvent args)
    {
        foreach (var (replaceable, replacing) in entity.Comp.Replace)
        {
            if (!_random.Prob(entity.Comp.Chance))
                return;

            // TODO: Мои яйца щас отвалятся. Почему оно не может найти солюшен. Пофиксить
            if (!_solutionContainer.TryGetSolution(entity.Owner, "food", out var solution, out _))
                return;

            var cachedVolume = solution.Value.Comp.Solution.Volume;

            _solutionContainer.RemoveReagent(solution.Value, replaceable, cachedVolume);
            _solutionContainer.TryAddReagent(solution.Value, replacing, cachedVolume);
        }


    }
}

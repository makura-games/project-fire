using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.Map;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;
using System.Linq;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._Scp.Scp173;
using Content.Shared._Sunrise.Footprints;

namespace Content.Server.Chemistry.TileReactions;

/// <summary>
/// Turns all of the reagents on a puddle into water.
/// </summary>
[DataDefinition]
public sealed partial class CleanTileReaction : ITileReaction
{
    // Fire added start
    [DataField]
    public bool DoubleScp173Reagent = true;
    // Fire added end

    /// <summary>
    /// How much it costs to clean 1 unit of reagent.
    /// </summary>
    /// <remarks>
    /// In terms of space cleaner can clean 1 average puddle per 5 units.
    /// </remarks>
    [DataField("cleanCost")]
    public float CleanAmountMultiplier { get; private set; } = 0.25f;

    /// <summary>
    /// What reagent to replace the tile conents with.
    /// </summary>
    [DataField("reagent", customTypeSerializer: typeof(PrototypeIdSerializer<ReagentPrototype>))]
    public string ReplacementReagent = "Water";

    FixedPoint2 ITileReaction.TileReact(TileRef tile,
        ReagentPrototype reagent,
        FixedPoint2 reactVolume,
        IEntityManager entityManager,
        List<ReagentData>? data)
    {
        var entities = entityManager.System<EntityLookupSystem>().GetLocalEntitiesIntersecting(tile, 0f).ToArray();
        var puddleQuery = entityManager.GetEntityQuery<PuddleComponent>();
        var solutionContainerSystem = entityManager.System<SharedSolutionContainerSystem>();
        // Multiply as the amount we can actually purge is higher than the react amount.
        var purgeAmount = reactVolume / CleanAmountMultiplier;

        // Fire added start - для удваивания количества вещества 173 от чистящего реагента
        var puddleSystem = entityManager.System<PuddleSystem>();
        // Fire added end

        foreach (var entity in entities)
        {
            if (!puddleQuery.TryGetComponent(entity, out var puddle) ||
                !solutionContainerSystem.TryGetSolution(entity, puddle.SolutionName, out var puddleSolution, out var solution))
            {
                continue;
            }

            // Fire added start - для удваивания количества вещества 173 от чистящего реагента
            if (DoubleScp173Reagent && solution.TryGetReagent(new ReagentId(Scp173Component.Reagent, null), out var quantity))
            {
                var tempSol = new Solution();
                tempSol.AddReagent(Scp173Component.Reagent, quantity.Quantity);
                puddleSystem.TrySpillAt(tile, tempSol, out _, false);

                purgeAmount -= FixedPoint2.Min(purgeAmount, quantity.Quantity);

                continue;
            }
            // Fire added start

            var purgeable = solutionContainerSystem.SplitSolutionWithout(puddleSolution.Value, purgeAmount, ReplacementReagent, reagent.ID);

            purgeAmount -= purgeable.Volume;

            solutionContainerSystem.TryAddSolution(puddleSolution.Value, new Solution(ReplacementReagent, purgeable.Volume));

            if (purgeable.Volume <= FixedPoint2.Zero)
                break;
        }

        // Fire edit - тут санрайз насрал удалением следов, я это удалил.

        return (reactVolume / CleanAmountMultiplier - purgeAmount) * CleanAmountMultiplier;
    }
}

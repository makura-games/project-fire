/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Threading;
using System.Threading.Tasks;
using Robust.Shared.CPUJob.JobQueues;

namespace Content.Server._CE.ZCollapse;

/// <summary>
/// Time-sliced wrapper around <see cref="CEStabilityFloodFill"/> for one whole Z-column — every grid
/// bridged to every other via a chain of <see cref="CEGridStabilitySupportComponent"/>s, computed
/// together in a single pass rather than grid-by-grid. This matters for correctness, not just
/// convenience: if each grid were computed separately and capped its Support seeds against a
/// neighbor grid's *previously stored* result, two Supports facing each other across a boundary can
/// reach a stable mutual fixed point (grid A reads B's old value, B reads A's new value, neither ever
/// decreasing) and hold a whole section aloft with zero Cores anywhere. Doing the whole column as one
/// unified BFS makes that structurally impossible instead: a node can only ever get a value by being
/// reachable, within this one pass, from an actual Core seed.
///
/// Everything this needs is captured as a plain-data snapshot on the main thread before the job is
/// queued (see <see cref="CEZCollapseSystem"/>) — <see cref="Process"/> never touches
/// <c>IEntityManager</c> or any system, so it's safe to suspend and resume across many ticks even
/// while the live world keeps changing underneath it.
/// </summary>
public sealed class StabilityJob(
    double maxTime,
    HashSet<(EntityUid Grid, Vector2i Tile)> liveNodes,
    List<(EntityUid Grid, Vector2i Tile, int Value)> coreSeeds,
    Dictionary<(EntityUid, Vector2i), List<((EntityUid Grid, Vector2i Tile) Node, int Strength, int Loss)>> bridges,
    CancellationToken cancellation = default)
    : Job<Dictionary<(EntityUid Grid, Vector2i Tile), int>>(maxTime, cancellation)
{
    private const int BatchSize = 256;

    /// <summary>Every (grid, tile) that physically exists right now, across every grid in the column.</summary>
    private readonly HashSet<(EntityUid Grid, Vector2i Tile)> _liveNodes = liveNodes;

    public IReadOnlySet<(EntityUid Grid, Vector2i Tile)> LiveNodes => _liveNodes;

    protected override async Task<Dictionary<(EntityUid Grid, Vector2i Tile), int>?> Process()
    {
        var stability = new Dictionary<(EntityUid, Vector2i), int>();
        var queue = new Queue<((EntityUid Grid, Vector2i Tile) Node, int Value)>();

        CEStabilityFloodFill.SeedCores(stability, queue, _liveNodes, coreSeeds);

        while (CEStabilityFloodFill.Process(queue, stability, _liveNodes, bridges, BatchSize) > 0)
        {
            await SuspendIfOutOfTime();
        }

        return stability;
    }
}

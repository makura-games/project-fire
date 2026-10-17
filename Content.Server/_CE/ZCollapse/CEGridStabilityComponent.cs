/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Robust.Shared.Map;

namespace Content.Server._CE.ZCollapse;

/// <summary>
/// Opt-in marker for a grid/map that participates in the ZCollapse structural-stability system
/// (added via a station's <c>zLevelsComponentOverrides</c>). Only grids with this component can
/// grow a Core's stability field or collapse tiles — player shuttles never opt in.
///
/// Holds only ground-truth-derived state: the last computed <see cref="Stability"/> result, and a
/// live index of which Core/Support entities are currently anchored on this grid. Neither can go
/// stale independently of the world — <see cref="Cores"/>/<see cref="Supports"/> are just "what's
/// anchored here right now" (kept in sync 1:1 with anchor/unanchor), and <see cref="Stability"/> is
/// always fully replaced by <see cref="CEZCollapseSystem"/> from a fresh flood-fill, never patched
/// incrementally. There is deliberately no separate seed cache to desync.
/// </summary>
[RegisterComponent]
public sealed partial class CEGridStabilityComponent : Component
{
    /// <summary>
    /// Last computed stability per tile. A tile absent from this dictionary has stability &lt;= 0
    /// (dead/untouched) and is never colored by the debug overlay.
    /// </summary>
    [ViewVariables]
    public readonly Dictionary<Vector2i, int> Stability = new();

    /// <summary>Currently anchored <see cref="CEGridStabilityCoreComponent"/> entities on this grid.</summary>
    [ViewVariables]
    public readonly HashSet<EntityUid> Cores = new();

    /// <summary>Currently anchored <see cref="CEGridStabilitySupportComponent"/> entities on this grid.</summary>
    [ViewVariables]
    public readonly HashSet<EntityUid> Supports = new();

    /// <summary>
    /// Tiles on this grid found unsupported on some past recompute, waiting out their random collapse
    /// delay before <see cref="CEZCollapseSystem.CollapseTile"/> steps them down a turf layer. A tile
    /// that regains stability before its time is simply dropped from here — there's no other cached
    /// state to unwind, so cancelling a collapse is exactly as simple as scheduling one.
    /// </summary>
    [ViewVariables]
    public readonly Dictionary<Vector2i, TimeSpan> PendingCollapses = new();

    /// <summary>
    /// If true and this grid turns out to be the lowest Z-level in its network (see
    /// <see cref="Content.Shared._CE.ZLevels.Core.Components.CEZMapNetworkComponent.SortedMin"/>),
    /// every one of its live tiles is auto-seeded with <see cref="CEZCollapseSystem.LowestLevelSupportValue"/>
    /// stability, as if it stood on permanently solid ground — regardless of whether any real
    /// <see cref="CEGridStabilityCoreComponent"/> is anchored there. No-op for any other grid.
    /// </summary>
    [DataField]
    public bool SupportLowestLevel;
}

/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Robust.Shared.Serialization;

namespace Content.Shared._CE.ZCollapse.Events;

/// <summary>
/// Raised when the server enables/disables the tile-stability debug overlay for a client.
/// After enabling, the client starts receiving <see cref="CEZCollapseOverlaySnapshotEvent"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class CEZCollapseOverlayToggledEvent(bool isEnabled) : EntityEventArgs
{
    public readonly bool IsEnabled = isEnabled;
}

/// <summary>
/// Raised when per-tile stability data changed for one or more grids the client has opted into
/// viewing via the debug overlay.
/// </summary>
[Serializable, NetSerializable]
public sealed class CEZCollapseOverlaySnapshotEvent(Dictionary<NetEntity, Dictionary<Vector2i, int>> grids) : EntityEventArgs
{
    /// <summary>
    /// Key is grid uid. Value is stability per tile (absent tile = stability &lt;= 0 / uncolored).
    /// </summary>
    public readonly Dictionary<NetEntity, Dictionary<Vector2i, int>> Grids = grids;
}

/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

namespace Content.Server._CE.ZCollapse;

/// <summary>
/// Keeps a floating grid aloft: when anchored, seeds the tile it stands on with
/// <see cref="LevitationForce"/> stability, which then flood-fills outward across the grid.
/// </summary>
[RegisterComponent]
public sealed partial class CEGridStabilityCoreComponent : Component
{
    [DataField]
    public int LevitationForce = 20;
}

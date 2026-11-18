/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

namespace Content.Server._CE.ZCollapse;

/// <summary>
/// Bridges structural stability between the Z-level this entity is anchored on and the Z-level
/// directly above it. Whichever side currently has a live tile at this entity's position feeds
/// <see cref="SupportStrength"/> as a seed into the other side, minus <see cref="TransferLoss"/> —
/// this is what lets structures hang below a floating grid: place the support on the lower level,
/// bridging up into the grid above. The loss applies symmetrically in both directions: a Support
/// isn't a one-way conduit, it's a physical joint that costs some of whatever stability passes
/// through it either way.
/// </summary>
[RegisterComponent]
public sealed partial class CEGridStabilitySupportComponent : Component
{
    [DataField]
    public int SupportStrength = 7;

    /// <summary>How much stability this Support consumes off whatever it conducts, each direction. A support standing on 4 stability passes at most (4 - TransferLoss) up, and vice versa.</summary>
    [DataField]
    public int TransferLoss = 0;
}

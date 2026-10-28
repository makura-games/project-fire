/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Robust.Shared.GameStates;

namespace Content.Shared._CE.ZLevels.Chasm;

/// <summary>
/// Marks a Z-level map as having a lethal abyss below it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CEZLevelChasmComponent : Component;

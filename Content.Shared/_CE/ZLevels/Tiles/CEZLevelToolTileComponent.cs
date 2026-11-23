/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared.Tools.Components;
using Robust.Shared.GameStates;

namespace Content.Shared._CE.ZLevels.Tiles;

/// <summary>
/// Marker for entities that also have <see cref="ToolTileCompatibleComponent"/>, allowing them to
/// deconstruct the tile on the z-level directly above the wielder while <see cref="Content.Shared._CE.ZLevels.Core.Components.CEZLevelViewerComponent.LookUp"/>
/// is enabled. Reuses <see cref="ToolTileCompatibleComponent.Delay"/> and <see cref="ToolTileCompatibleComponent.RequiresUnobstructed"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(CEZLevelToolTileSystem))]
public sealed partial class CEZLevelToolTileComponent : Component;

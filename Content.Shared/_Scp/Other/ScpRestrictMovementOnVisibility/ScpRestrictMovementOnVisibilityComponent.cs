using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared._Scp.Other.ScpRestrictMovementOnVisibility;

[RegisterComponent, NetworkedComponent]
public sealed partial class ScpRestrictMovementOnVisibilityComponent : Component
{
    [DataField]
    public EntityWhitelist? ContainersMoveWhitelist;

    [DataField]
    public EntityWhitelist? ContainersMoveBlacklist;

    [DataField]
    public float ContainmentRoomSearchRadius = 8f;
}

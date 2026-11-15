using System.Numerics;

using Robust.Shared.GameStates;

namespace Content.Shared._Scp.Other.DirectionalOccluder;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(ScpDirectionalOccluderSystem))]
public sealed partial class ScpDirectionalOccluderComponent : Component
{
    [DataField]
    public Dictionary<Direction, Box2> BoundingBoxes = new();

    [AutoNetworkedField, ViewVariables]
    public Box2? DefaultBoundingBox;

    [AutoNetworkedField, ViewVariables]
    public Vector2[]? DefaultPolygon;
}

using System.Numerics;
using Robust.Shared.Map;

namespace Content.Shared._Scp.ZLevels;

/// <summary>
/// Geometry for a shot's single transition to an adjacent floor.
/// Distances are measured along the shot direction from its original position.
/// </summary>
public readonly record struct ZLevelShotPlan(
    EntityUid SourceMap,
    EntityUid TargetMap,
    MapId TargetMapId,
    int Offset,
    Vector2 Transition,
    float TransitionDistance,
    float MaxDistance);

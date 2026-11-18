using System.Numerics;
using Content.Shared._Scp.ZLevels;

namespace Content.Server._Scp.ZLevels;

/// <summary>
/// Tracks a fired projectile's single transition between adjacent floors.
/// </summary>
[RegisterComponent]
public sealed partial class ZLevelProjectileComponent : Component
{
    /// <summary>Transition geometry and maximum travel distance.</summary>
    public ZLevelShotPlan Plan;

    /// <summary>Initial world position of the projectile.</summary>
    public Vector2 Origin;

    /// <summary>Actual direction, including spread and shooter velocity.</summary>
    public Vector2 Direction;

    /// <summary>Position at the end of the previous physics step.</summary>
    public Vector2 PreviousPosition;

    /// <summary>Travelled distance, including the source-floor segment.</summary>
    public float DistanceTravelled;

    /// <summary>Whether the single allowed transition has been completed.</summary>
    public bool Transitioned;

    /// <summary>Whether the projectile awaits source-floor contacts at the ledge.</summary>
    public bool AtEdge;

    /// <summary>Whether the last allowed segment awaits contacts before expiry.</summary>
    public bool AtLimit;
}

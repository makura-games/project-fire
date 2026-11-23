namespace Content.Shared._Scp.ZLevels;

/// <summary>
/// Preserves a cross-floor ray's route and remaining range across reflections.
/// </summary>
[RegisterComponent]
public sealed partial class ZLevelHitscanComponent : Component
{
    /// <summary>
    /// The single transition calculated for the initial shot direction.
    /// </summary>
    public ZLevelShotPlan Plan;

    /// <summary>
    /// Whether the initial transition has been checked; reflections stay on their current floor.
    /// </summary>
    public bool TransitionHandled;

    /// <summary>
    /// Remaining balance range from Plan; downward shots have no additional range cap.
    /// </summary>
    public float? RemainingDistance;
}

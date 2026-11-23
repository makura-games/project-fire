using Robust.Shared.GameStates;

namespace Content.Shared._CE.ZLevels.Damage.SoftLanding;

/// <summary>
/// Reduces fall damage and removes stun if the fall speed does not exceed a certain limit.
/// Lives on a status effect entity granted by the <c>SoftLanding</c> skill - see
/// <see cref="CESoftLandingStatusEffectSystem"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CESoftLandingStatusEffectComponent : Component
{
    /// <summary>
    /// The fall speed must be less than this for damage reduction and stun to start working.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float MaxSpeedLimit = 1f;

    [DataField, AutoNetworkedField]
    public float DamageMultiplier = 0f;

    [DataField, AutoNetworkedField]
    public float StunMultiplier = 0f;

    [DataField, AutoNetworkedField]
    public float DamageHardFallMultiplier = 0.5f;

    [DataField, AutoNetworkedField]
    public float StunHardFallMultiplier = 0.5f;
}

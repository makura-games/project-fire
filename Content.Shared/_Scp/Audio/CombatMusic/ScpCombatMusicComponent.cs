using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Audio.CombatMusic;

/// <summary>
/// Stores the body's music group, including after an SCP-035 takeover or an SCP-049 conversion.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ScpCombatMusicComponent : Component
{
    /// <summary>
    /// Server-selected group. Null uses fallback music for an unclassified body.
    /// </summary>
    [AutoNetworkedField]
    public ProtoId<ScpCombatMusicPrototype>? Music;
}


using Content.Shared.Actions.Components;
using Content.Shared.Whitelist;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Other.ScpBlind;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ScpBlindComponent : Component
{
    [DataField]
    public EntProtoId<ActionComponent> ActionProto = "Scp173Blind";

    /// <summary>
    /// Время, через которое начнется ослепление после активации способности
    /// </summary>
    [DataField]
    public TimeSpan StartBlindTime = TimeSpan.FromSeconds(12f);

    /// <summary>
    /// Время ослепления после успешного применения способности
    /// </summary>
    [DataField]
    public TimeSpan BlindnessTime = TimeSpan.FromSeconds(7);

    [DataField]
    public float? MinWatchersToBlind;

    [DataField]
    public float SearchAllowerRadius = 8f;

    [DataField]
    public float SearchBlockerRadius = 8f;

    [DataField]
    public bool MustBeAllowedToBlind = false;

    [DataField]
    public bool IgnoreBlockers = false;

    [DataField]
    public EntityWhitelist? InContainersBlindWhitelist;

    [DataField]
    public EntityWhitelist? InContainersBlindBlacklist;

    [ViewVariables, AutoNetworkedField]
    public EntityUid? ActionEnt;
}

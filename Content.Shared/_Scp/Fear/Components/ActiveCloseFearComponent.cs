using Robust.Shared.GameStates;

namespace Content.Shared._Scp.Fear.Components;

/// <summary>
/// Runtime-компонент активного "страха от близости".
/// Существует только пока у сущности реально активны close-fear эффекты.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ActiveCloseFearComponent : Component
{
    /// <summary>
    /// Источник страха, который сейчас применяется к сущности.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid Source;

    /// <summary>
    /// Whether this active proximity effect comes from another Z level.
    /// </summary>
    [AutoNetworkedField]
    public bool AcrossZLevels;

    /// <summary>
    /// Strength of the active source's non-audio effects.
    /// </summary>
    [AutoNetworkedField]
    public float EffectStrength = 1f;
}

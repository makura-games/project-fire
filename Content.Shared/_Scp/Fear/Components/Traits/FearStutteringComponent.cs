using Robust.Shared.GameStates;

namespace Content.Shared._Scp.Fear.Components.Traits;

[RegisterComponent, NetworkedComponent]
public sealed partial class FearStutteringComponent : Component
{
    [DataField, ViewVariables]
    public FearState RequiredState = FearState.Anxiety;

    /// <summary>
    /// Коэффициент силы страха, уже применённый к вероятностям.
    /// </summary>
    public float AppliedStrength = 1f;
}

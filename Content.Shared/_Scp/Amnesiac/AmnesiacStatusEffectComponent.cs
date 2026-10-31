using Content.Shared.Chemistry.Reagent;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Amnesiac;

[RegisterComponent, NetworkedComponent]
public sealed partial class AmnesiacStatusEffectComponent : Component
{
    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Reagent;

    [DataField(required: true)]
    public LocId ErasedMemory;

    [DataField]
    public TimeSpan ConfirmDelay = TimeSpan.FromSeconds(5);

    [ViewVariables]
    public TimeSpan ConfirmAvailableAt;
}

using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Scp.Amnesiac;

[Serializable, NetSerializable]
public sealed class AmnesiacInfoEvent(ProtoId<ReagentPrototype> reagent, LocId erasedMemory, TimeSpan confirmDelay) : EntityEventArgs
{
    public ProtoId<ReagentPrototype> Reagent { get; } = reagent;

    public LocId ErasedMemory { get; } = erasedMemory;

    public TimeSpan ConfirmDelay { get; } = confirmDelay;
}

[Serializable, NetSerializable]
public sealed class AmnesiacConfirmEvent : EntityEventArgs;

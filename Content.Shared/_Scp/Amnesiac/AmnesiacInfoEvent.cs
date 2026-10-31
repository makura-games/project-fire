using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Scp.Amnesiac;

[Serializable, NetSerializable]
public sealed class AmnesiacInfoEvent(NetEntity effect, ProtoId<ReagentPrototype> reagent, LocId erasedMemory, TimeSpan confirmDelay) : EntityEventArgs
{
    public NetEntity Effect { get; } = effect;

    public ProtoId<ReagentPrototype> Reagent { get; } = reagent;

    public LocId ErasedMemory { get; } = erasedMemory;

    public TimeSpan ConfirmDelay { get; } = confirmDelay;
}

[Serializable, NetSerializable]
public sealed class AmnesiacConfirmEvent(NetEntity effect) : EntityEventArgs
{
    public NetEntity Effect { get; } = effect;
}

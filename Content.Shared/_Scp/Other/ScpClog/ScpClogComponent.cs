using Content.Shared.Chemistry.Reagent;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Other.ScpClog;

[RegisterComponent, NetworkedComponent]
public sealed partial class ScpClogComponent : Component
{
    [DataField]
    public ProtoId<ReagentPrototype> Reagent = "Scp173Reagent";

    [DataField]
    public SoundPathSpecifier ClogSound = new("/Audio/_Scp/Scp173/clog.ogg");

    [DataField]
    public float ClogDeconstructEffectRadius = 8f;

    [DataField]
    public EntityWhitelist? InContainersClogWhitelist;

    [DataField]
    public EntityWhitelist? InContainersClogBlacklist;

    /// <summary>
    /// Количество реагента, которое необходимо накопить вокруг, засорение открывало шлюзы вокруг.
    /// </summary>
    [DataField]
    public int MinTotalSolutionVolume = 600;

    /// <summary>
    /// Количество реагента, которое необходимо накопить вокруг, чтобы начать взрываться при засорении
    /// </summary>
    [DataField]
    public int ExtraMinTotalSolutionVolume = 900;
}

using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Scp208;

[RegisterComponent, NetworkedComponent]
public sealed partial class Scp208Component : Component
{
    [DataField]
    public EntProtoId ActionHealProtoId = "Scp208Heal";

    [DataField(required: true)]
    public DamageSpecifier Damage = default!;

    [DataField]
    public bool StopBleeding = true;

    [DataField]
    public float BloodlossModifier = -1.0f;

    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(3f);

    [DataField]
    public SoundSpecifier? HealingBeginSound;

    [DataField]
    public SoundSpecifier? HealingEndSound;

    [ViewVariables]
    public EntityUid? HealActionEnt;
}

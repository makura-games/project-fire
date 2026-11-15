using Content.Shared.EntityConditions;
using Content.Shared.Storage;
using Robust.Shared.Audio;

namespace Content.Server._Scp.GameTicking.Rules.SpawnInPlayerInventory;

[RegisterComponent]
public sealed partial class SpawnInPlayerInventoryRuleComponent : Component
{
    [DataField(required: true)]
    public float Probability;

    [DataField(required: true)]
    public List<EntitySpawnEntry> Entities = default!;

    [DataField]
    public SoundSpecifier? Sound;

    [DataField]
    public EntityCondition[] StationConditions = [];
}

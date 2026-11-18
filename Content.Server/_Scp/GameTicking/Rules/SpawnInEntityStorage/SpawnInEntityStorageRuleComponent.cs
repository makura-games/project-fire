using Content.Shared.EntityConditions;
using Content.Shared.Storage;

namespace Content.Server._Scp.GameTicking.Rules.SpawnInEntityStorage;

[RegisterComponent]
public sealed partial class SpawnInEntityStorageRuleComponent : Component
{
    [DataField(required: true)]
    public float Probability;

    [DataField(required: true)]
    public List<EntitySpawnEntry> Entities = default!;

    [DataField]
    public bool DoOpenCloseAnimation = true;

    [DataField]
    public TimeSpan CloseAfter = TimeSpan.FromSeconds(1f);

    [DataField]
    public TimeSpan CloseAfterVariation = TimeSpan.FromSeconds(0.5f);

    [DataField]
    public EntityCondition[] StationConditions = [];
}

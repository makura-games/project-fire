using Content.Shared.AlertLevel;
using Robust.Shared.Prototypes;

namespace Content.Server._Scp.GameTicking.Rules.Components;

[RegisterComponent]
public sealed partial class Scp106AscentRuleComponent : Component
{
    [DataField]
    public EntProtoId SpawnPortalsRule;

    [ViewVariables] public EntityUid TargetStation;
    [ViewVariables] public ProtoId<AlertLevelPrototype> CachedAlertLevel;
}

using Content.Server._Scp.Objectives.Components;
using Content.Server.Objectives.Systems;
using Content.Server.Shuttles.Systems;
using Content.Shared.CCVar;
using Content.Shared.Mind;
using Content.Shared.Objectives.Components;
using Robust.Shared.Configuration;

namespace Content.Server._Scp.Objectives.Systems;

public sealed partial class ScpEscapeTargetConditionSystem : EntitySystem
{
    [Dependency] private EmergencyShuttleSystem _emergencyShuttle = default!;
    [Dependency] private TargetObjectiveSystem _target = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private SharedMindSystem _mind = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpEscapeTargetConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);
    }

    private void OnGetProgress(Entity<ScpEscapeTargetConditionComponent> ent, ref ObjectiveGetProgressEvent args)
    {
        if (!_target.GetTarget(ent, out var target) ||
            !TryComp<MindComponent>(target, out var mind) ||
            mind.OwnedEntity == null ||
            _mind.IsCharacterDeadIc(mind))
        {
            args.Progress = 0f;
            return;
        }

        if (!_config.GetCVar(CCVars.EmergencyShuttleEnabled))
        {
            args.Progress = 0f;
            return;
        }

        var targetEscaping = _emergencyShuttle.IsTargetEscaping(mind.OwnedEntity.Value);
        if (_emergencyShuttle.ShuttlesLeft)
            args.Progress = targetEscaping ? 1f : 0f;
        else
            args.Progress = targetEscaping ? 0.5f : 0f;
    }
}

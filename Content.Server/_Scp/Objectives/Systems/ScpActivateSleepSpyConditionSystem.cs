using Content.Server._Scp.Objectives.Components;
using Content.Server.Mind;
using Content.Server.Objectives.Components;
using Content.Server.Objectives.Systems;
using Content.Shared._Scp.Chaos;
using Content.Shared.Mobs.Systems;
using Content.Shared.Objectives.Components;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Random;

namespace Content.Server._Scp.Objectives.Systems;

public sealed partial class ScpActivateSleepSpyConditionSystem : EntitySystem
{
    [Dependency] private CodeConditionSystem _condition = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedJobSystem _job = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpActivateSleepSpyConditionComponent, ObjectiveAssignedEvent>(OnAssigned);
        SubscribeLocalEvent<ScpActivateSleepSpyConditionComponent, ObjectiveAfterAssignEvent>(OnAfterAssign);
        SubscribeLocalEvent<ScpActivateSleepSpyConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress, after: [typeof(CodeConditionSystem)]);
    }

    private void OnAssigned(Entity<ScpActivateSleepSpyConditionComponent> ent, ref ObjectiveAssignedEvent args)
    {
        var candidates = new HashSet<EntityUid>();

        var query = EntityQueryEnumerator<ChaosSleepSpyMobComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.IsAssigned)
               continue;

            if (!_mobState.IsAlive(uid))
                continue;

            candidates.Add(uid);
        }


        if (candidates.Count >= 1)
        {
            ent.Comp.Target = _random.Pick(candidates);
            TryComp<ChaosSleepSpyMobComponent>(ent.Comp.Target.Value, out var sleepSpyComp);
            if(sleepSpyComp != null)
                sleepSpyComp.IsAssigned = true;
        }
        else
            args.Cancelled = true;

    }

    private void OnAfterAssign(Entity<ScpActivateSleepSpyConditionComponent> ent, ref ObjectiveAfterAssignEvent args)
    {

        if (ent.Comp.Target is not { } target)
            return;

        if (!_mind.TryGetMind(target, out var mindId, out _))
            return;

        if (!_job.MindTryGetJob(mindId, out var jobPrototype))
            return;

        if (!_job.TryGetDepartment(jobPrototype.ID, out var departmentPrototype))
            return;

        _metaData.SetEntityName(ent, Loc.GetString("objective-condition-chaos-spy-activate-sleep-spy-title"), args.Meta);
        _metaData.SetEntityDescription(ent, Loc.GetString("objective-condition-chaos-spy-activate-sleep-spy-description", ("department", Loc.GetString(departmentPrototype.Name))), args.Meta);
    }

    private void OnGetProgress(Entity<ScpActivateSleepSpyConditionComponent> ent, ref ObjectiveGetProgressEvent args)
    {
        if (TryComp<CodeConditionComponent>(ent, out var code) && code.Completed)
        {
            args.Progress = 1f;
            return;
        }

        if (HasComp<ChaosSpyMobComponent>(ent.Comp.Target))
        {
            _condition.SetCompleted((ent.Owner, code), true);
            args.Progress = 1f;
            return;
        }

        args.Progress = 0f;
    }
}

using Content.Server._Scp.Objectives.Components;
using Content.Server._Scp.Scp173;
using Content.Server.Objectives.Components;
using Content.Server.Objectives.Systems;
using Content.Shared._Scp.Mobs.Components;
using Content.Shared._Scp.Scp106.Components;
using Content.Shared._Scp.Scp173;
using Content.Shared.Objectives.Components;
using Robust.Shared.Random;

namespace Content.Server._Scp.Objectives.Systems;

public sealed partial class ScpBreachScpConditionSystem : EntitySystem
{
    [Dependency] private CodeConditionSystem _condition = default!;
    [Dependency] private Scp173System _scp173 = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpBreachScpConditionComponent, ObjectiveAssignedEvent>(OnAssigned);
        SubscribeLocalEvent<ScpBreachScpConditionComponent, ObjectiveAfterAssignEvent>(OnAfterAssign);
        SubscribeLocalEvent<ScpBreachScpConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress, after: [typeof(CodeConditionSystem)]);
    }

    private void OnAssigned(Entity<ScpBreachScpConditionComponent> ent, ref ObjectiveAssignedEvent args)
    {
        var candidates = GetSupportedTargets(ent.Owner);
        if (candidates.Count == 0)
        {
            args.Cancelled = true;
            return;
        }

        ent.Comp.Target = _random.Pick(candidates);
    }

    private void OnAfterAssign(Entity<ScpBreachScpConditionComponent> ent, ref ObjectiveAfterAssignEvent args)
    {
        if (ent.Comp.Target is not { } target || Deleted(target) || !TryComp<ScpComponent>(target, out var scpComp))
            return;

        var targetName = Name(target);
        if (scpComp.Name != null)
            targetName = scpComp.Name;

        _metaData.SetEntityName(ent, Loc.GetString("objective-condition-chaos-spy-breach-title", ("targetName", targetName)), args.Meta);
        _metaData.SetEntityDescription(ent, Loc.GetString("objective-condition-chaos-spy-breach-description", ("targetName", targetName)), args.Meta);
    }

    private void OnGetProgress(Entity<ScpBreachScpConditionComponent> ent, ref ObjectiveGetProgressEvent args)
    {
        if (TryComp<CodeConditionComponent>(ent, out var code) && code.Completed)
        {
            args.Progress = 1f;
            return;
        }

        if (ent.Comp.Target is not { } target || Deleted(target))
        {
            args.Progress = 0f;
            return;
        }

        if (IsBreached(target))
        {
            _condition.SetCompleted((ent.Owner, code), true);
            args.Progress = 1f;
            return;
        }

        args.Progress = 0f;
    }
    private List<EntityUid> GetSupportedTargets(EntityUid currentObjective)
    {
        var usedTargets = new HashSet<EntityUid>();
        var objectiveQuery = EntityQueryEnumerator<ScpBreachScpConditionComponent>();
        while (objectiveQuery.MoveNext(out var uid, out var objective))
        {
            if (uid == currentObjective || objective.Target == null || Deleted(objective.Target.Value))
                continue;

            usedTargets.Add(objective.Target.Value);
        }

        var candidates = new List<EntityUid>();

        var scp173Query = EntityQueryEnumerator<Scp173Component>();
        while (scp173Query.MoveNext(out var uid, out _))
        {
            if (!usedTargets.Contains(uid))
                candidates.Add(uid);
        }

        var scp106Query = EntityQueryEnumerator<Scp106Component>();
        while (scp106Query.MoveNext(out var uid, out _))
        {
            if (!usedTargets.Contains(uid))
                candidates.Add(uid);
        }

        return candidates;
    }

    private bool IsBreached(EntityUid uid)
    {
        if (TryComp<Scp173Component>(uid, out _))
            return !_scp173.IsContained(uid);

        if (TryComp<Scp106Component>(uid, out var scp106))
            return !scp106.IsContained;

        return false;
    }
}

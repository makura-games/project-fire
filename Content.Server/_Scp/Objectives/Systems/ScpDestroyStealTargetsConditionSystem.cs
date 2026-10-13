using Content.Server._Scp.Objectives.Components;
using Content.Shared.Objectives.Components;
using Content.Shared.Objectives.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Scp.Objectives.Systems;

public sealed partial class ScpDestroyStealTargetsConditionSystem : EntitySystem
{
    [Dependency] private SharedObjectivesSystem _objectives = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MetaDataSystem _metaData = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpDestroyStealTargetsConditionComponent, ObjectiveAssignedEvent>(OnAssigned);
        SubscribeLocalEvent<ScpDestroyStealTargetsConditionComponent, ObjectiveAfterAssignEvent>(OnAfterAssign);
        SubscribeLocalEvent<ScpDestroyStealTargetsConditionComponent, ObjectiveGetProgressEvent>(OnGetProgress);
    }

    private void OnAssigned(Entity<ScpDestroyStealTargetsConditionComponent> ent, ref ObjectiveAssignedEvent args)
    {
        ent.Comp.Targets.Clear();

        var query = AllEntityQuery<StealTargetComponent>();
        while (query.MoveNext(out var uid, out var target))
        {
            if (target.StealGroup != ent.Comp.StealGroup)
                continue;

            ent.Comp.Targets.Add(uid);
        }

        if (ent.Comp.Targets.Count == 0)
        {
            args.Cancelled = true;
            return;
        }

        _random.Shuffle(ent.Comp.Targets);

        var minSize = Math.Min(ent.Comp.Targets.Count, ent.Comp.MinCollectionSize);
        var maxSize = Math.Min(ent.Comp.Targets.Count, ent.Comp.MaxCollectionSize);
        ent.Comp.CollectionSize = _random.Next(minSize, maxSize + 1);

        if (ent.Comp.Targets.Count > ent.Comp.CollectionSize)
            ent.Comp.Targets.RemoveRange(ent.Comp.CollectionSize, ent.Comp.Targets.Count - ent.Comp.CollectionSize);
    }

    private void OnAfterAssign(Entity<ScpDestroyStealTargetsConditionComponent> ent, ref ObjectiveAfterAssignEvent args)
    {
        var group = _proto.Index(ent.Comp.StealGroup);
        var localizedName = Loc.GetString(group.Name);

        _metaData.SetEntityName(ent, Loc.GetString(ent.Comp.ObjectiveText, ("itemName", localizedName), ("count", ent.Comp.CollectionSize)), args.Meta);
        _metaData.SetEntityDescription(ent, Loc.GetString(ent.Comp.DescriptionText, ("itemName", localizedName), ("count", ent.Comp.CollectionSize)), args.Meta);
        _objectives.SetIcon(ent, group.Sprite ?? ent.Comp.Icon, args.Objective);
    }

    private void OnGetProgress(Entity<ScpDestroyStealTargetsConditionComponent> ent, ref ObjectiveGetProgressEvent args)
    {
        if (ent.Comp.CollectionSize <= 0)
        {
            args.Progress = 0f;
            return;
        }

        var destroyed = 0;
        foreach (var target in ent.Comp.Targets)
        {
            if (!Exists(target) || Deleted(target))
                destroyed++;
        }

        args.Progress = Math.Clamp(destroyed / (float)ent.Comp.CollectionSize, 0f, 1f);
    }
}

namespace Content.Shared._Scp.Other.DirectionalOccluder;

public sealed partial class ScpDirectionalOccluderSystem : EntitySystem
{
    [Dependency] private OccluderSystem _occluder = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpDirectionalOccluderComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ScpDirectionalOccluderComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ScpDirectionalOccluderComponent, MoveEvent>(OnMove);
    }

    private void OnMapInit(Entity<ScpDirectionalOccluderComponent> ent, ref MapInitEvent args)
    {
        RefreshBoundingBox(ent);
    }

    private void OnShutdown(Entity<ScpDirectionalOccluderComponent> ent, ref ComponentShutdown args)
    {
        if (Terminating(ent)
            || ent.Comp.DefaultBoundingBox is not { } defaultBoundingBox
            || !TryComp<OccluderComponent>(ent, out var occluder))
        {
            return;
        }

        var currentBox = occluder.BoundingBox;
        if (currentBox.EqualsApprox(defaultBoundingBox))
            return;

        _occluder.SetBoundingBox(ent.Owner, defaultBoundingBox);
    }

    private void OnMove(Entity<ScpDirectionalOccluderComponent> ent, ref MoveEvent args)
    {
        if (args.OldRotation.GetCardinalDir() == args.NewRotation.GetCardinalDir())
            return;

        RefreshBoundingBox(ent);
    }

    private void RefreshBoundingBox(
        Entity<ScpDirectionalOccluderComponent> ent,
        OccluderComponent? occluder = null,
        TransformComponent? xform = null)
    {
        if (!Resolve(ent.Owner, ref occluder, ref xform, false))
            return;

        ent.Comp.DefaultBoundingBox ??= occluder.BoundingBox;
        Dirty(ent);

        var currentBox = occluder.BoundingBox;
        var box = GetBoundingBox(ent.Comp, xform.LocalRotation.GetCardinalDir());
        if (currentBox.EqualsApprox(box))
            return;

        _occluder.SetBoundingBox(ent.Owner, box, occluder);
    }

    private static Box2 GetBoundingBox(ScpDirectionalOccluderComponent component, Direction direction)
    {
        if (component.BoundingBoxes.TryGetValue(direction, out var box))
            return box;

        return component.DefaultBoundingBox ?? Box2.UnitCentered;
    }
}

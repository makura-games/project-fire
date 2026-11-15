using System.Numerics;

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
        RefreshPolygon(ent);
    }

    private void OnShutdown(Entity<ScpDirectionalOccluderComponent> ent, ref ComponentShutdown args)
    {
        if (Terminating(ent)
            || ent.Comp.DefaultPolygon is not { } defaultPolygon
            || !TryComp<OccluderComponent>(ent, out var occluder))
        {
            return;
        }

        if (PolygonsEqual(occluder.Polygon, defaultPolygon))
            return;

        _occluder.SetPolygon(ent.Owner, defaultPolygon, occluder);
    }

    private void OnMove(Entity<ScpDirectionalOccluderComponent> ent, ref MoveEvent args)
    {
        if (args.OldRotation.GetCardinalDir() == args.NewRotation.GetCardinalDir())
            return;

        RefreshPolygon(ent);
    }

    private void RefreshPolygon(
        Entity<ScpDirectionalOccluderComponent> ent,
        OccluderComponent? occluder = null,
        TransformComponent? xform = null)
    {
        if (!Resolve(ent.Owner, ref occluder, ref xform, false))
            return;

        if (ent.Comp.DefaultPolygon == null)
        {
            ent.Comp.DefaultPolygon = occluder.Polygon.ToArray();
            ent.Comp.DefaultBoundingBox ??= GetBoundingBox(ent.Comp.DefaultPolygon);
            Dirty(ent);
        }

        var box = GetBoundingBox(ent.Comp, xform.LocalRotation.GetCardinalDir());
        var polygon = GetPolygon(box);
        if (PolygonsEqual(occluder.Polygon, polygon))
            return;

        _occluder.SetPolygon(ent.Owner, polygon, occluder);
    }

    private static Box2 GetBoundingBox(ScpDirectionalOccluderComponent component, Direction direction)
    {
        if (component.BoundingBoxes.TryGetValue(direction, out var box))
            return box;

        return component.DefaultBoundingBox ?? Box2.UnitCentered;
    }

    private static Vector2[] GetPolygon(Box2 box)
    {
        return
        [
            box.TopLeft,
            box.TopRight,
            box.BottomRight,
            box.BottomLeft,
        ];
    }

    private static Box2 GetBoundingBox(ReadOnlySpan<Vector2> polygon)
    {
        if (polygon.Length == 0)
            return Box2.UnitCentered;

        var box = new Box2(polygon[0], polygon[0]);
        for (var i = 1; i < polygon.Length; i++)
        {
            box = box.ExtendToContain(polygon[i]);
        }

        return box;
    }

    private static bool PolygonsEqual(ReadOnlySpan<Vector2> first, ReadOnlySpan<Vector2> second)
    {
        if (first.Length != second.Length)
            return false;

        for (var i = 0; i < first.Length; i++)
        {
            if (first[i] != second[i])
                return false;
        }

        return true;
    }
}

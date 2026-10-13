using System.Numerics;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._Scp.Other.DirectionalFixture;

public sealed partial class ScpDirectionalFixtureSystem : EntitySystem
{
    [Dependency] private FixtureSystem _fixtures = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpDirectionalFixtureComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ScpDirectionalFixtureComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ScpDirectionalFixtureComponent, MoveEvent>(OnMove);
    }

    private void OnMapInit(Entity<ScpDirectionalFixtureComponent> ent, ref MapInitEvent args)
    {
        RefreshFixture(ent);
    }

    private void OnShutdown(Entity<ScpDirectionalFixtureComponent> ent, ref ComponentShutdown args)
    {
        if (Terminating(ent)
            || ent.Comp.DefaultBoundingBox is not { } defaultBoundingBox
            || !TryComp<FixturesComponent>(ent, out var fixtures))
        {
            return;
        }

        var fixture = _fixtures.GetFixtureOrNull(ent, ent.Comp.FixtureId, fixtures);
        if (fixture == null || !TryGetBoundingBox(fixture.Shape, out var currentBox) || currentBox.EqualsApprox(defaultBoundingBox))
            return;

        ApplyBoundingBox(ent, ent.Comp.FixtureId, fixture, defaultBoundingBox, fixtures);
    }

    private void OnMove(Entity<ScpDirectionalFixtureComponent> ent, ref MoveEvent args)
    {
        if (args.OldRotation.GetCardinalDir() == args.NewRotation.GetCardinalDir())
            return;

        RefreshFixture(ent);
    }

    private void RefreshFixture(
        Entity<ScpDirectionalFixtureComponent> ent,
        FixturesComponent? fixtures = null,
        TransformComponent? xform = null)
    {
        if (!Resolve(ent, ref fixtures, ref xform, false))
            return;

        var fixture = _fixtures.GetFixtureOrNull(ent, ent.Comp.FixtureId, fixtures);
        if (fixture == null || !TryGetBoundingBox(fixture.Shape, out var currentBox))
            return;

        if (ent.Comp.DefaultBoundingBox == null)
        {
            ent.Comp.DefaultBoundingBox = currentBox;
            Dirty(ent);
        }

        var box = GetBoundingBox(ent.Comp, xform.LocalRotation.GetCardinalDir());
        if (currentBox.EqualsApprox(box))
            return;

        ApplyBoundingBox(ent, ent.Comp.FixtureId, fixture, box, fixtures, xform: xform);
    }

    private void ApplyBoundingBox(
        EntityUid uid,
        string fixtureId,
        Fixture fixture,
        Box2 box,
        FixturesComponent? fixtures = null,
        PhysicsComponent? body = null,
        TransformComponent? xform = null)
    {
        if (!Resolve(uid, ref fixtures, ref body, ref xform, false))
            return;

        if (fixture.Shape is PolygonShape poly)
        {
            _physics.SetVertices(uid, fixtureId, fixture, poly, GetVertices(box), fixtures, body, xform);
            return;
        }

        _fixtures.DestroyFixture(uid, fixtureId, false, body, fixtures, xform);

        var replacement = new PolygonShape();
        replacement.SetAsBox(box);

        if (_fixtures.TryCreateFixture(uid,
                replacement,
                fixtureId,
                fixture.Density,
                fixture.Hard,
                fixture.CollisionLayer,
                fixture.CollisionMask,
                fixture.Friction,
                fixture.Restitution,
                true,
                fixtures,
                body,
                xform))
        {
            return;
        }

        Log.Error($"Failed to recreate directional fixture '{fixtureId}' for {ToPrettyString(uid)}.");
    }

    private static Box2 GetBoundingBox(ScpDirectionalFixtureComponent component, Direction direction)
    {
        if (component.BoundingBoxes.TryGetValue(direction, out var box))
            return box;

        return component.DefaultBoundingBox ?? Box2.UnitCentered;
    }

    private static bool TryGetBoundingBox(IPhysShape shape, out Box2 box)
    {
        switch (shape)
        {
            case PhysShapeAabb aabb:
                box = aabb.LocalBounds;
                return true;
            case PolygonShape polygon when polygon.VertexCount > 0:
                box = GetBoundingBox(polygon.Vertices);
                return true;
            default:
                box = default;
                return false;
        }
    }

    private static Box2 GetBoundingBox(Vector2[] vertices)
    {
        var min = vertices[0];
        var max = vertices[0];

        for (var i = 1; i < vertices.Length; i++)
        {
            min = Vector2.Min(min, vertices[i]);
            max = Vector2.Max(max, vertices[i]);
        }

        return new Box2(min, max);
    }

    private static Vector2[] GetVertices(Box2 box)
    {
        return
        [
            box.BottomLeft,
            box.BottomRight,
            box.TopRight,
            box.TopLeft,
        ];
    }
}

using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Systems.Actions;
using Content.Shared._Scp.Scp173;
using Content.Shared.Actions.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Client._Scp.Scp173;

public sealed partial class Scp173Overlay : Overlay
{
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IEntityManager _entity = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IInputManager _inputManager = default!;
    [Dependency] private IEyeManager _eyeManager = default!;

    private readonly SharedTransformSystem _transform;
    private readonly ActionUIController _controller;
    private readonly SharedPhysicsSystem _physics;
    private readonly SharedChargesSystem _charges;
    private readonly Scp173System _scp173;

    private readonly EntityQuery<MobStateComponent> _mobQuery;
    private readonly EntityQuery<Scp173Component> _scp173Query;
    private readonly EntityQuery<ActionComponent> _actionQuery;

    public Scp173Overlay(SharedTransformSystem transform,
        ActionUIController controller,
        SharedPhysicsSystem physics,
        SharedChargesSystem charges,
        Scp173System scp173)
    {
        IoCManager.InjectDependencies(this);

        _transform = transform;
        _controller = controller;
        _physics = physics;
        _charges = charges;
        _scp173 = scp173;

        _mobQuery = _entity.GetEntityQuery<MobStateComponent>();
        _scp173Query = _entity.GetEntityQuery<Scp173Component>();
        _actionQuery = _entity.GetEntityQuery<ActionComponent>();
    }

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    protected override void Draw(in OverlayDrawArgs args)
    {
        var playerEntity = _playerManager.LocalEntity;
        if (playerEntity == null)
            return;

        if (_controller.SelectingTargetFor is not { } actionId)
            return;

        if (!_actionQuery.TryGetComponent(actionId, out var action))
            return;

        if (!action.Enabled)
            return;

        if (_charges.IsEmpty(actionId))
            return;

        if (action.Cooldown.HasValue && action.Cooldown.Value.End > _timing.CurTime)
            return;

        if (!_scp173Query.TryGetComponent(playerEntity.Value, out var scp173))
            return;

        var mousePos = _eyeManager.PixelToMap(_inputManager.MouseScreenPosition);
        if (mousePos.MapId == MapId.Nullspace)
            return;

        var playerPos = _transform.GetWorldPosition(playerEntity.Value);
        var performerCoords = _transform.GetMapCoordinates(playerEntity.Value);

        // Ограничиваем цель максимальной дальностью прыжка
        _scp173.ClampTarget(performerCoords, ref mousePos, scp173.MaxJumpRange);

        // Рейкаст по AllMask для обнаружения как мобов, так и препятствий
        var direction = mousePos.Position - playerPos;
        var normalizedDirection = Vector2.Normalize(direction);
        var ray = new CollisionRay(playerPos, normalizedDirection, collisionMask: (int) CollisionGroup.AllMask);

        var rayCastResults = _physics.IntersectRay(mousePos.MapId, ray, direction.Length(), playerEntity, false)
            .OrderBy(x => x.Distance)
            .ToList();

        var lastSafePos = playerPos;
        var finalPos = mousePos.Position;

        foreach (var result in rayCastResults)
        {
            var ent = result.HitEntity;

            // Если моб — рисуем маркер убийства
            if (_mobQuery.TryGetComponent(ent, out var mobState) && mobState.CurrentState == MobState.Alive)
            {
                args.WorldHandle.DrawCircle(result.HitPos, 0.15f, Color.MediumVioletRed);
                lastSafePos = result.HitPos;
                continue;
            }

            // Если непроходимое препятствие — останавливаемся
            if (_scp173.CheckImpassableObstacle(ent))
            {
                finalPos = lastSafePos != playerPos ? lastSafePos : playerPos;
                break;
            }
        }

        args.WorldHandle.DrawLine(playerPos, finalPos, Color.Aqua);
        args.WorldHandle.DrawCircle(finalPos, 0.4f, Color.Red, false);
    }
}

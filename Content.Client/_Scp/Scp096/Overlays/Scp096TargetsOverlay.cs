using System.Numerics;
using Content.Shared._Scp.Blinking;
using Content.Shared._Scp.Scp096.Main.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._Scp.Scp096.Overlays;

public sealed partial class Scp096TargetsOverlay : Overlay
{
    [Dependency] private IEntityManager _ent = default!;

    private readonly TransformSystem _transform;
    private readonly SpriteSystem _sprite;

    private readonly Entity<Scp096Component> _entity;

    private readonly EntityQuery<Scp096TargetComponent> _targetQuery;
    private readonly List<(Entity<SpriteComponent> ent, float alpha)> _cachedAlphas = new(64);

    public Scp096TargetsOverlay(Entity<Scp096Component> entity)
    {
        IoCManager.InjectDependencies(this);

        _entity = entity;

        _transform = _ent.System<TransformSystem>();
        _sprite = _ent.System<SpriteSystem>();

        _targetQuery = _ent.GetEntityQuery<Scp096TargetComponent>();
    }

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    protected override void Draw(in OverlayDrawArgs args)
    {
        // Показываем ранее скрытые сущности
        ShowAllHiddenEntities();

        if (_entity.Comp.TargetsCount <= 0)
            return;

        // Скрываем все сущности, которые не являются целями
        HideNonTargetEntities();

        // Рисуем указатель к ближайшей цели
        DrawLineToTarget(in args);
    }

    private void HideNonTargetEntities()
    {
        var query = _ent.EntityQueryEnumerator<BlinkableComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out _, out var sprite))
        {
            if (_ent.IsClientSide(uid))
                continue;

            if (MathHelper.CloseTo(sprite.Color.A, 0f))
                continue;

            // Не скрываем целей скромника
            if (_targetQuery.HasComp(uid))
                continue;

            var entity = (uid, sprite);

            _cachedAlphas.Add((entity, sprite.Color.A));
            _sprite.SetColor(entity, sprite.Color.WithAlpha(0f));
        }
    }

    private void DrawLineToTarget(in OverlayDrawArgs args)
    {
        if (_entity.Comp.TargetsCount == 0)
            return;

        var playerPos = _transform.GetWorldPosition(_entity);
        var nearestTargetPos = FindClosestEntity(playerPos);

        if (nearestTargetPos == null)
            return;

        args.WorldHandle.DrawLine(playerPos, nearestTargetPos.Value, Color.Red);
        args.WorldHandle.DrawCircle(nearestTargetPos.Value, 0.4f, Color.Red, false);
    }

    private Vector2? FindClosestEntity(Vector2 playerPos)
    {
        Vector2? closestEntityPos = null;
        var closestDistance = float.MaxValue;

        var query = _ent.EntityQueryEnumerator<Scp096TargetComponent>();
        while (query.MoveNext(out var entity, out _))
        {
            var entityPosition = _transform.GetWorldPosition(entity);
            var distance = Vector2.Distance(playerPos, entityPosition);

            if (distance >= closestDistance)
                continue;

            closestDistance = distance;
            closestEntityPos = entityPosition;
        }

        return closestEntityPos;
    }

    private void ShowAllHiddenEntities()
    {
        // Обходим с конца, чтобы избежать проблем из-за смещения списка после RemoveAt
        for (var i = _cachedAlphas.Count - 1; i >= 0; i--)
        {
            var (ent, alpha) = _cachedAlphas[i];

            // Оставляем сущность неудаленной, если ее нет на клиенте
            // Потому что несуществование на клиенте может быть, когда сущность покинуло PVS
            // И когда она вернется(вдруг сохранится альфа), то нужно восстановить альфу.
            // Поэтому не используется Clear() списка и удаляем вручную.
            if (!_ent.EntityExists(ent))
                continue;

            _sprite.SetColor(ent.AsNullable(), ent.Comp.Color.WithAlpha(alpha));
            _cachedAlphas.RemoveAt(i);
        }
    }
}


using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._Scp.Blinking;
using Content.Shared._Scp.Containment.Cage;
using Content.Shared._Scp.Helpers;
using Content.Shared._Scp.Proximity;
using Content.Shared._Scp.Watching;
using Content.Shared.ActionBlocker;
using Content.Shared.DoAfter;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Storage.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Shared._Scp.Scp173;

// TODO: Выделить логику блокировки движения при смотрении в отдельную систему со своим компонентом.
public abstract class SharedScp173System : EntitySystem
{
    [Dependency] protected readonly IGameTiming Timing = default!;
    [Dependency] private readonly SharedBlinkingSystem _blinking = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] protected readonly EyeWatchingSystem Watching = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    protected static readonly TimeSpan ReagentCheckInterval = TimeSpan.FromSeconds(1f);

    public const float ContainmentRoomSearchRadius = 8f;

    private EntityQuery<InsideEntityStorageComponent> _insideQuery;
    private EntityQuery<ScpCageComponent> _scpCageQuery;

    public override void Initialize()
    {
        base.Initialize();

        _insideQuery = GetEntityQuery<InsideEntityStorageComponent>();
        _scpCageQuery = GetEntityQuery<ScpCageComponent>();
    }

    #region Jump Helpers

    /// <summary>
    /// Проверяет, является ли сущность непроходимым препятствием.
    /// Используется для определения, нужно ли остановить прыжок SCP-173 при столкновении.
    /// </summary>
    protected bool IsImpassableObstacle(EntityUid entity)
    {
        if (!TryComp<PhysicsComponent>(entity, out var physics))
            return false;

        if (!physics.Hard)
            return false;

        var layer = (CollisionGroup) physics.CollisionLayer;

        return layer.HasFlag(CollisionGroup.WallLayer)
               || layer.HasFlag(CollisionGroup.GlassAirlockLayer)
               || layer.HasFlag(CollisionGroup.HumanoidBlockLayer);
    }

    /// <summary>
    /// Ограничивает координаты цели прыжка до максимальной дальности.
    /// Если цель дальше <paramref name="maxRange"/>, координаты обрезаются.
    /// </summary>
    protected static void ClampTargetToRange(MapCoordinates performerCoords, ref MapCoordinates targetCoords, float maxRange)
    {
        var direction = targetCoords.Position - performerCoords.Position;
        var distance = direction.Length();

        if (distance > maxRange)
        {
            direction = Vector2.Normalize(direction) * maxRange;
            targetCoords = performerCoords.Offset(direction);
        }
    }

    #endregion


    public bool IsInScpCage(EntityUid uid, [NotNullWhen(true)] out EntityUid? storage)
    {
        storage = null;

        if (_insideQuery.TryComp(uid, out var insideEntityStorageComponent) &&
            _scpCageQuery.HasComp(insideEntityStorageComponent.Storage))
        {
            storage = insideEntityStorageComponent.Storage;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Находится ли 173 в своей камере. Проверяется по наличию рядом спавнера работы
    /// </summary>
    public bool IsContained(EntityUid uid)
    {
        return Watching.TryGetAnyEntitiesVisibleTo<Scp173BlockStructureDamageComponent>(uid,
            LineOfSightBlockerLevel.None,
            LookupFlags.Sensors | LookupFlags.Sundries,
            ContainmentRoomSearchRadius);
    }
}

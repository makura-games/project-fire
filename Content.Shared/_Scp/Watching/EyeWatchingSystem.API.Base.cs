using System.Diagnostics.CodeAnalysis;
using Content.Shared._Scp.Blinking;
using Content.Shared._Scp.Helpers;
using Content.Shared._Scp.Proximity;
using Content.Shared._Scp.Watching.FOV;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Storage.Components;

namespace Content.Shared._Scp.Watching;

public sealed partial class EyeWatchingSystem
{
    [Dependency] private SharedBlinkingSystem _blinking = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private FieldOfViewSystem _fov = default!;

    private const float MinVisibilityRange = 2f;
    private const float BlurryVisionRangeMultiplier = 2.5f;

    private EntityQuery<MobStateComponent> _mobStateQuery;
    private EntityQuery<InsideEntityStorageComponent> _insideStorageQuery;
    private EntityQuery<BlinkableComponent> _blinkableQuery;
    private EntityQuery<BlurryVisionComponent> _blurryVisionQuery;

    private void InitializeApi()
    {
        _mobStateQuery = GetEntityQuery<MobStateComponent>();
        _insideStorageQuery = GetEntityQuery<InsideEntityStorageComponent>();
        _blinkableQuery = GetEntityQuery<BlinkableComponent>();
        _blurryVisionQuery = GetEntityQuery<BlurryVisionComponent>();
    }

    /// <summary>
    /// Получает и возвращает всех сущности в радиусе видимости для цели.
    /// По сути является аналогом <see cref="EntityLookupSystem"/>, но использует проверку на линию видимости.
    /// </summary>
    /// <remarks>
    /// В методе нет проверок на дополнительные состояния, такие как моргание/закрыты ли глаза/поле зрения т.п.
    /// Единственная проверка - можно ли физически увидеть цель(т.е. не закрыта ли она стеной и т.п.).
    /// <see cref="ProximitySystem.IsRightType(EntityUid, EntityUid, LineOfSightBlockerLevel)"/>
    /// </remarks>
    /// <param name="ent">Цель, для которой ищем сущности в радиусе видимости</param>
    /// <param name="potentialWatchers">Список всех, кто находится в радиусе видимости</param>
    /// <param name="type">Требуемая прозрачность линии видимости.</param>
    /// <param name="flags">Список флагов для поиска целей в <see cref="EntityLookupSystem"/></param>
    /// <param name="rangeOverride">Если нужно использовать другой радиус поиска, отличный от <see cref="SeeRange"/></param>
    /// <typeparam name="T">Компонент, который должны иметь все сущности в радиусе видимости</typeparam>
    /// <returns>Удалось ли найти хоть кого-то</returns>
    public bool TryGetAllEntitiesVisibleTo<T>(
        Entity<TransformComponent?> ent,
        List<Entity<T>> potentialWatchers,
        LineOfSightBlockerLevel type = LineOfSightBlockerLevel.Transparent,
        LookupFlags flags = LookupFlags.Uncontained | LookupFlags.Approximate,
        float? rangeOverride = null)
        where T : IComponent
    {
        using var searchSet = HashSetPoolEntity<T>.Rent();
        return TryGetAllEntitiesVisibleTo(ent, potentialWatchers, searchSet.Value, type, flags, rangeOverride);
    }

    /// <summary>
    /// Получает и возвращает всех сущности в радиусе видимости для цели.
    /// По сути является аналогом <see cref="EntityLookupSystem"/>, но использует проверку на линию видимости.
    /// </summary>
    /// <remarks>
    /// В методе нет проверок на дополнительные состояния, такие как моргание/закрыты ли глаза/поле зрения т.п.
    /// Единственная проверка - можно ли физически увидеть цель(т.е. не закрыта ли она стеной и т.п.).
    /// <see cref="ProximitySystem.IsRightType(EntityUid, EntityUid, LineOfSightBlockerLevel)"/>
    /// </remarks>
    /// <param name="ent">Цель, для которой ищем сущности в радиусе видимости</param>
    /// <param name="potentialWatchers">Список всех, кто находится в радиусе видимости</param>
    /// <param name="searchSet">Заранее заготовленный список, который будет использоваться в <see cref="EntityLookupSystem"/></param>
    /// <param name="type">Требуемая прозрачность линии видимости.</param>
    /// <param name="flags">Список флагов для поиска целей в <see cref="EntityLookupSystem"/></param>
    /// <param name="rangeOverride">Если нужно использовать другой радиус поиска, отличный от <see cref="SeeRange"/></param>
    /// <typeparam name="T">Компонент, который должны иметь все сущности в радиусе видимости</typeparam>
    /// <returns>Удалось ли найти хоть кого-то</returns>
    private bool TryGetAllEntitiesVisibleTo<T>(
        Entity<TransformComponent?> ent,
        List<Entity<T>> potentialWatchers,
        HashSet<Entity<T>> searchSet,
        LineOfSightBlockerLevel type = LineOfSightBlockerLevel.Transparent,
        LookupFlags flags = LookupFlags.Uncontained | LookupFlags.Approximate,
        float? rangeOverride = null)
        where T : IComponent
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return false;

        searchSet.Clear();
        _lookup.GetEntitiesInRange(ent.Comp.Coordinates, GetVisibilityRange(ent, rangeOverride), searchSet, flags);

        foreach (var target in searchSet)
        {
            if (!IsInProximity(ent, target, type))
                continue;

            potentialWatchers.Add(target);
        }

        return potentialWatchers.Count != 0;
    }

    /// <summary>
    /// Проверяет, есть ли хоть одна сущность в радиусе видимости цели.
    /// По сути является аналогом <see cref="EntityLookupSystem"/>, но использует проверку на линию видимости.
    /// </summary>
    /// <remarks>
    /// В методе нет проверок на дополнительные состояния, такие как моргание/закрыты ли глаза/поле зрения т.п.
    /// Единственная проверка - можно ли физически увидеть цель(т.е. не закрыта ли она стеной и т.п.).
    /// <see cref="ProximitySystem.IsRightType(EntityUid, EntityUid, LineOfSightBlockerLevel)"/>
    /// </remarks>
    /// <param name="viewer">Цель, для которой ищем сущности в радиусе видимости</param>
    /// <param name="type">Требуемая прозрачность линии видимости.</param>
    /// <param name="flags">Список флагов для поиска целей в <see cref="EntityLookupSystem"/></param>
    /// <param name="rangeOverride">Если нужно использовать другой радиус поиска, отличный от <see cref="SeeRange"/></param>
    /// <typeparam name="T">Компонент, который должны иметь все сущности в радиусе видимости</typeparam>
    /// <returns>Удалось ли найти хоть кого-то</returns>
    public bool TryGetAnyEntitiesVisibleTo<T>(
        Entity<TransformComponent?> viewer,
        LineOfSightBlockerLevel type = LineOfSightBlockerLevel.Transparent,
        LookupFlags flags = LookupFlags.Uncontained | LookupFlags.Approximate,
        float? rangeOverride = null)
        where T : IComponent
    {
        using var searchSet = HashSetPoolEntity<T>.Rent();
        if (!TryGetAnyEntitiesVisibleTo(viewer, out _, searchSet.Value, type, flags, rangeOverride))
            return false;

        return true;
    }

    /// <summary>
    /// Получает и возвращает первую сущность в радиусе видимости цели.
    /// По сути является аналогом <see cref="EntityLookupSystem"/>, но использует проверку на линию видимости.
    /// </summary>
    /// <remarks>
    /// В методе нет проверок на дополнительные состояния, такие как моргание/закрыты ли глаза/поле зрения т.п.
    /// Единственная проверка - можно ли физически увидеть цель(т.е. не закрыта ли она стеной и т.п.).
    /// <see cref="ProximitySystem.IsRightType(EntityUid, EntityUid, LineOfSightBlockerLevel)"/>
    /// </remarks>
    /// <param name="viewer">Цель, для которой ищем сущности в радиусе видимости</param>
    /// <param name="firstVisible">Первая попавшаяся сущность в радиусе видимости цели</param>
    /// <param name="type">Требуемая прозрачность линии видимости.</param>
    /// <param name="flags">Список флагов для поиска целей в <see cref="EntityLookupSystem"/></param>
    /// <param name="rangeOverride">Если нужно использовать другой радиус поиска, отличный от <see cref="SeeRange"/></param>
    /// <typeparam name="T">Компонент, который должны иметь все сущности в радиусе видимости</typeparam>
    /// <returns>Удалось ли найти хоть кого-то</returns>
    public bool TryGetAnyEntitiesVisibleTo<T>(
        Entity<TransformComponent?> viewer,
        [NotNullWhen(true)] out Entity<T>? firstVisible,
        LineOfSightBlockerLevel type = LineOfSightBlockerLevel.Transparent,
        LookupFlags flags = LookupFlags.Uncontained | LookupFlags.Approximate,
        float? rangeOverride = null)
        where T : IComponent
    {
        firstVisible = null;

        using var searchSet = HashSetPoolEntity<T>.Rent();
        if (!TryGetAnyEntitiesVisibleTo(viewer, out var first, searchSet.Value, type, flags, rangeOverride))
            return false;

        firstVisible = first;
        return true;
    }

    /// <summary>
    /// Получает и возвращает первую сущность в радиусе видимости цели.
    /// По сути является аналогом <see cref="EntityLookupSystem"/>, но использует проверку на линию видимости.
    /// </summary>
    /// <remarks>
    /// В методе нет проверок на дополнительные состояния, такие как моргание/закрыты ли глаза/поле зрения т.п.
    /// Единственная проверка - можно ли физически увидеть цель(т.е. не закрыта ли она стеной и т.п.).
    /// <see cref="ProximitySystem.IsRightType(EntityUid, EntityUid, LineOfSightBlockerLevel)"/>
    /// </remarks>
    /// <param name="viewer">Цель, для которой ищем сущности в радиусе видимости</param>
    /// <param name="firstVisible">Первая попавшаяся сущность в радиусе видимости цели</param>
    /// <param name="searchSet">Заранее заготовленный список, который будет использоваться в <see cref="EntityLookupSystem"/></param>
    /// <param name="type">Требуемая прозрачность линии видимости.</param>
    /// <param name="flags">Список флагов для поиска целей в <see cref="EntityLookupSystem"/></param>
    /// <param name="rangeOverride">Если нужно использовать другой радиус поиска, отличный от <see cref="SeeRange"/></param>
    /// <typeparam name="T">Компонент, который должны иметь все сущности в радиусе видимости</typeparam>
    /// <returns>Удалось ли найти хоть кого-то</returns>
    private bool TryGetAnyEntitiesVisibleTo<T>(
        Entity<TransformComponent?> viewer,
        [NotNullWhen(true)] out Entity<T>? firstVisible,
        HashSet<Entity<T>> searchSet,
        LineOfSightBlockerLevel type = LineOfSightBlockerLevel.Transparent,
        LookupFlags flags = LookupFlags.Uncontained | LookupFlags.Approximate,
        float? rangeOverride = null)
        where T : IComponent
    {
        firstVisible = null;

        if (!Resolve(viewer.Owner, ref viewer.Comp))
            return false;

        searchSet.Clear();
        _lookup.GetEntitiesInRange(viewer.Comp.Coordinates, GetVisibilityRange(viewer, rangeOverride), searchSet, flags);

        foreach (var target in searchSet)
        {
            if (!IsInProximity(viewer, target, type))
                continue;

            firstVisible = target;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Проверяет, правильный ли между сущностями тип видимости.
    /// </summary>
    private bool IsInProximity(EntityUid ent, EntityUid target, LineOfSightBlockerLevel type)
    {
        if (target == ent)
            return false;

        if (!_proximity.IsRightType(ent, target, type, out _))
            return false;

        return true;
    }

    /// <summary>
    /// Получает фактический радиус обзора сущности с учетом эффектов, ухудшающих зрение.
    /// </summary>
    private float GetVisibilityRange(EntityUid viewer, float? rangeOverride = null)
    {
        if (rangeOverride.HasValue)
            return rangeOverride.Value;

        if (!_blurryVisionQuery.TryComp(viewer, out var blurry))
            return SeeRange;

        return Math.Clamp(SeeRange - blurry.Magnitude * BlurryVisionRangeMultiplier, MinVisibilityRange, SeeRange);
    }

    /// <summary>
    /// Проверяет, находится ли цель в фактическом радиусе обзора смотрящего.
    /// </summary>
    private bool IsInVisibilityRange(EntityUid viewer, EntityUid target)
    {
        var viewerCoords = Transform(viewer).Coordinates;
        var targetCoords = Transform(target).Coordinates;

        return viewerCoords.TryDistance(EntityManager, targetCoords, out var distance) &&
               distance <= GetVisibilityRange(viewer);
    }

    /// <summary>
    /// Проверка на то, может ли смотрящий видеть цель
    /// </summary>
    /// <param name="viewer">Смотрящий</param>
    /// <param name="target">Цель, которую проверяем</param>
    /// <param name="useFov">Применять ли проверку на поле зрения?</param>
    /// <param name="useTimeCompensation">Будет ли использоваться компенсация времени? Нужно для передвижения SCP-173</param>
    /// <param name="checkBlinking">Будет ли проводиться проверка на моргание?</param>
    /// <param name="fovOverride">Если нужно использовать другой угол поля зрения</param>
    /// <returns>Видит ли смотрящий цель</returns>
    public bool CanSee(Entity<BlinkableComponent?> viewer,
        EntityUid target,
        bool useFov = true,
        bool useTimeCompensation = false,
        bool checkBlinking = true,
        float? fovOverride = null)
    {
        if (_mobState.IsIncapacitated(viewer))
            return false;

        if (!IsInVisibilityRange(viewer.Owner, target))
            return false;

        // Проверяем, видит ли смотрящий цель
        if (useFov && !_fov.IsInFov(viewer.Owner, target, fovOverride))
            return false; // Если не видит, то не считаем его как смотрящего

        if (checkBlinking && _blinking.IsBlind(viewer, useTimeCompensation))
            return false;

        var canSeeAttempt = new CanSeeAttemptEvent();
        RaiseLocalEvent(viewer, canSeeAttempt);

        if (canSeeAttempt.Blind)
            return false;

        return true;
    }
}

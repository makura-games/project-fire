using System.Linq;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Content.Shared.Storage.Components;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Scp.Proximity;

/// <summary>
/// Единая монолитная система, которая будет вызывать ивенты
/// при приближении сущности с <see cref="ProximityTargetComponent"/> к сущности с <see cref="ProximityReceiverComponent"/>.
/// Ивенты вызываются на обе сущности, что позволяет создавать логику при разных ситуациях.
/// </summary>
/// <remarks>
/// Система позволяет настраивать требуемый уровень видимости между сущностями, расстояние.
/// Настройка происходит в <see cref="ProximityTargetComponent"/>.
/// </remarks>
public sealed partial class ProximitySystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan ProximitySearchCooldown = TimeSpan.FromSeconds(0.05f);
    private TimeSpan _nextSearchTime = TimeSpan.Zero;

    // Оптимизации аллокации памяти
    private readonly HashSet<Entity<ProximityTargetComponent>> _targets = [];
    private readonly Dictionary<EntityUid, ProximityMatch> _currentMatches = [];
    private readonly Dictionary<EntityUid, ProximityMatch> _nextMatches = [];

    private const float JustUselessNumber = 30f;

    /// <summary>
    /// Список тегов, которые обозначают непрозрачный объект-преграду.
    /// </summary>
    private static readonly HashSet<ProtoId<TagPrototype>> SolidTags =
    [
        "Wall",
        "Window",
        "Airlock",
        "GlassAirlock",
        "HighSecDoor",
        "Windoor",
        "Directional",
        "SecureWindoor",
        "SecurePlasmaWindoor",
        "SecureUraniumWindoor",
    ];

    private EntityQuery<ActiveProximityTargetComponent> _activeProximityQuery;
    private EntityQuery<InsideEntityStorageComponent> _insideQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);

        _activeProximityQuery = GetEntityQuery<ActiveProximityTargetComponent>();
        _insideQuery = GetEntityQuery<InsideEntityStorageComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent args)
    {
        _nextSearchTime = TimeSpan.Zero;
        _currentMatches.Clear();
        _nextMatches.Clear();

        var query = EntityQueryEnumerator<ActiveProximityTargetComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            RemCompDeferred<ActiveProximityTargetComponent>(uid);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        // Оптимизации, чтобы просчет не происходил часто
        if (_timing.CurTime < _nextSearchTime)
            return;

        _nextMatches.Clear();

        var query = EntityQueryEnumerator<ProximityReceiverComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var receiver, out var xform))
        {
            _targets.Clear();
            _lookup.GetEntitiesInRange(xform.Coordinates, receiver.CloseRange, _targets, receiver.Flags);

            foreach (var target in _targets)
            {
                if (!_xformQuery.TryComp(target, out var targetXform))
                    continue;

                var targetCoords = targetXform.Coordinates;

                if (!xform.Coordinates.TryDistance(EntityManager, _transform, targetCoords, out var range))
                    continue;

                if (range > receiver.CloseRange)
                    continue;

                if (!IsRightType(uid, target, receiver.RequiredLineOfSight, out var lightOfSightBlockerLevel))
                    continue;

                var candidate = new ProximityMatch(uid, receiver.CloseRange, range, lightOfSightBlockerLevel);

                if (_nextMatches.TryGetValue(target, out var current) && !IsBetter(candidate, current))
                    continue;

                _nextMatches[target] = candidate;
            }
        }

        ApplyDelta();

        _nextSearchTime = _timing.CurTime + ProximitySearchCooldown;
    }

    private void ApplyDelta()
    {
        foreach (var (target, current) in _currentMatches)
        {
            if (_nextMatches.ContainsKey(target))
                continue;

            if (Deleted(target))
                continue;

            if (_activeProximityQuery.HasComp(target))
                RemCompDeferred<ActiveProximityTargetComponent>(target);

            var exited = new ProximityTargetExitedEvent(current.Receiver);
            RaiseLocalEvent(target, ref exited);
        }

        foreach (var (target, next) in _nextMatches)
        {
            if (Deleted(target))
                continue;

            var hadCurrent = _currentMatches.TryGetValue(target, out var current);
            var proximity = EnsureComp<ActiveProximityTargetComponent>(target);

            proximity.Receiver = next.Receiver;
            proximity.CloseRange = next.CloseRange;

            if (!hadCurrent)
            {
                var entered = new ProximityTargetEnteredEvent(next.Receiver, next.Range, next.CloseRange, next.BlockerLevel);
                RaiseLocalEvent(target, ref entered);
                continue;
            }

            if (current.Receiver != next.Receiver)
            {
                var changed = new ProximityTargetReceiverChangedEvent(
                    current.Receiver,
                    next.Receiver,
                    next.Range,
                    next.CloseRange,
                    next.BlockerLevel);

                RaiseLocalEvent(target, ref changed);
            }
        }

        _currentMatches.Clear();
        foreach (var (target, next) in _nextMatches)
        {
            _currentMatches[target] = next;
        }
    }

    private static bool IsBetter(ProximityMatch candidate, ProximityMatch current)
    {
        var candidateNormalized = candidate.Range / MathF.Max(candidate.CloseRange, float.Epsilon);
        var currentNormalized = current.Range / MathF.Max(current.CloseRange, float.Epsilon);

        if (candidateNormalized < currentNormalized)
            return true;

        if (candidateNormalized > currentNormalized)
            return false;

        if (candidate.BlockerLevel < current.BlockerLevel)
            return true;

        if (candidate.BlockerLevel > current.BlockerLevel)
            return false;

        return candidate.Receiver.CompareTo(current.Receiver) < 0;
    }


    /// <inheritdoc cref="IsRightType(EntityUid, EntityUid, LineOfSightBlockerLevel, out LineOfSightBlockerLevel)"/>
    public bool IsRightType(EntityUid receiver, EntityUid target, LineOfSightBlockerLevel type)
    {
        return IsRightType(receiver, target, type, out _);
    }

    /// <summary>
    /// Проверяет, совпадает ли тип прозрачности сущностей между двумя переданными сущностям.
    /// </summary>
    /// <param name="receiver">Первая сущность</param>
    /// <param name="target">Вторая сущность</param>
    /// <param name="type">Нужный тип</param>
    /// <param name="level">Текущий тип перекрытия между сущностями</param>
    /// <returns>Совпадает или нет</returns>
    public bool IsRightType(EntityUid receiver, EntityUid target, LineOfSightBlockerLevel type, out LineOfSightBlockerLevel level)
    {
        level = GetLightOfSightBlockerLevel(receiver, target);
        return level <= type;
    }

    /// <summary>
    /// Получает тип заслоения сущности в зависимости от того, что препятствует(или нет) их прямому контакту.
    /// Например, если <see cref="receiver"/> находится за окном от <see cref="target"/>, то метод выдаст <see cref="LineOfSightBlockerLevel.Transparent"/>.
    /// </summary>
    /// <param name="receiver">Первая сущность</param>
    /// <param name="target">Вторая сущность</param>
    /// <returns>Тип прозрачности сущностей, перекрывающий прямой контакт между этими двумя</returns>
    public LineOfSightBlockerLevel GetLightOfSightBlockerLevel(EntityUid receiver, EntityUid target)
    {
        if (_insideQuery.HasComp(receiver))
            return LineOfSightBlockerLevel.Solid;

        var isUnOccluded = _examine.InRangeUnOccluded(receiver, target, JustUselessNumber);

        if (!isUnOccluded)
            return LineOfSightBlockerLevel.Solid;
        else if (!InRangeUnobstructed(receiver, target))
            return LineOfSightBlockerLevel.Transparent;
        else
            return LineOfSightBlockerLevel.None;
    }

    private bool InRangeUnobstructed(Entity<TransformComponent?> first, Entity<TransformComponent?> second)
    {
        return _interaction.InRangeUnobstructed(
            first,
            second,
            JustUselessNumber,
            predicate: IsNotSolidObject);
    }

    private bool IsNotSolidObject(EntityUid e) => !_tag.HasAnyTag(e, SolidTags);

    /// <summary>
    /// Проверяет, есть ли в заданном радиусе сущность с компонентом T
    /// </summary>
    /// <param name="uid">Сущность, от которой берем радиус</param>
    /// <param name="range">Радиус проверки</param>
    /// <param name="level"><see cref="LineOfSightBlockerLevel"/></param>
    /// <typeparam name="T">Компонент, который должен быть у искомой сущности</typeparam>
    /// <returns>Имеется ли рядом такая сущность или нет</returns>
    public bool IsNearby<T>(EntityUid uid, float range, LineOfSightBlockerLevel level = LineOfSightBlockerLevel.None) where T : IComponent
    {
        return _lookup.GetEntitiesInRange<T>(Transform(uid).Coordinates, range, LookupFlags.Uncontained | LookupFlags.Approximate)
            .Any(e => IsRightType(uid, e, level, out _));
    }

    /// <summary>
    /// Проверяет, есть ли в заданном радиусе сущность с компонентом T
    /// </summary>
    /// <param name="uid">Сущность, от которой берем радиус</param>
    /// <param name="range">Радиус проверки</param>
    /// <param name="buffer">Заготовленный список, чтобы не плодить каждый раз новый</param>
    /// <param name="level"><see cref="LineOfSightBlockerLevel"/></param>
    /// <typeparam name="T">Компонент, который должен быть у искомой сущности</typeparam>
    /// <returns>Имеется ли рядом такая сущность или нет</returns>
    public bool IsNearby<T>(EntityUid uid, float range, HashSet<Entity<T>> buffer, LineOfSightBlockerLevel level = LineOfSightBlockerLevel.None) where T : IComponent
    {
        _lookup.GetEntitiesInRange(Transform(uid).Coordinates, range, buffer);

        return buffer.Any(e => IsRightType(uid, e, level, out _));
    }

    private readonly record struct ProximityMatch(
        EntityUid Receiver,
        float CloseRange,
        float Range,
        LineOfSightBlockerLevel BlockerLevel);
}

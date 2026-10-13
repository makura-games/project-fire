using Content.Shared._Scp.Blinking;
using Content.Shared._Scp.Helpers;
using Content.Shared._Scp.Proximity;
using Robust.Shared.Timing;

namespace Content.Shared._Scp.Watching;

/// <summary>
/// Единая система, обрабатывающая смотрение игроков друг на друга.
/// Включает различные проверки, например поле зрения, закрыты ли глаза и подобное
/// </summary>
public sealed partial class EyeWatchingSystem : EntitySystem
{
    [Dependency] private ProximitySystem _proximity = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>
    /// Радиус, в котором сущности могут увидеть друг друга.
    /// </summary>
    [ViewVariables]
    public float SeeRange { get; private set; } = 16f;

    public override void Initialize()
    {
        InitializeApi();
        InitializeEvents();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        ShutdownEvents();
    }

    /// <summary>
    /// Обрабатывает все сущности, помеченные как цель для просмотра. Вызывает ивент на смотрящем, если он видит цель.
    /// Это может использоваться для создания различных эффектов или динамических проверок
    /// </summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<WatchingTargetComponent>();
        while (query.MoveNext(out var uid, out var watchingComponent))
        {
            if (_timing.CurTime < watchingComponent.NextTimeWatchedCheck)
                continue;

            // Все потенциально возможные смотрящие. Среди них те, что прошли фаст-чек из самых простых проверок
            using var potentialWatchers = ListPoolEntity<BlinkableComponent>.Rent();
            if (!TryGetAllEntitiesVisibleTo(uid, potentialWatchers.Value))
            {
                SetNextTime(watchingComponent);
                Dirty(uid, watchingComponent);

                continue;
            }

            // Вызываем ивенты на потенциально смотрящих. Без особых проверок
            // Полезно в коде, который уже использует подобные проверки или не требует этого
            foreach (var potentialViewer in potentialWatchers.Value)
            {
                var simpleViewerEvent = new SimpleEntityLookedAtEvent((uid, watchingComponent));
                var simpleTargetEvent = new SimpleEntitySeenEvent(potentialViewer);

                // За подробностями какой ивент для чего навести мышку на название ивента
                RaiseLocalEvent(potentialViewer, ref simpleViewerEvent);
                RaiseLocalEvent(uid, ref simpleTargetEvent);
            }

            // Если требуются только Simple ивенты, то нет смысла делать дальнейшие действия.
            if (watchingComponent.SimpleMode)
            {
                SetNextTime(watchingComponent);
                Dirty(uid, watchingComponent);

                continue;
            }

            // Проверяет всех потенциальных смотрящих на то, действительно ли они видят цель.
            // Каждый потенциально смотрящий проходит полный комплекс проверок.
            // Выдает полный список всех сущностей, кто действительно видит цель
            using var realWatchers = ListPoolEntity<BlinkableComponent>.Rent();
            if (!TryGetWatchersFrom(uid, realWatchers.Value, potentialWatchers.Value, checkProximity: false))
            {
                SetNextTime(watchingComponent);
                Dirty(uid, watchingComponent);

                continue;
            }

            // Вызываем ивент на смотрящем, говорящие, что он действительно видит цель
            foreach (var viewer in realWatchers.Value)
            {
                var netViewer = GetNetEntity(viewer);
                var firstTime = !watchingComponent.AlreadyLookedAt.ContainsKey(netViewer);

                // Небольшая заглушка для удобства работы с ивентами.
                // Использовать firstTime не очень удобно, поэтому в качестве дополнительного способа определения будет TimeSpan.Zero
                watchingComponent.AlreadyLookedAt[netViewer] = TimeSpan.Zero;

                // За подробностями какой ивент для чего навести мышку на название ивента
                var viewerEvent = new EntityLookedAtEvent((uid, watchingComponent), firstTime);
                var targetEvent = new EntitySeenEvent(viewer, firstTime);

                RaiseLocalEvent(viewer, ref viewerEvent);
                RaiseLocalEvent(uid, ref targetEvent);

                // Добавляет смотрящего в список уже смотревших, чтобы позволить системам манипулировать этим
                // И предотвращать эффект, если игрок смотрит не первый раз или не так давно
                watchingComponent.AlreadyLookedAt[netViewer] = _timing.CurTime;
            }

            SetNextTime(watchingComponent);
            Dirty(uid, watchingComponent);
        }
    }

    /// <summary>
    /// Устанавливает время следующей проверки видимости
    /// </summary>
    private void SetNextTime(WatchingTargetComponent component)
    {
        component.NextTimeWatchedCheck = _timing.CurTime + component.WatchingCheckInterval;
    }
}

/// <summary>
/// Ивент вызываемый на смотрящем, передающий информации, что он посмотрел на кого-то
/// </summary>
/// <param name="Target">Цель, на которую посмотрели</param>
/// <param name="FirstTime">Видим ли мы цель в первый раз</param>
[ByRefEvent]
public readonly record struct EntityLookedAtEvent(Entity<WatchingTargetComponent> Target, bool FirstTime);

/// <summary>
/// Ивент вызываемый на цели, передающий информации, что на нее кто-то посмотрел
/// </summary>
/// <param name="Viewer">Смотрящий, который увидел цель</param>
/// <param name="FirstTime">Видим ли мы цель в первый раз</param>
[ByRefEvent]
public readonly record struct EntitySeenEvent(EntityUid Viewer, bool FirstTime);

/// <summary>
/// Простой ивент, говорящий, что смотрящий посмотрел на цель.
/// Вызывается до прохождения различных проверок на смотрящем. Если вдруг требуются собственная ручная проверка
/// </summary>
/// <param name="Target">Цель, на которую посмотри</param>
[ByRefEvent]
public readonly record struct SimpleEntityLookedAtEvent(Entity<WatchingTargetComponent> Target);

/// <summary>
/// Простой ивент, говорящий, что на цель кто-то посмотрел.
/// Вызывается до прохождения различных проверок на цели. Если вдруг требуются собственная ручная проверка
/// </summary>
/// <param name="Viewer">Смотрящий</param>
[ByRefEvent]
public readonly record struct SimpleEntitySeenEvent(EntityUid Viewer);

using System.Diagnostics.CodeAnalysis;
using Content.Server._Scp.Misc;
using Content.Server.Light.EntitySystems;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Shared._Sunrise.Helpers;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Light.Components;
using Content.Shared.Station.Components;
using Content.Shared.Storage.Components;
using Content.Shared.Tag;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Scp.MetaGarbage;

/// <summary>
/// Система сохранения мусора между раундами.
/// В конце раунда сохраняет мусор, который был в комплексе и спавнит его в начале следующего раунда.
/// </summary>
public sealed partial class MetaGarbageSystem : EntitySystem
{
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly LightBulbSystem _bulb = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private static readonly HashSet<ProtoId<TagPrototype>> AllowedTags = [ "Trash", "MetaGarbageSavable" ];
    private static readonly HashSet<ProtoId<TagPrototype>> ForbiddenTags = [ "MetaGarbagePreventSaving" ];
    private static readonly ProtoId<TagPrototype> ReplaceTag = "MetaGarbageReplace";
    private static readonly ProtoId<TagPrototype> ContainerAllowedTag = "MetaGarbageCanBeSpawnedInContainer";

    /// <summary>
    /// Сохраненный мусор, который будет передаваться из раунда в раунд.
    /// Ключ - прототип комплекса, к которому привязан мусор.
    /// Значение - список данных о мусоре, который был сохранен.
    /// </summary>
    public Dictionary<EntProtoId, List<StationMetaGarbageData>> CachedGarbage { get; private set; } = [];

    /// <summary>
    /// Радиус поиска аналогичных сущностей, который используется для поиска аналогичных предметов на месте спавна.
    /// Нужен, чтобы не спавнить замапленный на карте мусор, дублируя его.
    /// </summary>
    private const float AlreadySpawnedItemsSearchRadius = 0.2f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MetaGarbageTargetComponent, StationPostInitEvent>(OnMapInit, after:[typeof(SharedSolutionContainerSystem)]);
        SubscribeLocalEvent<RealRoundEndedMessage>(OnRoundEnded);

        InitializeCCVars();
        InitializeDebug();
    }

    private void OnMapInit(Entity<MetaGarbageTargetComponent> ent, ref StationPostInitEvent args)
    {
        if (!_enableSpawningWithoutRule)
            return;

        TrySpawnGarbage((ent, ent.Comp, args.Station.Comp));
    }

    private void OnRoundEnded(RealRoundEndedMessage args)
    {
        TrySaveGarbage();
    }

    /// <summary>
    /// Сохраняет мусор для всех станций
    /// </summary>
    public bool TrySaveGarbage()
    {
        if (!_enableSaving)
            return false;

        var query = EntityQueryEnumerator<MetaGarbageTargetComponent, StationDataComponent>();

        while (query.MoveNext(out var uid, out var metaGarbage, out _))
        {
            var stationPrototype = Prototype(uid);
            if (stationPrototype == null)
                continue;

            // Вычищаем прошлые данные о мусоре на данной карте и собираем их заново
            CachedGarbage.Remove(stationPrototype);

            // Нам не нужен перенос мусора при нулевом коэфициенте
            if (metaGarbage.SpawnPercent <= 0f)
                continue;

            // Сохраняем новые данные
            CollectGarbage((uid, metaGarbage), stationPrototype);
            PrintDebugInfo(uid);
        }

        return true;
    }

    /// <summary>
    /// Спавнит сохраненный для переданной станции мусор.
    /// </summary>
    public bool TrySpawnGarbage(Entity<MetaGarbageTargetComponent?, StationDataComponent?> ent)
    {
        if (!_enableSpawning)
            return false;

        if (!Resolve(ent, ref ent.Comp1, ref ent.Comp2))
            return false;

        var mapPrototype = Prototype(ent);
        if (mapPrototype == null)
            return false;

        if (!CachedGarbage.TryGetValue(mapPrototype, out var list))
            return false;

        list.ShuffleRobust(_random).TakePercentage(ent.Comp1.SpawnPercent);
        var mapId = GetStationMapId((ent, ent.Comp2));

        var spawnedCount = 0;
        foreach (var data in list)
        {
            var coords = new MapCoordinates(data.Position, mapId);

            if (IsItemAlreadySpawned(data.Prototype, coords, out var found) && !data.Replace)
                continue;

            if (data.Replace)
                Del(found);

            var item = Spawn(data.Prototype, coords, rotation: data.Rotation);
            TryAddLiquid(item, data.LiquidData);
            TrySetBulbState(item, data.BulbState);
            TryInsertIntoContainer(item, coords, data.ContainerName);

            spawnedCount++;
            Log.Debug($"Spawned {data.Prototype}|{item} at {data.Position} on map {mapId}|{Name(ent)}");
        }

        Log.Info($"Spawned {spawnedCount}/{list.Count} items");
        PrintDebugInfo(ent);

        return true;
    }

    private void CollectGarbage(Entity<MetaGarbageTargetComponent> station, EntProtoId stationPrototype)
    {
        var query = EntityQueryEnumerator<TagComponent, TransformComponent>();

        var debugCount = 0;

        while (query.MoveNext(out var uid, out var tag, out var xform))
        {
            if (!IsValidEntityToSave(uid, tag))
                continue;

            var itemStation = _station.GetOwningStation(uid, xform);
            if (station != itemStation)
                continue;

            var proto = Prototype(uid);
            if (proto == null)
                continue;

            if (!TryCheckSolution(station, uid, out var solution))
                continue;

            SaveEntity((uid, xform), stationPrototype, proto, solution);
            debugCount++;
        }

        Log.Info($"Saved {debugCount} trash items");
    }

    private bool IsValidEntityToSave(EntityUid uid, TagComponent tag)
    {
        if (!_tag.HasAnyTag(tag, AllowedTags))
            return false;

        if (_tag.HasAnyTag(tag, ForbiddenTags))
            return false;

        // Если сохранение в контейнерах разрешено - считаем сущность доступной для сохранения
        // так как ниже будут только проверки на контейнеры. Остальные проверки стоит размещать выше.
        if (_tag.HasTag(tag, ContainerAllowedTag))
            return true;

        // Проверка на контейнеры.
        if (HasComp<InsideEntityStorageComponent>(uid))
            return false;

        if (_container.IsEntityInContainer(uid))
            return false;

        return true;
    }

    /// <summary>
    /// Проверяет реагенты внутри сущности.
    /// Если найдены запрещенные реагенты с шансом не дает сущности сохраниться.
    /// Если все ок - возвращает информацию о реагентах. Она может быть нулл
    /// </summary>
    private bool TryCheckSolution(Entity<MetaGarbageTargetComponent> station,
        EntityUid uid,
        out Dictionary<string, MetaGarbageSolutionProxy>? data)
    {
        data = null;

        if (!TryComp<SolutionContainerManagerComponent>(uid, out var solutionContainer))
            return true;

        data = [];

        // Собираем данные о реагента
        foreach (var container in solutionContainer.Containers)
        {
            if (!_solution.TryGetSolution((uid, solutionContainer), container, out var targetSolutionEnt, out var targetSolution))
                continue;

            // Проверяем наличие специальных реагентов, количество которых мы хотим сократить
            foreach (var (reagentProto, probability) in station.Comp.ReagentSaveModifiers)
            {
                var reagent = new ReagentId(reagentProto, null);

                if (!targetSolution.TryGetReagent(reagent, out _))
                    continue;

                // Если не повезло - даем сигнал, что сущность не нужно сохранять
                if (!_random.Prob(probability))
                    return false;
            }

            var solution = targetSolutionEnt.Value.Comp.Solution;
            var liquidData = new MetaGarbageSolutionProxy(ReagentToProxy(solution.Contents));
            data[container] = liquidData;
        }

        return true;
    }

    /// <summary>
    /// Сохраняет сущность в словарь для последующего спавна
    /// </summary>
    private void SaveEntity(Entity<TransformComponent> ent, EntProtoId stationPrototype, EntProtoId targetProto, Dictionary<string, MetaGarbageSolutionProxy>? liquid = null)
    {
        // Сохраняем данные о мусоре в список для спавна в следующем раунде.
        var position = _transform.GetWorldPosition(ent.Comp);
        var rotation = _transform.GetWorldRotation(ent.Comp);
        var replace = _tag.HasTag(ent, ReplaceTag);
        var containerName = _container.TryGetOuterContainer(ent, ent.Comp, out var container) ? container.ID : null;
        LightBulbState? bulbState = TryComp<LightBulbComponent>(ent, out var bulb) ? bulb.State : null;

        var data = new StationMetaGarbageData(targetProto, position, rotation, liquid, replace, containerName, bulbState);

        // Добавляем в словарь данные.
        // Ключ - айди прототипа карты, чтобы разные карты имели разный набор мусора с прошлых смен
        // Значение - список мусора, который сохранен для данной карты.
        if (CachedGarbage.TryGetValue(stationPrototype, out var list))
            list.Add(data);
        else
            CachedGarbage[stationPrototype] = [data];
    }

    /// <summary>
    /// Пытается добавить реагенты в сущность, если они у нее были в прошлом раунде.
    /// Вычищает стандартные реагенты из сущности, если они там есть.
    /// </summary>
    private bool TryAddLiquid(EntityUid uid, Dictionary<string, MetaGarbageSolutionProxy>? data)
    {
        if (data == null)
            return false;

        if (!TryComp<SolutionContainerManagerComponent>(uid, out var solutionContainer))
            return false;

        foreach (var (container, liquidData) in data)
        {
            var solution = new Solution(ProxyToReagent(liquidData.Contents));

            _solution.EnsureAllSolutions((uid, solutionContainer));

            if (!_solution.EnsureSolutionEntity((uid, solutionContainer),
                    container,
                    out _,
                    out var solutionEntity))
                continue;

            _solution.RemoveAllSolution(solutionEntity.Value);
            _solution.AddSolution(solutionEntity.Value, solution);

            var ev = new SolutionChangedEvent(solutionEntity.Value);
            RaiseLocalEvent(uid, ref ev);
        }

        return true;
    }

    /// <summary>
    /// Получает айди карты, на которой находится станция.
    /// </summary>
    private MapId GetStationMapId(Entity<StationDataComponent> ent)
    {
        foreach (var grid in ent.Comp.Grids)
        {
            var id = Transform(grid).MapID;

            if (id != MapId.Nullspace)
                return id;
        }

        // Сюда доходить не должно
        var fallback = Transform(ent).MapID;
        Log.Error($"Cannot find station map id, using fallback id: {fallback}");
        return fallback;
    }

    /// <summary>
    /// Проверяет, присутствует ли данный предмет на заданных координатах.
    /// Помогает избежать дублирования замапленных предметов.
    /// </summary>
    private bool IsItemAlreadySpawned(EntProtoId proto, MapCoordinates coords, [NotNullWhen(true)] out EntityUid? found)
    {
        found = null;
        foreach (var ent in _lookup.GetEntitiesInRange(coords, AlreadySpawnedItemsSearchRadius))
        {
            var prototype = Prototype(ent);
            if (prototype == null)
                continue;

            if (prototype == proto)
            {
                found = ent;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Конвертирует <seealso cref="ReagentQuantity"/> в <seealso cref="MetaGarbageReagentQuantityProxy"/>
    /// </summary>
    private static List<MetaGarbageReagentQuantityProxy> ReagentToProxy(List<ReagentQuantity> list)
    {
        List<MetaGarbageReagentQuantityProxy> toReturn = [];

        foreach (var quantity in list)
        {
            toReturn.Add(new MetaGarbageReagentQuantityProxy(quantity.Reagent, quantity.Quantity));
        }

        return toReturn;
    }

    /// <summary>
    /// Конвертирует <seealso cref="MetaGarbageReagentQuantityProxy"/> в <seealso cref="ReagentQuantity"/>
    /// </summary>
    private static List<ReagentQuantity> ProxyToReagent(List<MetaGarbageReagentQuantityProxy> list)
    {
        List<ReagentQuantity> toReturn = [];

        foreach (var quantity in list)
        {
            toReturn.Add(new ReagentQuantity(quantity.Reagent, quantity.Quantity));
        }

        return toReturn;
    }

    /// <summary>
    /// Пытается задать состояние лампочки.
    /// Например, разбитое или сожженое состояние.
    /// </summary>
    private bool TrySetBulbState(EntityUid uid, LightBulbState? state)
    {
        if (state == null)
            return false;

        _bulb.SetState(uid, state.Value);

        Log.Debug($"Bulb`s({Name(uid)}) state changed to {state.ToString()}");
        return true;
    }

    /// <summary>
    /// Пытается найти рядом нужный контейнер и положить внутрь сущность.
    /// </summary>
    /// <param name="uid">Сущность, которую мы хотим положить</param>
    /// <param name="coords">Координаты, где искать контейнер</param>
    /// <param name="container">Название контейнера, по которому мы будем его искать</param>
    /// <returns>Получилось ли вставить сущность или нет</returns>
    private bool TryInsertIntoContainer(EntityUid uid, MapCoordinates coords, string? container)
    {
        if (string.IsNullOrEmpty(container))
            return false;

        // Проходимся по всей контейнерам близким к данным координатам.
        // И проверяем, что этот контейнер имеет нужное нам название.
        var lookup = _lookup.GetEntitiesInRange<ContainerManagerComponent>(coords, 1f);
        foreach (var ent in lookup)
        {
            foreach (var (name, comp) in ent.Comp.Containers)
            {
                if (name != container)
                    continue;

                // Проверяем, есть ли в контейнере подобная нашей сущность
                // Если есть - вытаскиваем ее, удаляем и помещаем нашу.
                if (comp.ContainedEntities.Count != 0)
                {
                    var item = EntityUid.Invalid;
                    foreach (var contained in comp.ContainedEntities)
                    {
                        if (!IsSameItem(uid, contained))
                            continue;

                        item = contained;
                        break;
                    }

                    if (item == EntityUid.Invalid)
                        continue;

                    if (_tag.HasTag(uid, ReplaceTag))
                    {
                        _container.RemoveEntity(ent, item, ent.Comp, force: true);
                        Del(item);
                    }
                }

                _container.Insert(uid, comp, force: true);

                Log.Debug($"{Name(uid)} inserted into container {container} in {Name(ent)}");
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Проверяет, равен ли айди прототипа у двух сущностей.
    /// </summary>
    private bool IsSameItem(EntityUid uid, EntityUid other)
    {
        var uidProto = Prototype(uid);
        var otherProto = Prototype(other);

        return uidProto == otherProto;
    }
}

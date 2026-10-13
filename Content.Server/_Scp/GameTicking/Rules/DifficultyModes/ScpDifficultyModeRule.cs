using System.Linq;
using Content.Server._Sunrise.Helpers;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.Station.Systems;
using Content.Shared._Scp.Mobs.Components;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Item;
using Content.Shared.Roles;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._Scp.GameTicking.Rules.DifficultyModes;

public sealed partial class ScpDifficultyModeRule : GameRuleSystem<ScpDifficultyModeRuleComponent>
{
    [Dependency] private StationJobsSystem _stationJobs = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private SunriseHelpersSystem _helpers = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpComponent, PlayerSpawnCompleteEvent>(OnPlayerSpawn);
    }

    /// <summary>
    /// Срабатывает при спавне игрока на SCP объекте и закрывает слоты других объектов, если их лимит превышен.
    /// Лимиты задаются в текущем режиме сложности как количество определенных классов содержания в раунде.
    /// Если игрок с данным классом содержания заходит в раунд, то из всех слотов SCP объектов с данным классом также вычитается 1 слот
    /// </summary>
    private void OnPlayerSpawn(Entity<ScpComponent> ent, ref PlayerSpawnCompleteEvent args)
    {
        if (args.JobId == null)
            return;

        // Проверяем, запущен ли сейчас какой-либо режим сложности
        // Если да, сможем получить его компонент для получения настроек сложности
        var isGameModeStarted = _ticker.GetActiveGameRules()
            .Where(HasComp<ScpDifficultyModeRuleComponent>)
            .Select(Comp<ScpDifficultyModeRuleComponent>)
            .TryFirstOrDefault(out var rule);

        if (!isGameModeStarted || rule == null)
            return;

        // Получаем все работки SCP, которые соответствуют классу содержания зашедшего SCP объекта
        var matchingScp = _prototype.EnumeratePrototypes<JobPrototype>()
            .Where(proto => IsMatchingScpJob(ent.Comp.Class, proto, rule.PlayableWhitelist, rule.PlayableBlacklist));

        // Забираем у каждого SCP с классом схожим с только что зашедшим слот
        foreach (var scp in matchingScp)
        {
            if (scp.ID == args.JobId)
                continue;

            if (rule.ScpSlots.TryGetValue(ent.Comp.Class, out var slots) && slots == ScpDifficultyModeRuleComponent.UnlimitedSlotsFlag)
                continue;

            if (!_stationJobs.TryGetJobSlot(args.Station, scp, out var currentSlots))
                continue;

            if (currentSlots == null || currentSlots <= 0)
                continue;

            _stationJobs.TrySetJobSlot(args.Station, scp, currentSlots.Value - 1);
        }
    }

    protected override void Started(EntityUid uid,
        ScpDifficultyModeRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (!TryGetRandomStation(out var targetStation))
            return;

        // Проходимся по всем работкам и сцп-предметами, устанавливая текущие правила игры
        // Слоты на неподходящие работы закрываются, а предметы удаляются

        DealWithPlayableScp(component, targetStation.Value);
        DealWithItems(component, targetStation.Value);
    }

    private void DealWithPlayableScp(ScpDifficultyModeRuleComponent component, EntityUid targetStation)
    {
        foreach (var (classification, slots) in component.ScpSlots)
        {
            if (slots == ScpDifficultyModeRuleComponent.UnlimitedSlotsFlag)
                continue;

            // Получаем все работки SCP, которые соответствуют текущему классу содержания
            var matchingScp = _prototype.EnumeratePrototypes<JobPrototype>()
                .Where(proto => IsMatchingScpJob(classification, proto, component.PlayableWhitelist, component.PlayableBlacklist));

            // Подсчитываем, сколько будет доступно слотов
            var targetSlots = slots.Next(_random);

            // Устанавливаем для каждого подходящего SCP количество слотов
            foreach (var job in matchingScp)
            {
                _stationJobs.TrySetJobSlot(targetStation, job, targetSlots);
            }
        }
    }

    /// <summary>
    /// Проходится по SCP предметам и удаляем неразрешенные
    /// </summary>
    private void DealWithItems(ScpDifficultyModeRuleComponent component, EntityUid targetStation)
    {
        // Находим все неподходящие для данного режима предметы
        var inappropriateItems = _helpers.GetAll<ScpComponent, ItemComponent>()
            .Where(item =>
                !IsMatchingItem((item, item.Comp1), component.ItemWhitelist, component.ItemBlacklist, targetStation))
            .ToList();

        // И удаляем их
        foreach (var item in inappropriateItems)
        {
            QueueDel(item);
        }
    }

    /// <summary>
    /// Проверяет, является ли данная работа SCP и подходит ли под требования режима
    /// </summary>
    /// <returns>Да/Нет</returns>
    private bool IsMatchingScpJob(Classification classification, JobPrototype job, ComponentRegistry? whitelist, ComponentRegistry? blacklist)
    {
        if (job.JobEntity == null)
            return false;

        if (!_prototype.TryIndex<EntityPrototype>(job.JobEntity, out var entity))
            return false;

        // Реализация вайтлиста. Так как в вайтлисте будет перечисление компонентов, которые будут представлять сцп
        // То нам нужно, чтобы хотя бы один совпал
        if (whitelist != null && !entity.Components.Any(whitelist.Contains))
            return false;

        // Обратная ситуация с блеклистом. Нужно, чтобы не совпал ни один.
        // Следовательно, делаем возврат, если найден хоть один
        if (blacklist != null && entity.Components.Any(blacklist.Contains))
            return false;

        if (!entity.Components.TryGetComponent("Scp", out var component) || component is not ScpComponent scpComponent)
            return false;

        if (scpComponent.Class != classification)
            return false;

        return true;
    }

    /// <summary>
    /// Проверяет, подходит ли данный предмет под требования режима
    /// </summary>
    /// <returns>Да/Нет</returns>
    private bool IsMatchingItem(Entity<ScpComponent> item, EntityWhitelist? whitelist, EntityWhitelist? blacklist, EntityUid targetStation)
    {
        if (!_whitelist.CheckBoth(item, blacklist, whitelist))
            return false;

        var station = _station.GetOwningStation(item);

        if (station != targetStation)
            return false;

        return true;
    }
}

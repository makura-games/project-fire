using Content.Shared._Scp.Fear.Components;
using Content.Shared._Scp.Helpers;
using Content.Shared._Scp.Proximity;
using Content.Shared._Scp.Shaders;
using Content.Shared._Scp.Shaders.Grain;
using Content.Shared._Scp.Shaders.Highlighting;
using Content.Shared._Scp.Shaders.Vignette;
using Content.Shared._Scp.Watching;
using Content.Shared._Sunrise.Mood;
using Content.Shared.Examine;
using Content.Shared.Mobs.Systems;
using Content.Shared.Rejuvenate;
using Robust.Shared.Timing;

namespace Content.Shared._Scp.Fear.Systems;

/// <summary>
/// Система страха.
/// Работает на следующих базовых принципах:
/// Есть два способа взаимодействовать со страхом: повышать уровень страха или приближаться к источнику страха.
/// Приближение к источнику страха сильно усиливает эффекты в зависимости от расстояния до источника.
/// Повышение уровня страха усиливает эффекты, уровень страха понижается со временем.
/// </summary>
// TODO: Перевод всех фич страха на отдельные компоненты
// TODO: Attempt-ивенты для страха
public abstract partial class SharedFearSystem : EntitySystem
{
    [Dependency] private SharedHighlightSystem _highlight = default!;
    [Dependency] private EyeWatchingSystem _watching = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedShaderStrengthSystem _shaderStrength = default!;
    [Dependency] private ProximitySystem _proximity = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const string MoodSourceClose = "FearSourceClose";
    private const string MoodFearSourceSeen = "FearSourceSeen";
    private const string MoodHalfFearSourceSeen = "ScpFearSourceSeenHalf";

    private EntityQuery<FearSourceComponent> _fearSourceQuery;
    private EntityQuery<FearComponent> _fearQuery;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FearComponent, EntityLookedAtEvent>(OnEntityLookedAt);

        SubscribeLocalEvent<FearComponent, FearStateChangedEvent>(OnFearStateChanged);

        SubscribeLocalEvent<FearComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<FearComponent, RejuvenateEvent>(OnRejuvenate);

        SubscribeLocalEvent<FearComponent, ExaminedEvent>(OnExamine);

        InitializeFears();
        InitializeGameplay();
        InitializeTraits();
        InitializeCloseFear();

        InitializeZLevelFear();

        _fearSourceQuery = GetEntityQuery<FearSourceComponent>();
        _fearQuery = GetEntityQuery<FearComponent>();
    }

    /// <summary>
    /// Обрабатывает событие, когда игрок посмотрел на источник страха.
    /// Повышает уровень страха до указанного у источника уровня.
    /// </summary>
    private void OnEntityLookedAt(Entity<FearComponent> ent, ref EntityLookedAtEvent args)
    {
        if (!_mobState.IsAlive(ent))
            return;

        // Проверка на видимость.
        // Это нужно, чтобы можно было не пугаться через стекло, например.
        // Это будет использовано, например, у ученых, которые 100 лет видели сцп через стекла и не должны пугаться.
        if (!_proximity.IsRightType(args.Target, ent, ent.Comp.SeenBlockerLevel))
            return;

        if (!args.Target.Comp.AlreadyLookedAt.TryGetValue(GetNetEntity(ent), out var lastSeenTime))
            return;

        // Первая проверка существует для ситуаций, когда длина раунда меньше нужного времени, чтобы испугаться снова.
        // Если ее не будет, персонаж не будет пугаться при виде страшного объекта в первые N минут раунда
        if (_timing.CurTime > ent.Comp.TimeToGetScaredAgainOnLookAt
            && _timing.CurTime < lastSeenTime + ent.Comp.TimeToGetScaredAgainOnLookAt)
            return;

        if (!_fearSourceQuery.TryComp(args.Target, out var source))
            return;

        if (source.UponSeenState == FearState.None)
            return;

        // Если текущий уровень страха выше, чем тот, что мы хотим поставить,
        // то мы не должны его ставить.
        if (ent.Comp.State > source.UponSeenState)
            return;

        // Если в списке фобий персонажа нет фобии, которой является источник, то мы ее не боимся
        if (source.PhobiaType.HasValue && !ent.Comp.Phobias.Contains(source.PhobiaType.Value))
            return;

        if (!TrySetFearLevel(ent.AsNullable(), source.UponSeenState))
            return;

        AddSeenFearMood(ent, 1f);
        HighLightAllVisibleFears(ent);
    }

    /// <summary>
    /// Обрабатывает событие изменения уровня страха у персонажа.
    /// </summary>
    private void OnFearStateChanged(Entity<FearComponent> ent, ref FearStateChangedEvent args)
    {
        if (args.NewState != args.OldState)
            PlayFearStateSound(ent, args.OldState);

        // Добавляем геймплейные проблемы, завязанный на уровне страха
        ManageShootingProblems(ent);
        ManageStateBasedMood(ent);
        ManageFallOff(ent);

        // Проверка на то, что уровень понизился -> мы успокоились.
        // Геймплейные штуки ниже не нужно триггерить.
        if (!IsIncreasing(args.NewState, args.OldState))
            return;

        ManageJitter(ent);
        ManageAdrenaline(ent);
        TryScream(ent);
    }

    protected virtual void OnShutdown(Entity<FearComponent> ent, ref ComponentShutdown args)
    {
        CleanupFear(ent);
    }

    protected virtual void OnRejuvenate(Entity<FearComponent> ent, ref RejuvenateEvent args)
    {
        ResetFear(ent);
    }

    /// <summary>
    /// Обрабатывает событие осмотра персонажа с компонентом страха.
    /// Позволяет увидеть текущий уровень страха, посмотрев на человека, в ИЦ форме.
    /// </summary>
    private void OnExamine(Entity<FearComponent> ent, ref ExaminedEvent args)
    {
        var isDead = _mobState.IsDead(ent);

        var text = isDead
            ? GetDeadExamineText(ent, ent.Comp.State)
            : GetExamineText(ent, ent.Comp.State);

        if (text == null)
            return;

        args.PushMarkup(text, -1000);
    }

    /// <summary>
    /// Sets the fear state and retained effect strength. A stronger exposure at the same state
    /// refreshes persistent penalties without replaying the reactions to a rising state.
    /// </summary>
    public bool TrySetFearLevel(Entity<FearComponent?> ent, FearState state, float strength = 1f)
    {
        if (!Resolve(ent, ref ent.Comp))
            return false;

        if (strength != 1f && strength != ZLevelFearStrength)
            return false;

        if (state == FearState.None)
            strength = 1f;

        if (ent.Comp.State == state && strength <= ent.Comp.EffectStrength)
            return false;

        var entity = (ent, ent.Comp);
        var ev = new FearStateChangedEvent(state, ent.Comp.State);

        ent.Comp.State = state;
        ent.Comp.EffectStrength = strength;

        // TODO: Реализовать версию метода использующую RaiseNetworkEvent для вызова с сервера адекватной синхронизации
        // Или перенести все в Shared, но это не от меня зависит.
        RaiseLocalEvent(ent, ref ev);

        SetFearBasedShaderStrength(entity);
        SetNextCalmDownTime(entity);

        DirtyField(ent, ent.Comp, nameof(FearComponent.State));
        DirtyField(ent, ent.Comp, nameof(FearComponent.EffectStrength));

        return true;
    }

    /// <summary>
    /// Устанавливает силу шейдеров в зависимости от текущего уровня страха.
    /// </summary>
    private void SetFearBasedShaderStrength(Entity<FearComponent> ent)
    {
        var grainStrength = ent.Comp.FearBasedGrainStrength[ent.Comp.State] * ent.Comp.EffectStrength;
        var vignetteStrength = ent.Comp.FearBasedVignetteStrength[ent.Comp.State] * ent.Comp.EffectStrength;

        _shaderStrength.TrySetAdditionalStrength<GrainOverlayComponent>(ent.Owner, grainStrength);
        _shaderStrength.TrySetAdditionalStrength<VignetteOverlayComponent>(ent.Owner, vignetteStrength);

        ent.Comp.CurrentFearBasedShaderStrength[nameof(GrainOverlayComponent)] = grainStrength;
        ent.Comp.CurrentFearBasedShaderStrength[nameof(VignetteOverlayComponent)] = vignetteStrength;

        DirtyField(ent, ent.Comp, nameof(FearComponent.CurrentFearBasedShaderStrength));
    }

    private void ResetFear(Entity<FearComponent> ent)
    {
        TrySetFearLevel(ent.AsNullable(), FearState.None);
        CleanupFear(ent);
    }

    private void CleanupFear(Entity<FearComponent> ent)
    {
        ClearCloseFear(ent);

        RemoveSeenFearMood(ent);
        WipeMood(ent);
    }

    private void RemoveSeenFearMood(EntityUid uid)
    {
        RaiseLocalEvent(uid, new MoodRemoveEffectEvent(MoodFearSourceSeen));
        RaiseLocalEvent(uid, new MoodRemoveEffectEvent(MoodHalfFearSourceSeen));
        if (_fearQuery.TryComp(uid, out var fear))
        {
            fear.SeenMoodStrength = 0f;
            fear.SeenMoodExpiry = TimeSpan.Zero;
        }
    }

    private void AddSeenFearMood(Entity<FearComponent> ent, float strength)
    {
        if (_timing.CurTime < ent.Comp.SeenMoodExpiry && ent.Comp.SeenMoodStrength >= strength)
            return;

        // Разные ID не позволяют таймеру старого половинного эффекта снять новый полный эффект.
        RemoveSeenFearMood(ent);
        var effect = strength < 1f ? MoodHalfFearSourceSeen : MoodFearSourceSeen;
        AddNegativeMoodEffect(ent, effect);
        ent.Comp.SeenMoodStrength = strength;
        ent.Comp.SeenMoodExpiry = _timing.CurTime + TimeSpan.FromSeconds(120);
    }

    private void RemoveCloseFearMood(EntityUid uid)
    {
        RaiseLocalEvent(uid, new MoodRemoveEffectEvent(MoodSourceClose));
    }

    /// <summary>
    /// Устанавливает для шейдера параметры силы.
    /// Сила зависит от расстояния до источника страха и параметров самого источника.
    /// </summary>
    /// <param name="ent">Сущность, с компонентами силы шейдера</param>
    /// <param name="currentRange">Текущее расстояние сущности до источника страха</param>
    /// <param name="maxRange">Расстояние, на котором начинаются увеличиваться параметры шейдера</param>
    /// <param name="parameters">Параметры силы шейдера. Минимальное означает силу шейдера на входе в <see cref="maxRange"/>,
    /// максимальное силу при максимальном приближении к источнику страха</param>
    /// <param name="type"> <see cref="LineOfSightBlockerLevel"/> </param>
    /// <param name="fear">Компонент страха</param>
    /// <typeparam name="T">Компонент с настройками шейдера</typeparam>
    private void SetRangeBasedShaderStrength<T>(Entity<T?> ent,
        float currentRange,
        float maxRange,
        MinMaxExtended parameters,
        LineOfSightBlockerLevel type,
        FearComponent? fear = null,
        float effectStrength = 1f)
        where T : IShaderStrength, IComponent
    {
        if (!Resolve(ent, ref fear))
            return;

        var strength = CalculateShaderStrength(currentRange, maxRange, parameters) * effectStrength;

        if (type == LineOfSightBlockerLevel.Transparent)
            strength /= fear.TransparentStrengthDecreaseFactor;

        SetShaderStrength(ent, fear, strength);
    }

    /// <summary>
    /// Устанавливает силу шейдера, учитывая текущий уровень страха и его потребности в силе.
    /// </summary>
    /// <typeparam name="T">Компонент настроек, контролирующий силу шейдера</typeparam>
    private void SetShaderStrength<T>(Entity<T?> ent, FearComponent? fear, float strength)
        where T : IShaderStrength, IComponent
    {
        if (!Resolve(ent, ref fear))
            return;

        var actualStrength = GetActualStrength<T>(fear, strength);
        actualStrength /= GetDrunkModifier(ent);

        if (IsStrengthSimilar(ent, actualStrength))
            return;

        _shaderStrength.TrySetAdditionalStrength(ent, actualStrength);
    }

    /// <summary>
    /// Проверяет, является ли текущая сила шейдера близкой к текущей.
    /// </summary>
    private bool IsStrengthSimilar<T>(Entity<T?> ent, float strength)
        where T : IShaderStrength, IComponent
    {
        if (!Resolve(ent, ref ent.Comp))
            return true;

        // Ранний выход, чтобы не просчитывать всякое, если текущая сила уже равна подобной.
        if (MathHelper.CloseTo(ent.Comp.AdditionalStrength, strength))
            return true;

        return false;
    }
}

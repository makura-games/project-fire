using Content.Shared._Scp.Fear.Components;
using Content.Shared._Scp.Weapons.Ranged;
using Content.Shared._Sunrise.Mood;
using Content.Shared._Sunrise.Random;
using Content.Shared.Drunk;
using Content.Shared.Jittering;
using Content.Shared.Standing;
using Content.Shared.StatusEffectNew;
using Content.Shared.Stunnable;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Fear.Systems;

public abstract partial class SharedFearSystem
{
    [Dependency] private StatusEffectsSystem _effects = default!;
    [Dependency] private SharedJitteringSystem _jittering = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private RandomPredictedSystem _random = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedGunSystem _gun = default!;

    private const float BaseJitteringAmplitude = 1f;
    private const float BaseJitteringFrequency = 4f;

    private const float MinimumAlcoholModifier = 1f;
    private const float MaximumAlcoholModifier = 4f;

    private static readonly EntProtoId DrunkStatusEffect = "StatusEffectDrunk";
    private static readonly EntProtoId AdrenalineStatusEffect = "StatusEffectFearAdrenaline";

    private static readonly Dictionary<FearState, string> FearMoodStates = new()
    {
        { FearState.Anxiety, "FearStateAnxiety" },
        { FearState.Fear, "FearStateFear" },
        { FearState.Terror, "FearStateTerror" },
    };

    private static readonly Dictionary<FearState, string> HalfFearMoodStates = new()
    {
        { FearState.Anxiety, "ScpFearStateAnxietyHalf" },
        { FearState.Fear, "ScpFearStateFearHalf" },
        { FearState.Terror, "ScpFearStateTerrorHalf" },
    };

    private EntityQuery<DrunkStatusEffectComponent> _drunkQuery;

    private void InitializeGameplay()
    {
        _drunkQuery = GetEntityQuery<DrunkStatusEffectComponent>();

        SubscribeLocalEvent<ActiveFearFallOffComponent, MapInitEvent>(OnFallOffMapInit);
        SubscribeLocalEvent<ActiveFearFallOffComponent, MoveEvent>(OnFallOffMove);
    }

    private void OnFallOffMapInit(Entity<ActiveFearFallOffComponent> ent, ref MapInitEvent args)
    {
        SetNextFallOffTime(ent);
    }

    private void OnFallOffMove(Entity<ActiveFearFallOffComponent> ent, ref MoveEvent args)
    {
        if (_timing.CurTime < ent.Comp.FallOffNextCheckTime)
            return;

        SetNextFallOffTime(ent); // Даже если не прокнет, то время все равно должно устанавливаться

        if (!_random.ProbForEntity(ent, ent.Comp.FallOffChance))
            return;

        if (_standing.IsDown(ent.Owner))
            return;

        _stun.TryAddParalyzeDuration(ent.Owner, ent.Comp.FallOffTime);
    }

    /// <summary>
    /// Регулирует проблемы со стрельбой при увеличении страха
    /// </summary>
    private void ManageShootingProblems(Entity<FearComponent> ent)
    {
        if (ent.Comp.State == FearState.None)
        {
            SetSpreadParameters(ent, 1f, 1f); // Убирает модификаторы, скручивая их до 1
            return;
        }

        var modifier = ent.Comp.FearBasedSpreadAngleModifier[ent.Comp.State];
        if (ent.Comp.EffectStrength == 1f)
        {
            SetSpreadParameters(ent, modifier, modifier);
            return;
        }
        var spread = EnsureComp<DispersingShotSourceComponent>(ent);
        // Ослабляем только надбавку страха, сохраняя базовую неточность стрелка.
        var angle = spread.DefaultAngleIncreaseModifier +
                    (Math.Max(spread.DefaultAngleIncreaseModifier, modifier) - spread.DefaultAngleIncreaseModifier) * ent.Comp.EffectStrength;
        var maxAngle = spread.DefaultMaxAngleMultiplier +
                       (Math.Max(spread.DefaultMaxAngleMultiplier, modifier) - spread.DefaultMaxAngleMultiplier) * ent.Comp.EffectStrength;
        SetSpreadParameters(ent, angle, maxAngle);
    }

    private void SetSpreadParameters(EntityUid uid, float angleIncrease, float maxAngle)
    {
        var component = EnsureComp<DispersingShotSourceComponent>(uid);
        component.AngleIncreaseMultiplier = angleIncrease;
        component.MaxAngleMultiplier = maxAngle;

        Dirty(uid, component);

        if (_hands.TryGetActiveItem(uid, out var held) && HasComp<GunComponent>(held))
            _gun.RefreshModifiers(held.Value);
    }

    /// <summary>
    /// Заставляет сущность трястись от страха.
    /// Параметры тряски зависят от уровня страха
    /// </summary>
    private void ManageJitter(Entity<FearComponent> ent)
    {
        // При ступоре и обмороке персонаж не должен трястись
        if (_fearFaintingQuery.HasComp(ent) || _fearStuporQuery.HasComp(ent))
            return;

        if (MathHelper.CloseTo(ent.Comp.BaseJitterTime, 0f))
            return;

        // Значения будут коррелировать с текущем уровнем страха
        var genericModifier = GetGenericFearBasedModifier(ent.Comp.State);
        var alcoholModifier = GetDrunkModifier(ent);

        var time = ent.Comp.BaseJitterTime * genericModifier * ent.Comp.EffectStrength / alcoholModifier;
        var amplitude = BaseJitteringAmplitude * genericModifier / alcoholModifier;
        var frequency = BaseJitteringFrequency * genericModifier / alcoholModifier;

        _jittering.DoJitter(ent, TimeSpan.FromSeconds(time), false, amplitude, frequency);
    }

    /// <summary>
    /// Вводит адреналин в кровь сущности, количество зависит от уровня страха.
    /// </summary>
    private void ManageAdrenaline(Entity<FearComponent> ent)
    {
        if (MathHelper.CloseTo(ent.Comp.AdrenalineBaseTime, 0f))
            return;

        var modifier = GetGenericFearBasedModifier(ent.Comp.State);
        var time = TimeSpan.FromSeconds(ent.Comp.AdrenalineBaseTime * modifier * ent.Comp.EffectStrength);

        _effects.TryAddStatusEffectDuration(ent, AdrenalineStatusEffect, time);
    }

    /// <summary>
    /// Выставляет модификаторы настроения, зависящие от уровня страха
    /// </summary>
    private void ManageStateBasedMood(Entity<FearComponent> ent)
    {
        if (ent.Comp.State == FearState.None)
            WipeMood(ent);

        var states = ent.Comp.EffectStrength < 1f ? HalfFearMoodStates : FearMoodStates;
        if (!states.TryGetValue(ent.Comp.State, out var moodEffect))
            return;

        AddNegativeMoodEffect(ent, moodEffect);
    }

    /// <summary>
    /// Убирает все стандартные модификаторы настроения, зависящие от уровня страха.
    /// </summary>
    /// <param name="uid"></param>
    private void WipeMood(EntityUid uid)
    {
        foreach (var effect in FearMoodStates.Values)
        {
            RaiseLocalEvent(uid, new MoodRemoveEffectEvent(effect));
        }
        foreach (var effect in HalfFearMoodStates.Values)
        {
            RaiseLocalEvent(uid, new MoodRemoveEffectEvent(effect));
        }
    }

    /// <summary>
    /// Вызывает эффект негативного влияния на настроение.
    /// Сила эффекта зависит от уровня алкоголя в крови сущности, алкоголь делает негативные эффекты слабее
    /// </summary>
    protected void AddNegativeMoodEffect(EntityUid uid, string effect, float strength = 1f)
    {
        var drunkModifier = Math.Clamp(1f / GetDrunkModifier(uid), 0f, 1f);
        RaiseLocalEvent(uid, new MoodEffectEvent(effect, drunkModifier * strength));
    }

    /// <summary>
    /// Получает модификатор, зависящий от силы алкоголя в крови сущности
    /// </summary>
    private float GetDrunkModifier(EntityUid uid)
    {
        if (!_effects.TryGetStatusEffect(uid, DrunkStatusEffect, out var drunkEffect))
            return 1f;

        if (!_drunkQuery.TryComp(drunkEffect, out var drunk))
            return 1f;

        var normalized = Math.Clamp(drunk.CurrentBoozePower / 50f, MinimumAlcoholModifier, MaximumAlcoholModifier);

        return normalized;
    }

    protected virtual void TryScream(Entity<FearComponent> ent) {}

    private void ManageFallOff(Entity<FearComponent> ent)
    {
        if (ent.Comp.State >= ent.Comp.FallOffRequiredState)
        {
            var comp = EnsureComp<ActiveFearFallOffComponent>(ent);
            comp.FallOffChance = ent.Comp.FallOffChance * ent.Comp.EffectStrength;
            Dirty(ent, comp);
        }
        else
        {
            RemComp<ActiveFearFallOffComponent>(ent);
        }
    }

    /// <summary>
    /// Устанавливает следующее время возможности запнуться.
    /// </summary>
    private void SetNextFallOffTime(Entity<ActiveFearFallOffComponent> ent)
    {
        ent.Comp.FallOffNextCheckTime = _timing.CurTime + ent.Comp.FallOffCheckInterval;
        Dirty(ent);
    }
}

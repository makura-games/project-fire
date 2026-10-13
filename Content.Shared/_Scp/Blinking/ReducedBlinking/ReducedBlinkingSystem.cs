using Content.Shared._Sunrise.Random;
using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._Scp.Blinking.ReducedBlinking;

// TODO: Переделать на химикат и дать возможно варить, используя реагент 173 и нечто из синтезатора реагентов.
// TODO: Добавить звук закапывания капель.
// TODO: Анхардкод: Перенос значений в компоненты
public abstract partial class SharedReducedBlinkingSystem : EntitySystem
{
    [Dependency] private SharedBlinkingSystem _blinking = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private UseDelaySystem _useDelay = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private RandomPredictedSystem _random = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] protected IGameTiming Timing = default!;

    private const float ToleranceReduceUseLimit = 90.0f;
    private const float MinReducedBlinkingEffectiveness = 0.15f;
    private const float MinToleranceIncrease = 15f;
    private const float MaxToleranceIncrease = 25f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ReducedBlinkingComponent, AfterInteractEvent>(OnInteract);
        SubscribeLocalEvent<ReducedBlinkingComponent, EyeDropletsUsedDoAfterEvent>(OnSuccess);
    }

    private void OnInteract(Entity<ReducedBlinkingComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled)
            return;

        if (_useDelay.IsDelayed(ent.Owner))
            return;

        if (!HasComp<BlinkableComponent>(args.Target))
            return;

        var doAfterArgs = new DoAfterArgs(EntityManager, args.User, ent.Comp.ApplicationTime, new EyeDropletsUsedDoAfterEvent(), ent, args.Target, ent)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
        };

        args.Handled = _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnSuccess(Entity<ReducedBlinkingComponent> ent, ref EyeDropletsUsedDoAfterEvent args)
    {
        if (args.Target == null)
            return;

        var target = args.Target.Value;

        if (!TryComp<BlinkableComponent>(target, out var blinkable))
            return;

        if (_blinking.AreEyesClosedManually(target))
        {
            _popup.PopupPredicted(Loc.GetString("eye-droplets-failed", ("name", Name(target))), ent, ent);
            return;
        }

        if (blinkable.ReducedBlinkingTolerance >= ToleranceReduceUseLimit)
        {
            _popup.PopupPredicted(Loc.GetString("eye-droplets-tolerance-too-high"), ent, ent, PopupType.LargeCaution);
            return;
        }

        var effectiveness = GetReducedBlinkingEffectiveness(blinkable.ReducedBlinkingTolerance);

        var firstBonusTime = ent.Comp.FirstBlinkingBonusTime * effectiveness;
        var otherBonusTime = ent.Comp.OtherBlinkingBonusTime * effectiveness;
        var bonusDuration = ent.Comp.OtherBlinkingBonusDuration * effectiveness;

        var comp = new ActiveReducedBlinkingUserComponent
        {
            BlinkingIntervalBonus = bonusDuration,
            FirstBonusEndTime = Timing.CurTime + firstBonusTime,
            AllBonusEndTime = Timing.CurTime + otherBonusTime,
        };

        AddComp(target, comp, true);
        Dirty(target, comp);

        blinkable.AdditionalBlinkingTime += firstBonusTime;
        blinkable.ReducedBlinkingTolerance = MathF.Min(
            100f,
            blinkable.ReducedBlinkingTolerance + _random.NextFloatForEntity(target, MinToleranceIncrease, MaxToleranceIncrease));

        DirtyField(target, blinkable, nameof(BlinkableComponent.AdditionalBlinkingTime));
        DirtyField(target, blinkable, nameof(BlinkableComponent.ReducedBlinkingTolerance));

        _blinking.ResetBlink(target);
        _useDelay.TryResetDelay(ent);

        if (ent.Comp.UseSound != null)
            _audio.PlayPredicted(ent.Comp.UseSound, ent, target);

        _popup.PopupPredicted(Loc.GetString("eye-droplets-used", ("name", Identity.Name(target, EntityManager))), ent, ent);

        // Уменьшаем количество оставшихся использований
        ent.Comp.UsageCount--;

        // Удаляем предмет, если использований не осталось
        if (ent.Comp.UsageCount <= 0 && _net.IsServer)
            QueueDel(ent);
    }

    private static float GetReducedBlinkingEffectiveness(float tolerance)
    {
        var normalized = Math.Clamp(tolerance / 100f, 0f, 1f);
        return 1f - normalized * (1f - MinReducedBlinkingEffectiveness);
    }
}

[Serializable, NetSerializable]
public sealed partial class EyeDropletsUsedDoAfterEvent : SimpleDoAfterEvent;

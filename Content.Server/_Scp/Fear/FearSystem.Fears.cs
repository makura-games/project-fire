using System.Runtime.InteropServices;
using Content.Server._Scp.Shaders.Highlighting;
using Content.Shared._Scp.Blinking;
using Content.Shared._Scp.Fear;
using Content.Shared._Scp.Fear.Components;
using Content.Shared._Scp.Fear.Components.Fears;
using Content.Shared._Scp.Helpers;
using Content.Shared._Scp.Watching;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;

namespace Content.Server._Scp.Fear;

public sealed partial class FearSystem
{
    [Dependency] private HighlightSystem _highlight = default!;
    [Dependency] private EyeWatchingSystem _watching = default!;
    [Dependency] private ScpHelpers _helpers = default!;
    [Dependency] private MobStateSystem _mob = default!;

    private const string MoodSomeoneDiedOnMyEyes = "FearSomeoneDiedOnMyEyes";
    private const string MoodHemophobicBleeding = "FearHemophobicBleeding";
    private const string MoodHemophobicSeeBlood = "FearHemophobicSeeBlood";

    private static readonly TimeSpan HemophobiaCheckCooldown = TimeSpan.FromSeconds(0.5f);
    private TimeSpan _nextHemophobiaCheck = TimeSpan.Zero;

    private readonly List<EntityUid> _hemophobiaBloodList = [];

    private void InitializeFears()
    {
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);

        SubscribeLocalEvent<HemophobiaComponent, FearCalmDownAttemptEvent>(OnCalmDown);
    }

    /// <summary>
    /// Обрабатывает событие смерти персонажей.
    /// Если персонаж умер, он становится страшным.
    /// Если воскрес, перестает
    /// </summary>
    private void OnMobStateChanged(MobStateChangedEvent ev)
    {
        if (!HasComp<HumanoidProfileComponent>(ev.Target))
            return;

        var activated = ev.NewMobState == MobState.Dead;
        var toggleUsed = new ItemToggledEvent(false, activated, null);
        RaiseLocalEvent(ev.Target, ref toggleUsed);

        // Если activated = true, значит человек умер.
        // Поэтому код ниже требует true, так как реализует логику для смерти.
        if (!activated)
            return;

        using var realWatchers = ListPoolEntity<BlinkableComponent>.Rent();
        if (!_watching.TryGetWatchers(ev.Target, realWatchers.Value, flags: LookupFlags.Dynamic))
            return;

        foreach (var uid in realWatchers.Value)
        {
            // Убийца не будет печалиться смерти убитого
            if (uid.Owner == ev.Origin)
                continue;

            AddNegativeMoodEffect(uid, MoodSomeoneDiedOnMyEyes);
        }
    }

    /// <summary>
    /// Обрабатывает страх гемофобов.
    /// Проверяет количество окружающей крови и в зависимости от этого пугает персонажей.
    /// </summary>
    private void UpdateHemophobia()
    {
        if (_timing.CurTime < _nextHemophobiaCheck)
            return;

        var query = EntityQueryEnumerator<HemophobiaComponent, FearComponent, MobStateComponent>();

        while (query.MoveNext(out var uid, out var hemophobia, out var fear, out var mob))
        {
            if (!_mob.IsAlive(uid, mob))
                continue;

            _hemophobiaBloodList.Clear();
            var bloodAmount = _helpers.GetAroundSolutionVolume(uid, hemophobia.Reagent, _hemophobiaBloodList);
            var requiredBloodAmount = hemophobia.BloodRequiredPerState[fear.State];

            if (bloodAmount <= requiredBloodAmount)
                continue;

            var fearEntity = (uid, fear);

            if (!TrySetFearLevel(fearEntity, GetHemophobiaFearState(hemophobia, bloodAmount)))
                continue;

            _highlight.NetHighlightAll(CollectionsMarshal.AsSpan(_hemophobiaBloodList), uid);
            AddNegativeMoodEffect(uid, MoodHemophobicSeeBlood);
        }

        _nextHemophobiaCheck = _timing.CurTime + HemophobiaCheckCooldown;
    }

    /// <summary>
    /// Получает уровень страха, соответствующий текущему количеству крови вокруг.
    /// </summary>
    private static FearState GetHemophobiaFearState(HemophobiaComponent component, FixedPoint2 bloodAmount)
    {
        var result = FearState.None;

        foreach (var kvp in component.SortedBloodRequiredPerState)
        {
            if (bloodAmount >= kvp.Value)
                result = kvp.Key;
            else
                break;
        }

        return result;
    }

    /// <summary>
    /// Проверяет, может ли сущность с гемофобией успокоиться в данный момент.
    /// Для этого рядом не должно быть большого количества крови
    /// </summary>
    private void OnCalmDown(Entity<HemophobiaComponent> ent, ref FearCalmDownAttemptEvent args)
    {
        var requiredBloodToCancel = ent.Comp.BloodRequiredPerState[args.NewState];

        if (_helpers.IsAroundSolutionVolumeGreaterThan(ent, ent.Comp.Reagent, requiredBloodToCancel))
            args.Cancel();
    }
}

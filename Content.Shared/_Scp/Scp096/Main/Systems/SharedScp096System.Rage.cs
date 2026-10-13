using Content.Shared._Scp.Audio;
using Content.Shared._Scp.Scp096.Main.Components;
using Content.Shared._Scp.Shaders.SinCity;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage.Systems;
using Content.Shared.Jittering;
using Content.Shared.StatusEffectNew;
using Content.Shared.Stunnable;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Scp096.Main.Systems;

public abstract partial class SharedScp096System
{
    /*
     * Часть системы, отвечающая за состояние ярости и пред-яростное состояние скромника.
     */

    [Dependency] private StatusEffectsSystem _statusEffects = default!;
    [Dependency] private SharedStaminaSystem _stamina = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedJitteringSystem _jittering = default!;
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;

    private static readonly EntProtoId StunnedEffect = "StatusEffectStunned";

    private void InitializeRage()
    {
        SubscribeLocalEvent<ActiveScp096HeatingUpComponent, ComponentStartup>(OnHeatingUpStart);
        SubscribeLocalEvent<ActiveScp096HeatingUpComponent, ComponentShutdown>(OnHeatingUpShutdown);

        SubscribeLocalEvent<ActiveScp096RageComponent, ComponentStartup>(OnRageStart);
        SubscribeLocalEvent<ActiveScp096RageComponent, ComponentShutdown>(OnRageShutdown);
    }

    #region Event handlers

    protected virtual void OnHeatingUpStart(Entity<ActiveScp096HeatingUpComponent> ent, ref ComponentStartup args)
    {
        TryBreakOutOfHold(ent.Owner);

        // Устанавливаем время окончания пред-агр состояния
        ent.Comp.RageHeatUpEnd = _timing.CurTime + ent.Comp.RageHeatUp;

        // Убираем компонент, отвечающий за шейдер для обычного состояния
        RemComp<SinCityOverlayComponent>(ent);

        // Устанавливаем звук пред-агр состояния
        UpdateAudio(ent.Owner, ent.Comp.TriggerSound);

        // Если скромник был застанен или сидит - убираем это
        var totalDamage = _stamina.GetStaminaDamage(ent);
        _statusEffects.TryRemoveStatusEffect(ent, StatusEffectSleep);
        _statusEffects.TryRemoveStatusEffect(ent, StunnedEffect);
        _stun.TryUnstun(ent.Owner);
        _stamina.TryTakeStamina(ent, -1f * totalDamage);
        _standing.Stand(ent, force: true);

        // Заставляем трястись
        _jittering.AddJitter(ent, -10, 100);

        TryToggleRestrictions(ent.Owner, false);
        ToggleMovement(ent, false);
        TryToggleTears(ent.Owner, false);
        UpdateAppearance(ent.Owner);

        Dirty(ent);
    }

    protected virtual void OnHeatingUpShutdown(Entity<ActiveScp096HeatingUpComponent> ent, ref ComponentShutdown args)
    {
        if (_timing.ApplyingState || IsClientSide(ent))
            return;

        // Сниманием тряску
        RemComp<JitteringComponent>(ent);

        // Убираем ограничение на передвижение
        ToggleMovement(ent, true);
    }

    protected virtual void OnRageStart(Entity<ActiveScp096RageComponent> ent, ref ComponentStartup args)
    {
        TryBreakOutOfHold(ent.Owner);

        ent.Comp.RageStartTime = _timing.CurTime;
        Dirty(ent);

        UpdateAudio(ent.Owner, ent.Comp.RageSound);
        UpdateAppearance(ent.Owner);
        RefreshSpeedModifiers(ent.Owner);
    }

    protected virtual void OnRageShutdown(Entity<ActiveScp096RageComponent> ent, ref ComponentShutdown args)
    {
        if (_timing.ApplyingState || IsClientSide(ent))
            return;

        if (!TryComp<Scp096Component>(ent, out var scp096))
        {
            Log.Error($"Found entity with {nameof(ActiveScp096RageComponent)} but without {nameof(Scp096Component)}: {ToPrettyString(ent)}, prototype: {Prototype(ent)}");
            return;
        }

        scp096.TargetsCount = 0;
        Dirty(ent, scp096);

        if (_net.IsServer)
            RaiseNetworkEvent(new NetworkAmbientMusicEventStop(), ent);

        // Добавляем компонент, отвечающий за шейдер для обычного состояния
        EnsureComp<SinCityOverlayComponent>(ent);

        // Усыпляем скромника
        if (!_statusEffects.TryAddStatusEffectDuration(ent, StatusEffectSleep, ent.Comp.PacifiedTime))
        {
            // При усыплении скромника и так меняется внешний вид, нет смысла делать это несколько раз.
            // Поэтому запрашиваем обновление внешнего вида только при неуспешном усыплении
            UpdateAppearance(ent.Owner);
        }

        TryToggleRestrictions(ent.Owner, true);
        RefreshSpeedModifiers(ent.Owner, true);
        TryToggleTears(ent.Owner, true);
        UpdateAudio(ent.Owner, scp096.CrySound);
    }

    #endregion

    /// <summary>
    /// Проходится по скромникам и переводит из пред-яростного состояния в яростное, когда придет время.
    /// </summary>
    private void UpdateHeatingUp()
    {
        var query = EntityQueryEnumerator<ActiveScp096HeatingUpComponent, Scp096Component>();
        while (query.MoveNext(out var uid, out var component, out _))
        {
            if (!component.RageHeatUpEnd.HasValue)
                continue;

            if (_timing.CurTime < component.RageHeatUpEnd.Value)
                continue;

            TryStartRage(uid);
        }
    }

    /// <summary>
    /// Проходится по скромникам и проверяет, не вышел ли таймер ярости.
    /// Если да - убирает все цели и заканчивает состояние ярости, переводя скромника в сон.
    /// </summary>
    private void UpdateRage()
    {
        var query = EntityQueryEnumerator<ActiveScp096RageComponent>();
        while (query.MoveNext(out var uid, out var rage))
        {
            if (!rage.RageStartTime.HasValue)
                continue;

            var elapsedTime = _timing.CurTime - rage.RageStartTime.Value;
            if (elapsedTime < rage.RageDuration)
                continue;

            RemoveAllTargets();

            // Возможная заглушка на случай, если удаление всех сущностей произойдет НЕ в 1 тик.
            // Что приведет к проблемам.
            rage.RageStartTime = null;
            Dirty(uid, rage);
        }
    }

    /// <summary>
    /// Пытается перевести скромника в пред-агр состояние.
    /// </summary>
    /// <remarks>
    /// Не будет переводить в состояние ярости, если скромник уже находится в нем или находится в состоянии ярости.
    /// </remarks>
    /// <param name="uid"><see cref="EntityUid"/> Скромника</param>
    /// <returns>
    /// <para>True: Скромник переведен в состояние</para>
    /// False: Скромник НЕ переведен в состояние.
    /// </returns>
    private bool TryStartHeatingUp(EntityUid uid)
    {
        if (HasComp<ActiveScp096RageComponent>(uid) || HasComp<ActiveScp096WithoutFaceComponent>(uid))
            return false;

        EnsureComp<ActiveScp096HeatingUpComponent>(uid);
        return true;
    }

    /// <summary>
    /// Пытается перевести скромника в состояние ярости.
    /// </summary>
    /// <remarks>
    /// Не будет переводить в состояние ярости, если скромник уже находится в нем или находится в пред-агр состоянии.
    /// </remarks>
    /// <param name="uid"><see cref="EntityUid"/> Скромника</param>
    /// <returns>
    /// <para>True: Скромник переведен в состояние ярости</para>
    /// False: Скромник НЕ переведен в состояние ярости.
    /// </returns>
    private bool TryStartRage(EntityUid uid)
    {
        if (HasComp<ActiveScp096RageComponent>(uid) || !HasComp<ActiveScp096HeatingUpComponent>(uid))
            return false;

        RemComp<ActiveScp096HeatingUpComponent>(uid);
        EnsureComp<ActiveScp096RageComponent>(uid);

        return true;
    }
}

using System.Linq;
using Content.Shared._Scp.Blood;
using Content.Shared._Scp.Proximity;
using Content.Shared._Scp.Scp096.Main.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Jittering;
using Content.Shared.Standing;

namespace Content.Shared._Scp.Scp096.Main.Systems;

public abstract partial class SharedScp096System
{
    /*
     * Часть системы, отвечающая за способности скромника и их применение.
     */

    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ProximitySystem _proximity = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private StandingStateSystem _standing = default!;

    private void InitializeActions()
    {
        SubscribeLocalEvent<Scp096Component, Scp096CryOutEvent>(OnCryOut);

        SubscribeLocalEvent<Scp096Component, Scp096FaceSkinRipEvent>(OnFaceSkinRip);
        SubscribeLocalEvent<Scp096Component, Scp096FaceSkinRipStartDoAfterEvent>(OnFaceSkinRipDoAfter);

        SubscribeLocalEvent<Scp096Component, Scp096SitDownEvent>(OnSitDown);
    }

    private void OnCryOut(Entity<Scp096Component> ent, ref Scp096CryOutEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.CryOutDamage == null)
            return;

        if (ent.Comp.CryOutRequireBeInContainmentChamber && !IsInContainmentChamber(ent))
        {
            var message = Loc.GetString("scp096-only-in-containment-chamber");
            _popup.PopupClient(message, ent, ent);

            return;
        }

        var targets =
            _lookup.GetEntitiesInRange<DamageableComponent>(Transform(ent).Coordinates,
                    ent.Comp.CryOutRange,
                    LookupFlags.Static)
                .Where(e => IsValidForCryOutDamage(ent, e));

        var damagedAny = false;
        foreach (var target in targets)
        {
            if (_damageable.TryChangeDamage(target.Owner, ent.Comp.CryOutDamage, origin: ent))
                damagedAny = true;
        }

        // Никому не нанесли урон?
        if (!damagedAny)
            return;

        _audio.PlayPredicted(ent.Comp.CryOutSound, ent, ent);

        _jittering.AddJitter(ent, -10, 100f);
        AddToPendingJittering(ent, _timing.CurTime + ent.Comp.CryOutJitterTime);

        args.Handled = true;
    }

    private void OnFaceSkinRip(Entity<Scp096Component> ent, ref Scp096FaceSkinRipEvent args)
    {
        if (args.Handled)
            return;

        if (!ent.Comp.FaceEntity.HasValue)
            return;

        if (_scpMask.TryGetScpMask(ent, out var mask))
        {
            var message = Loc.GetString("scp096-mask-prevent-skin-rip", ("mask", Name(mask.Value)));
            _popup.PopupClient(message, ent, ent);

            return;
        }

        if (!TryGetFace(ent.AsNullable(), out var face))
            return;

        if (_mobState.IsIncapacitated(face.Value))
            return;

        var doAfterArgs = new DoAfterArgs(EntityManager,
            ent,
            ent.Comp.FaceSkinRipDoAfterTime,
            new Scp096FaceSkinRipStartDoAfterEvent(),
            ent,
            ent,
            ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            BreakOnDropItem = true,
            BreakOnHandChange = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfterArgs))
            return;

        _jittering.AddJitter(ent, -10, 100f);
        AddToPendingJittering(ent, _timing.CurTime + ent.Comp.FaceSkinRipDoAfterTime);

        args.Handled = true;
    }

    private void OnFaceSkinRipDoAfter(Entity<Scp096Component> ent, ref Scp096FaceSkinRipStartDoAfterEvent args)
    {
        if (args.Handled)
            return;

        if (args.Cancelled)
        {
            RemComp<JitteringComponent>(ent);

            // Не убираем из _pendingJitteringRemoval, потому что затраты на поиск нужного кортежа будет больше,
            // чем от временного хранения лишних данных
            return;
        }

        if (HasComp<ActiveScp096HeatingUpComponent>(ent) || HasComp<ActiveScp096RageComponent>(ent))
            return;

        if (!ent.Comp.FaceEntity.HasValue)
        {
            Log.Error("Failed to get SCP-096 face entity!");
            return;
        }

        var damagedAny = _damageable.TryChangeDamage(ent.Comp.FaceEntity.Value,
            ent.Comp.FaceSkinRipDamageToFace,
            ignoreResistances: true,
            origin: ent,
            ignoreVariance: true,
            ignoreGlobalModifiers: true);

        if (!damagedAny)
            return;

        var message = Loc.GetString("scp096-face-skin-rip", ("name", Identity.Name(ent, EntityManager)));
        _popup.PopupPredicted(message, ent, ent);

        _audio.PlayPredicted(ent.Comp.FaceSkinRipDamageToFaceSound, ent, ent);
        SpawnBlood(ent.Owner);

        args.Handled = true;
    }

    private void OnSitDown(Entity<Scp096Component> ent, ref Scp096SitDownEvent args)
    {
        if (args.Handled)
            return;

        if (HasComp<ActiveScp096HeatingUpComponent>(ent) || HasComp<ActiveScp096RageComponent>(ent))
            return;

        var sat = _standing.IsDown(ent.Owner);
        args.Handled = TryToggleSit(ent.AsNullable(), sat);
    }

    /// <summary>
    /// Переключает положение скромника из сидячего в стоячее. Или наоборот.
    /// </summary>
    /// <param name="ent">Сущность, которая будет менять свое состояние</param>
    /// <param name="haveToStand">Мы хотим встать? Если да -> true. Иначе false</param>
    /// <returns>Получилось сменить состояние или нет</returns>
    private bool TryToggleSit(EntityUid ent, bool haveToStand)
    {
        // Проверка на эквивалентность состояния.
        // Пример - мы хотим встать, но уже стоим -> выходим из метода.
        if (!_standing.IsDown(ent) == haveToStand)
            return false;

        var successful = haveToStand
            ? _standing.Stand(ent)
            : _standing.Down(ent, false);

        if (!successful)
        {
            if (_timing.IsFirstTimePredicted)
                Log.Error($"Failed to change SCP-096 standing state! Entity {ToPrettyString(ent)}, have to stand: {haveToStand}");

            return false;
        }

        return true;
    }

    /// <summary>
    /// Проверяет, должна ли сущность получить урон от способности плача.
    /// </summary>
    /// <param name="ent">Скромник, который использует способность</param>
    /// <param name="target">Сущность для проверки</param>
    /// <returns>Должна ли сущность получить урон. Да или нет</returns>
    private bool IsValidForCryOutDamage(Entity<Scp096Component> ent, EntityUid target)
    {
        if (!_whitelist.CheckBoth(target, ent.Comp.CryOutBlacklist, ent.Comp.CryOutWhitelist))
            return false;

        if (!_proximity.IsRightType(ent, target, LineOfSightBlockerLevel.None, out _))
            return false;

        return true;
    }

    /// <summary>
    /// Создает разбрызгивающиеся кровавые частички рядом с сущностью, идущие от самой сущности.
    /// </summary>
    /// <param name="ent">Сущность, которая будет разбрызгивать свою кровь</param>
    protected virtual void SpawnBlood(Entity<BloodSplattererComponent?> ent) { }
}

using Content.Shared._Scp.Fear;
using Content.Shared._Scp.Fear.Systems;
using Content.Shared._Scp.Proximity;
using Content.Shared._Scp.Scp096.Main.Components;
using Content.Shared._Scp.Scp096.Protection;
using Content.Shared._Scp.Scp106.Components;
using Content.Shared._Scp.Watching;
using Content.Shared._Scp.Watching.FOV;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using JetBrains.Annotations;
using Robust.Shared.Network;

namespace Content.Shared._Scp.Scp096.Main.Systems;

public abstract partial class SharedScp096System
{
    /*
     * Часть системы, отвечающая за целей скромника и их обработку.
     */

    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedFearSystem _fear = default!;
    [Dependency] private FieldOfViewSystem _fov = default!;
    [Dependency] private EyeWatchingSystem _watching = default!;
    [Dependency] private INetManager _net = default!;

    protected EntityQuery<Scp096ProtectionComponent> ProtectionQuery;

    private void InitializeTargets()
    {
        SubscribeLocalEvent<Scp096TargetComponent, DamageChangedEvent>(OnTargetDamageChanged);
        SubscribeLocalEvent<Scp096TargetComponent, MobStateChangedEvent>(OnTargetMobStateChanged);
        SubscribeLocalEvent<Scp096TargetComponent, MapUidChangedEvent>(OnMapChanged);

        SubscribeLocalEvent<Scp096TargetComponent, ComponentStartup>(OnTargetStartup);
        SubscribeLocalEvent<Scp096TargetComponent, ComponentShutdown>(OnTargetShutdown);

        ProtectionQuery = GetEntityQuery<Scp096ProtectionComponent>();
    }

    private void OnTargetDamageChanged(Entity<Scp096TargetComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased)
            return;

        if (!Scp096Query.HasComp(args.Origin))
            return;

        ent.Comp.AlreadyAppliedDamage += args.DamageDelta?.GetTotal() ?? FixedPoint2.Zero;
        Dirty(ent);

        // Убираем цель только после нанесения суммарно нужного количества урона
        if (ent.Comp.AlreadyAppliedDamage >= ent.Comp.TotalDamageToStop)
            RemComp<Scp096TargetComponent>(ent);
    }

    private void OnTargetMobStateChanged(Entity<Scp096TargetComponent> ent, ref MobStateChangedEvent args)
    {
        if (!_mobState.IsDead(ent))
            return;

        if (!Scp096Query.HasComp(args.Origin))
            return;

        // Сообщаем игроку, что нужно продолжать разрывать цель.
        _popup.PopupClient(Loc.GetString("scp096-keep-attacking"), ent, args.Origin, PopupType.Medium);
    }

    private void OnMapChanged(Entity<Scp096TargetComponent> ent, ref MapUidChangedEvent args)
    {
        // Если цель оказалась в измерении SCP-106 - убираем ее из списка целей
        if (HasComp<Scp106BackRoomMapComponent>(args.NewMap))
            RemComp<Scp096TargetComponent>(ent);
    }

    protected virtual void OnTargetStartup(Entity<Scp096TargetComponent> ent, ref ComponentStartup args)
    {
        if (_timing.ApplyingState || IsClientSide(ent))
            return;

        var becameTarget = false;

        var query = EntityQueryEnumerator<Scp096Component>();
        while (query.MoveNext(out var uid, out var scp096))
        {
            if (!TryStartHeatingUp(uid))
                continue;

            scp096.TargetsCount++;
            Dirty(uid, scp096);
            becameTarget = true;
        }

        // Если не получилось стать целью - убираем компонент
        if (!becameTarget)
        {
            ent.Comp.DecreaseTargetCountOnShutdown = false;
            Dirty(ent);
            RemComp<Scp096TargetComponent>(ent);

            return;
        }

        _fear.TrySetFearLevel(ent.Owner, FearState.Terror);
    }

    protected virtual void OnTargetShutdown(Entity<Scp096TargetComponent> ent, ref ComponentShutdown args)
    {
        if (_timing.ApplyingState || IsClientSide(ent))
            return;

        var query = EntityQueryEnumerator<Scp096Component>();
        while (query.MoveNext(out var uid, out var scp096))
        {
            if (ent.Comp.DecreaseTargetCountOnShutdown)
            {
                scp096.TargetsCount--;
                Dirty(uid, scp096);
            }

            if (scp096.TargetsCount <= 0)
                RemCompDeferred<ActiveScp096RageComponent>(uid);
        }
    }

    /// <summary>
    /// Проверяет, может ли цель быть целью scp-096.
    /// Если может - добавляет ее в список целей. Возвращает полученный результат
    /// </summary>
    [PublicAPI]
    public bool TryAddTarget(Entity<Scp096Component> scp, EntityUid target, bool ignoreAngle = false, bool ignoreMask = false, bool ignoreBlinded = false)
    {
        if (!CanBeAggro(scp, ignoreMask))
            return false;

        if (!IsValidTarget(scp, target, ignoreAngle, ignoreBlinded))
            return false;

        EnsureComp<Scp096TargetComponent>(target);
        return true;
    }

    /// <summary>
    /// Убирает все текущие цели у scp-096
    /// </summary>
    private void RemoveAllTargets()
    {
        var query = EntityQueryEnumerator<Scp096TargetComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            RemComp<Scp096TargetComponent>(uid);
        }
    }

    /// <summary>
    /// Проверяет, может ли цель быть целью скромника.
    /// Включает в себя различные проверки на поле зрения, защиту и прочее.
    /// </summary>
    private bool IsValidTarget(Entity<Scp096Component> scp, EntityUid target, bool ignoreAngle = false, bool ignoreBlinded = false)
    {
        // Уже является целью?
        if (TargetQuery.HasComp(target))
            return false;

        // Проверяем, есть ли у цели защита от 096
        // TODO: Избавиться от проверки каждый тик.
        if (ProtectionQuery.TryComp(target, out var protection) && !_random.ProbForEntity(scp, protection.ProblemChance))
            return false;

        // Проверяем, смотрит ли 096 на цель и цель на 096
        if (!IsTargetSeeScp096(target, scp, ignoreAngle, ignoreBlinded))
            return false;

        // Если все условия выполнены, то цель валидна
        return true;
    }

    /// <summary>
    /// Проверяет, видит ли цель SCP-096.
    /// Использует особые проверки поля зрения для проверки "смотрят ли они друг-другу в лицо"
    /// </summary>
    private bool IsTargetSeeScp096(EntityUid viewer, Entity<Scp096Component> scp, bool ignoreAngle, bool ignoreBlinded = false)
    {
        // Проверяем, может ли цель вообще быть увидена.
        // Проверяет на наличие базовых компонентов и т.п.
        if (!_watching.CanBeWatched(viewer, scp))
            return false;

        // Проверяет, не слеп ли персонаж
        if (!_watching.CanSee(viewer, scp, false) && !ignoreBlinded)
            return false;

        // Если игнорируем угол, то считаем, что смотрящий видит 096
        if (ignoreAngle)
            return true;

        // Проверяем, что между скромником и целью нет сплошных препятствий, закрывающих обзор
        // TODO: Проверить, что лучше проверять первым для максимальной производительности. Сюда впихнул временно.
        if (!_proximity.IsRightType(scp, viewer, LineOfSightBlockerLevel.Transparent))
            return false;

        // Проверяем, смотрит ли 096 в лицо цели
        if (!_fov.IsInViewAngle(scp.Owner, viewer, scp.Comp.ArgoAngle))
            return false;

        // Проверяем, смотри ли цель в лицо 096
        if (!_fov.IsInViewAngle(viewer, scp.Owner, scp.Comp.ArgoAngle))
            return false;

        // Соответственно если все проверки прошли, то цель видит 096
        return true;
    }
}

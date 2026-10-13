using System.Linq;
using Content.Shared._Scp.Mobs.Components;
using Content.Shared._Scp.Other.Events;
using Content.Shared._Scp.Proximity;
using Content.Shared._Scp.Scp096.Main.Components;
using Content.Shared._Scp.ScpMask;
using Content.Shared._Scp.Watching;
using Content.Shared._Sunrise.Random;
using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.Bed.Sleep;
using Content.Shared.CombatMode;
using Content.Shared.DoAfter;
using Content.Shared.Interaction.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Lock;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Rejuvenate;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._Scp.Scp096.Main.Systems;

// TODO: Generic система для обнаружения, находится ли объект в камере содержания
/// <summary>
/// Система, регулирующая поведения и состояния скромника.
/// </summary>
public abstract partial class SharedScp096System : EntitySystem
{
    /*
     * Основная часть системы, отвечающая за самые базовые вещи.
     */

    [Dependency] private RandomPredictedSystem _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private MovementSpeedModifierSystem _speedModifier = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private ScpMaskSystem _scpMask = default!;
    [Dependency] private SharedEntityStorageSystem _entityStorage = default!;
    [Dependency] private LockSystem _lock = default!;
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly EntProtoId StatusEffectSleep = "StatusEffectForcedSleeping";
    private static readonly SoundSpecifier StorageOpenSound = new SoundCollectionSpecifier("MetalBreak");

    // EntityQuery для быстрого доступа к компонентам
    protected EntityQuery<Scp096Component> Scp096Query;
    protected EntityQuery<ActiveScp096HeatingUpComponent> HeatingUpQuery;
    protected EntityQuery<ActiveScp096RageComponent> RageQuery;
    protected EntityQuery<Scp096TargetComponent> TargetQuery;
    protected EntityQuery<ActiveScp096WithoutFaceComponent> WithoutFaceQuery;
    protected EntityQuery<Scp096FaceComponent> FaceQuery;

    // ID алертов с лицом скромника
    protected static readonly ProtoId<AlertPrototype> IdleAlert = "Scp096Idle";
    protected static readonly ProtoId<AlertPrototype> RageAlert = "Scp096Rage";
    protected static readonly ProtoId<AlertPrototype> HeatingUpAlert = "Scp096HeatingUp";
    protected static readonly ProtoId<AlertPrototype> SleepAlert = "Scp096Sleep";
    protected static readonly ProtoId<AlertPrototype> FaceDamageAlert = "Scp096FaceDamage";

    /// <summary>
    /// Радиус поиска камеры содержания вокруг скромника.
    /// </summary>
    private const float ContainmentChamberSearchRadius = 8f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Scp096Component, SimpleEntitySeenEvent>(OnSeen);

        SubscribeLocalEvent<ProjectileHitEvent>(OnProjectileHit);
        SubscribeLocalEvent<Scp096Component, HitScanAttackedEvent>(OnHitScanHit);
        SubscribeLocalEvent<Scp096Component, AttackedEvent>(OnAttacked);
        SubscribeLocalEvent<Scp096Component, DisarmedEvent>(OnDisarmed);

        SubscribeLocalEvent<Scp096Component, AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<Scp096Component, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<Scp096Component, SleepStateChangedEvent>(OnSleepStateChanged);

        SubscribeLocalEvent<Scp096Component, ScpMaskTargetEquipAttempt>(OnMaskAttempt);
        SubscribeLocalEvent<Scp096Component, RejuvenateEvent>(OnRejuvenate);

        SubscribeLocalEvent<Scp096Component, ComponentInit>(OnInit);
        SubscribeLocalEvent<Scp096Component, ComponentShutdown>(OnShutdown);

        InitializeRage();
        InitializeTargets();
        InitializeHands();
        InitializeActions();
        InitializeHolding();
        InitializeWithoutFace();
        InitializeAppearance();
        InitializeFace();

        Scp096Query = GetEntityQuery<Scp096Component>();
        HeatingUpQuery = GetEntityQuery<ActiveScp096HeatingUpComponent>();
        RageQuery = GetEntityQuery<ActiveScp096RageComponent>();
        TargetQuery = GetEntityQuery<Scp096TargetComponent>();
        WithoutFaceQuery = GetEntityQuery<ActiveScp096WithoutFaceComponent>();
        FaceQuery = GetEntityQuery<Scp096FaceComponent>();

        Log.Level = LogLevel.Info;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateHeatingUp();
        UpdateRage();
        UpdateAnimations();
        UpdateJittering();
    }

    #region Event handlers

    private void OnSeen(Entity<Scp096Component> ent, ref SimpleEntitySeenEvent args)
    {
        TryAddTarget(ent, args.Viewer);
    }

    private void OnProjectileHit(ref ProjectileHitEvent args)
    {
        CheckRandomTargetByDamage(args.Target, args.Shooter, 1f);
    }

    private void OnHitScanHit(Entity<Scp096Component> ent, ref HitScanAttackedEvent args)
    {
        CheckRandomTargetByDamage(ent, args.User, 1f);
    }

    private void OnAttacked(Entity<Scp096Component> ent, ref AttackedEvent args)
    {
        var chance = _random.NextFloatForEntity(ent, 0.4f, 0.7f);
        CheckRandomTargetByDamage(ent, args.User, chance);
    }

    private void OnDisarmed(Entity<Scp096Component> ent, ref DisarmedEvent args)
    {
        CheckRandomTargetByDamage(ent, args.Source, 0.1f);
    }

    /// <summary>
    /// Отвечает за логику агрессии после удара по скромнику.
    /// В случае успешного прохождения проверок записывает нападающего в цели scp-096
    /// </summary>
    private void CheckRandomTargetByDamage(EntityUid target, EntityUid? attacker, float chance)
    {
        // В состоянии без лица урон это единственный способ застанить скромника.
        // А стан это единственный способ вылечить его лицо, не получив пиздюлей.
        if (WithoutFaceQuery.HasComp(target))
            return;

        if (!attacker.HasValue)
            return;

        if (!_random.ProbForEntity(attacker.Value, chance))
            return;

        if (!Scp096Query.TryComp(target, out var scp))
            return;

        TryAddTarget((target, scp), attacker.Value, true, ignoreBlinded: true);
    }

    private void OnMobStateChanged(Entity<Scp096Component> ent, ref MobStateChangedEvent args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        UpdateAppearance(ent.AsNullable());

        if (args.NewMobState == MobState.Alive)
            return;

        RemoveAllTargets();
    }

    private void OnSleepStateChanged(Entity<Scp096Component> ent, ref SleepStateChangedEvent args)
    {
        ToggleSitAnimation(ent.AsNullable(), !args.FellAsleep);
    }

    private void OnAttackAttempt(Entity<Scp096Component> ent, ref AttackAttemptEvent args)
    {
        if (!args.Target.HasValue)
            return;

        var target = args.Target.Value;

        if (!CanAttack(ent, target))
        {
            args.Cancel();
            return;
        }

        // Открываем ударом шкафчики и хранилища
        if (TryComp<EntityStorageComponent>(target, out var entityStorageComponent) && !entityStorageComponent.Open)
        {
            _lock.TryUnlock(target, ent);
            _entityStorage.OpenStorage(target, entityStorageComponent);
            _audio.PlayLocal(StorageOpenSound, ent, ent);
        }
    }

    private void OnMaskAttempt(Entity<Scp096Component> ent, ref ScpMaskTargetEquipAttempt args)
    {
        if (HasComp<ActiveScp096RageComponent>(ent) || HasComp<ActiveScp096HeatingUpComponent>(ent))
            args.Cancel();
    }

    private void OnRejuvenate(Entity<Scp096Component> ent, ref RejuvenateEvent args)
    {
        RemComp<ActiveScp096WithoutFaceComponent>(ent);
        RemComp<ActiveScp096RageComponent>(ent);
        RemComp<ActiveScp096HeatingUpComponent>(ent);

        RemoveAllTargets();
    }

    protected virtual void OnInit(Entity<Scp096Component> ent, ref ComponentInit args)
    {
        SpawnFace(ent);
        UpdateAudio(ent.AsNullable(), ent.Comp.CrySound);
        ActualizeAlert(ent);
    }

    protected virtual void OnShutdown(Entity<Scp096Component> ent, ref ComponentShutdown args)
    {
        if (_timing.ApplyingState || IsClientSide(ent))
            return;

        var query = EntityQueryEnumerator<Scp096TargetComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            RemCompDeferred<Scp096TargetComponent>(uid);
        }

        _pendingAnimations.Remove(ent);
        _pendingJitteringRemoval.Remove(ent);

        UpdateAudio(ent.AsNullable(), setDefault: false);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Проверяет, может ли scp-096 перейти в состояние ярости.
    /// Не включает проверку, что скромник уже в состоянии ярости. Так и задумано
    /// </summary>
    private bool CanBeAggro(Entity<Scp096Component> ent, bool ignoreMask = false)
    {
        if (_sleepingQuery.HasComp(ent))
            return false;

        if (_mobState.IsIncapacitated(ent))
            return false;

        // В маске мы мирные
        if (_scpMask.HasScpMask(ent) && !ignoreMask)
            return false;

        if (WithoutFaceQuery.HasComp(ent))
            return false;

        return true;
    }

    /// <summary>
    /// Обновляет модификаторы скорости передвижения scp-096 в зависимости от текущего состояния.
    /// В режиме ярости скорость большая, в обычном состоянии маленькая.
    /// </summary>
    private void RefreshSpeedModifiers(Entity<Scp096Component?> ent, bool forceDefault = false)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        float newSpeed;

        if (RageQuery.TryComp(ent, out var rage) && !forceDefault)
            newSpeed = rage.Speed;
        else if (WithoutFaceQuery.TryComp(ent, out var withoutFaceComp) && !forceDefault)
            newSpeed = withoutFaceComp.Speed;
        else
            newSpeed = ent.Comp.Speed;

        _speedModifier.ChangeBaseSpeed(ent, newSpeed, newSpeed, 20.0f);
    }

    /// <summary>
    /// Актуализирует звук, исходящий от скромника.
    /// Обновляет его в зависимости
    /// </summary>
    /// <param name="ent">Сущность, на которой будет висеть звук</param>
    /// <param name="sound">Звук, который мы хотим поставить. Если не указывать - звук выключится</param>
    /// <param name="setDefault">Если звук не указан и параметр True будет установлен стандартный звук - звук плача.</param>
    protected void UpdateAudio(Entity<Scp096Component?> ent, SoundSpecifier? sound = null, bool setDefault = true)
    {
        if (IsClientSide(ent) || !_timing.IsFirstTimePredicted || _timing.ApplyingState)
            return;

        if (!Resolve(ent, ref ent.Comp))
            return;

        if (setDefault)
            sound ??= ent.Comp.CrySound;

        ent.Comp.AudioStream = _audio.Stop(ent.Comp.AudioStream);

        if (_net.IsServer)
            ent.Comp.AudioStream = _audio.PlayPvs(sound, ent)?.Entity;

        Dirty(ent);

        // Логгирование!
        if (!Log.IsLogLevelEnabled(LogLevel.Debug))
            return;

        switch (sound)
        {
            case SoundPathSpecifier soundPath:
                Log.Debug($"Set SCP-096 sound to {soundPath.Path}");
                break;
            case SoundCollectionSpecifier soundCollection:
                Log.Debug($"Set SCP-096 sound to {soundCollection.Collection}");
                break;
            case null:
                Log.Debug("Disabled SCP-096 sound");
                break;
            default:
                Log.Error("Set SCP-096 sound to unknown sound type!");
                break;
        }
    }

    /// <summary>
    /// Проверяет, может ли скромник ударить эту сущность.
    /// Можно ударить свою цель, структуру, борга или животного.
    /// </summary>
    private bool CanAttack(Entity<Scp096Component> scp, EntityUid target)
    {
        // В состоянии содранного лица можно атаковать без ограничений
        if (HasComp<ActiveScp096WithoutFaceComponent>(scp))
            return true;

        // Атаковать можно только в состоянии агрессии или случае выше
        if (!HasComp<ActiveScp096RageComponent>(scp))
            return false;

        // Если цель не имеет компонента моргания, значит это 99% не игрок
        // И скромник имеет право это расхуярить(это структура, борг или животное)
        if (!HasComp<MobStateComponent>(target))
            return true;

        if (!HasComp<Scp096TargetComponent>(target))
            return false;

        return true;
    }

    /// <summary>
    /// Переключает возможность передвижения.
    /// </summary>
    /// <param name="uid"><see cref="EntityUid"/>, с которым будет происходить действие</param>
    /// <param name="enable"> Включить передвижение?
    /// <para>True - включает возможность передвижения, убирая блокирующие компоненты</para>
    /// False - блокирует передвижение, выдавая компоненты.
    /// </param>
    private void ToggleMovement(EntityUid uid, bool enable)
    {
        if (enable)
        {
            RemComp<BlockMovementComponent>(uid);
            RemComp<NoRotateOnInteractComponent>(uid);
        }
        else
        {
            EnsureComp<BlockMovementComponent>(uid);
            EnsureComp<NoRotateOnInteractComponent>(uid);
        }

        var finalResult = _actionBlocker.UpdateCanMove(uid);

        if (enable != finalResult)
        {
            if (_timing.IsFirstTimePredicted)
                Log.Verbose($"Movement state mismatch! Tried to set movement to {enable}, but ended up with {finalResult}. This CAN BE NORMAL if entity is stunned or something like this.");
        }
    }

    /// <summary>
    /// <para> Актуализирует состояние лица в интерфейсе скромника в зависимости от состояния </para>
    /// Самостоятельно просчитывает данные и состояние в зависимости от наличия компонентов.
    /// </summary>
    private void ActualizeAlert(EntityUid uid)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        if (RageQuery.HasComp(uid))
        {
            _alerts.ShowAlert(uid, RageAlert);
            return;
        }

        if (HeatingUpQuery.HasComp(uid))
        {
            _alerts.ShowAlert(uid, HeatingUpAlert);
            return;
        }

        if (UseDownState(uid))
        {
            _alerts.ShowAlert(uid, SleepAlert);
            return;
        }

        // Если лицо не повреждено(severity == 0), то там нужно использовать стандартный алерт -> поэтому тут проверка.
        if (TryGetAlertDamageSeverity(uid, out var severity) && severity != 0)
        {
            _alerts.ShowAlert(uid, FaceDamageAlert, severity);
            return;
        }

        _alerts.ShowAlert(uid, IdleAlert);
    }

    /// <summary>
    /// Переключает ограничения связанные с взаимодействием с объектом.
    /// </summary>
    private bool TryToggleRestrictions(Entity<ScpRestrictionComponent?> ent, bool value)
    {
        if (!Resolve(ent, ref ent.Comp))
            return false;

        ent.Comp.CanTakeStaminaDamage = value;
        ent.Comp.CanBeDisarmed = value;
        ent.Comp.CanStandingState = value;
        Dirty(ent);

        return true;
    }

    /// <summary>
    /// Проверяет, находится ли скромник в камере содержания
    /// </summary>
    /// <param name="uid"><see cref="EntityUid"/> скромника</param>
    /// <returns>Находится/не находится</returns>
    private bool IsInContainmentChamber(EntityUid uid)
    {
        var coords = Transform(uid).Coordinates;
        var markers =
            _lookup.GetEntitiesInRange<Scp096ContainmentChamberMarkerComponent>(coords, ContainmentChamberSearchRadius, LookupFlags.Sensors);

        return markers
            .Any(e => _proximity.IsRightType(uid, e, LineOfSightBlockerLevel.None, out _));
    }

    #endregion
}

#region Events

public sealed partial class Scp096CryOutEvent : InstantActionEvent;
public sealed partial class Scp096FaceSkinRipEvent : InstantActionEvent;
public sealed partial class Scp096SitDownEvent : InstantActionEvent;


[Serializable, NetSerializable]
public sealed partial class Scp096FaceSkinRipStartDoAfterEvent : SimpleDoAfterEvent;

#endregion

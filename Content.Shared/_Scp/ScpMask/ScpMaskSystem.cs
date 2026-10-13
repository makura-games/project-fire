using System.Diagnostics.CodeAnalysis;
using Content.Shared._Scp.Mobs.Components;
using Content.Shared._Scp.SafeTime;
using Content.Shared._Sunrise.Random;
using Content.Shared.Actions;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._Scp.ScpMask;

/// <summary>
/// Система масок для сцп объектов, работающая для абсолютно всех гуманоидных объектов, которые могут носить одежду. <br/>
/// Позволяет контролировать будет ли надета маска, дает способность разорвать маску. <br/>
/// </summary>
/// <remarks>
/// TODO: Рефактор на выдачу компонента на время ношения маски. Компонент должен хранить ссылку на маску.
/// </remarks>

public sealed partial class ScpMaskSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private RandomPredictedSystem _random = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpComponent, ScpTearMaskEvent>(OnTear);
        SubscribeLocalEvent<ScpComponent, ScpTearMaskDoAfterEvent>(OnTearSuccess);

        SubscribeLocalEvent<ScpComponent, DamageChangedEvent>(OnDamage);

        InitializeEquipment();
        InitializeRestrictions();
    }

    private void OnTear(Entity<ScpComponent> scp, ref ScpTearMaskEvent args)
    {
        if (args.Handled)
            return;

        if (!TryGetScpMask(scp, out var scpMask))
            return;

        // Нельзя снять маску, пока действует сейвтайм
        if (IsInSafeTime(scpMask.Value, scp))
            return;

        var doAfterArgs = new DoAfterArgs(EntityManager, scp, scpMask.Value.Comp.TearTime, new ScpTearMaskDoAfterEvent(), scp, scp, scpMask)
        {
            BreakOnDamage = true,
        };

        args.Handled = _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnTearSuccess(Entity<ScpComponent> scp, ref ScpTearMaskDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        if (!TryTear(scp))
            return;

        args.Handled = true;
    }

    private void OnDamage(Entity<ScpComponent> scp, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased)
            return;

        // Нужен только тот урон, что нанесли игроки
        if (!args.Origin.HasValue || args.Origin == scp)
            return;

        if (!TryGetScpMask(scp, out var scpMask))
            return;

        // Проверяем, подходит ли инициатор урона под заданные критерии
        // Например, мы не хотим, чтобы урон от НЕ мобов считался -> записываем MobState в DamageOriginWhitelist
        // и только сущности имеющие MobState пройдут здесь
        if (!_whitelist.CheckBoth(args.Origin,
                scpMask.Value.Comp.DamageOriginBlacklist,
                scpMask.Value.Comp.DamageOriginWhitelist))
            return;

        if (!_random.ProbForEntity(scp, scpMask.Value.Comp.TearChanceOnDamage))
            return;

        TryTear(scp, scpMask.Value);
    }

    #region Public API

    /// <summary>
    /// Моментально маску на выбранном сцп
    /// </summary>
    /// <returns>Успешна ли попытка порвать маску</returns>
    public bool TryTear(EntityUid scp)
    {
        if (!TryGetScpMask(scp, out var scpMask))
            return false;

        if (!TryTear(scp, scpMask.Value))
            return false;

        return true;
    }

    public bool TryTear(EntityUid scp, Entity<ScpMaskComponent> mask)
    {
        if (mask.Comp.TearSound != null)
            _audio.PlayPvs(mask.Comp.TearSound,scp);

        var message = Loc.GetString(
            "scp-destroyed-mask",
            ("scp", Name(scp)),
            ("mask", Name(mask))
        );

        _popup.PopupPredicted(message, scp, null, PopupType.LargeCaution);
        PredictedQueueDel(mask.Owner);

        return true;
    }

    /// <summary>
    /// Метод, позволяющий проверить, надета ли на сцп маска. В случае привета передает ентити маски
    /// </summary>
    /// <returns>Надета ли маска</returns>
    public bool TryGetScpMask(EntityUid scp, [NotNullWhen(true)] out Entity<ScpMaskComponent>? mask)
    {
        mask = null;

        if (!_inventory.TryGetInventoryEntity<ScpMaskComponent>(scp, out var maskEntity))
            return false;

        if (!Resolve(maskEntity, ref maskEntity.Comp))
            return false;

        mask = (maskEntity.Owner, maskEntity.Comp);

        return true;
    }

    public bool HasScpMask(EntityUid scp)
    {
        return TryGetScpMask(scp, out _);
    }

    /// <summary>
    /// Создает попап, говорящий о невозможности использовать способность из-за маски
    /// </summary>
    public bool TryCreatePopup(EntityUid scp, EntityUid? mask)
    {
        if (!mask.HasValue)
            return false;

        var message = Loc.GetString("scp-mask-action-blocked", ("mask", Name(mask.Value)));
        if (_net.IsServer) // Для работы вызовов с сервера
            _popup.PopupEntity(message, scp, scp, PopupType.LargeCaution);

        return true;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Упрощенный метод для проверки, находится ли маска в периоде "безопасного времени".
    /// Автоматически получает маску на сущности и вызывает полноценный метод, возвращая его результат.
    /// </summary>
    /// <param name="user">Сущность, для которой будет показано уведомление о невозможности снятия маски, если <see cref="silent"/> = true</param>
    /// <param name="silent">Будет ли показано уведомление о невозможности снятия маски, если маска находится в безопасном времени</param>
    /// <returns>Находится ли маска в безопасном времени или нет</returns>
    private bool IsInSafeTime(EntityUid user, bool silent = false)
    {
        if (!TryGetScpMask(user, out var mask))
            return false;

        return IsInSafeTime(mask.Value, user, silent);
    }

    /// <summary>
    /// Проверяет, находится ли маска в "безопасном времени" в течение которого ее запрещено снимать.
    /// </summary>
    /// <param name="ent">Маска, которую проверяем</param>
    /// <param name="user">Сущность, для которой будет показано уведомление о невозможности снятия маски, если <see cref="silent"/> = true</param>
    /// <param name="silent">Будет ли показано уведомление о невозможности снятия маски, если маска находится в безопасном времени</param>
    /// <returns>Находится ли маска в безопасном времени или нет.</returns>
    private bool IsInSafeTime(Entity<ScpMaskComponent> ent, EntityUid user, bool silent = false)
    {
        if (!ent.Comp.SafeTimeEnd.HasValue)
            return false;

        if (_timing.CurTime >= ent.Comp.SafeTimeEnd)
            return false;

        if (!silent)
        {
            var timeLeft = SharedSafeTimeSystem.GetTimeLeft(_timing.CurTime, ent.Comp.SafeTimeEnd.Value);
            var message = Loc.GetString("scp-mask-cannot-tear-safe-time", ("time", timeLeft));
            _popup.PopupClient(message, user, user);
        }

        return true;
    }

    #endregion

}

#region Events

public sealed partial class ScpTearMaskEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed partial class ScpTearMaskDoAfterEvent : SimpleDoAfterEvent;


#endregion


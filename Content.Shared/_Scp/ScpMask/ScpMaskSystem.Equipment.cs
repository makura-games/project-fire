using System.Diagnostics.CodeAnalysis;
using Content.Shared._Scp.Mobs.Components;
using Content.Shared.Bed.Sleep;
using Content.Shared.DoAfter;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Serialization;

namespace Content.Shared._Scp.ScpMask;

public sealed partial class ScpMaskSystem
{
    [Dependency] private MobStateSystem _mob = default!;

    private void InitializeEquipment()
    {
        SubscribeLocalEvent<ScpMaskComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<ScpMaskComponent, MeleeHitEvent>(OnMeleeHit);

        SubscribeLocalEvent<ScpComponent, ScpMaskEquipmentDoAfterEvent>(OnMeleeEquip);
        SubscribeLocalEvent<ScpMaskComponent, BeingEquippedAttemptEvent>(OnEquip);
        SubscribeLocalEvent<ScpMaskComponent, BeingUnequippedAttemptEvent>(OnUnequip);
    }

    private void OnAfterInteract(Entity<ScpMaskComponent> ent, ref AfterInteractEvent args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        if (args.Handled)
            return;

        if (!args.CanReach)
            return;

        if (!args.Target.HasValue)
            return;

        if (!TryMeleeEquip(ent, args.Target.Value, args.User))
            return;

        args.Handled = true;
    }

    private void OnMeleeHit(Entity<ScpMaskComponent> ent, ref MeleeHitEvent args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        if (args.Handled)
            return;

        foreach (var uid in args.HitEntities)
        {
            if (!TryMeleeEquip(ent, uid, args.User))
                continue;

            args.Handled = true;
            return;
        }
    }

    private bool TryMeleeEquip(Entity<ScpMaskComponent> ent, EntityUid target, EntityUid user)
    {
        if (!CanEquip(ent, target, user, out _, false))
            return false;

        var time = ent.Comp.AttackEquipTime;

        // Быстро надеваем, если цель застанена, спит, в критическом состоянии или умерла
        if (HasComp<StunnedComponent>(target) || HasComp<SleepingComponent>(target) || _mob.IsIncapacitated(target))
            time *= 0.1f;

        var doAfterArgs = new DoAfterArgs(EntityManager, user, time, new ScpMaskEquipmentDoAfterEvent(), target, target, ent)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfterArgs))
            return false;

        return true;
    }

    private bool CanEquip(Entity<ScpMaskComponent> ent, EntityUid target, EntityUid user, [NotNullWhen(true)] out string? foundSlot, bool checkSlots = true)
    {
        foundSlot = null;

        // Маска должна надеваться только на те сущности, что находятся в вайтлисте
        if (_whitelist.IsWhitelistFailOrNull(ent.Comp.TargetWhitelist, target))
        {
            var message = Loc.GetString("scp-mask-cannot-equip", ("name", Identity.Name(target, EntityManager)));
            _popup.PopupEntity(message, target, user);

            return false;
        }

        // Уже есть маска?
        if (HasScpMask(target))
        {
            var message = Loc.GetString("scp-mask-already-equipped", ("name", Identity.Name(target, EntityManager)));
            _popup.PopupEntity(message, target, user);

            return false;
        }

        // Возможно отменить надевание маски в определенных ситуациях
        // Пример: Маска 096 не должна надеваться, пока тот в агре

        var maskEvent = new ScpMaskEquipAttempt();
        var targetEvent = new ScpMaskTargetEquipAttempt();

        RaiseLocalEvent(ent, maskEvent);
        RaiseLocalEvent(target, targetEvent);

        if (maskEvent.Cancelled || targetEvent.Cancelled)
            return false;

        if (!checkSlots)
        {
            foundSlot = string.Empty;
            return true;
        }

        if (!_inventory.TryGetSlots(target, out var slots))
            return false;

        foreach (var slot in slots)
        {
            if (!_inventory.CanEquip(user, target, ent, slot.Name, out _, slot))
                continue;

            foundSlot = slot.Name;
            break;
        }

        if (foundSlot == null)
            return false;

        return true;
    }

    private void OnMeleeEquip(Entity<ScpComponent> ent, ref ScpMaskEquipmentDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        if (!args.Used.HasValue)
            return;

        if (!TryComp<ScpMaskComponent>(args.Used, out var scpMask))
            return;

        if (!CanEquip((args.Used.Value, scpMask), ent, args.User, out var slot))
            return;

        if (!_inventory.TryEquip(args.User, ent, args.Used.Value, slot, force: true))
            return;

        args.Handled = true;
    }

    private void OnEquip(Entity<ScpMaskComponent> ent, ref BeingEquippedAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (!CanEquip(ent, args.EquipTarget, args.Equipment, out _, false))
        {
            args.Cancel();
            return;
        }

        DoEquipmentStuff(ent, args.EquipTarget, args.Equipment);
    }

    private void OnUnequip(Entity<ScpMaskComponent> ent, ref BeingUnequippedAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (args.UnEquipTarget == args.Equipment && IsInSafeTime(ent, args.Equipment))
            args.Cancel();
    }

    private void DoEquipmentStuff(Entity<ScpMaskComponent> ent, EntityUid target, EntityUid equiper)
    {
        // Проигрывание звука надевания.
        _audio.PlayPredicted(ent.Comp.EquipSound, target, equiper);

        // Задаем сейвтайм, в течение которого игрок не может снять маску
        ent.Comp.SafeTimeEnd = _timing.CurTime + ent.Comp.SafeTime;
        Dirty(ent);
    }
}

#region Events

[Serializable, NetSerializable]
public sealed partial class ScpMaskEquipmentDoAfterEvent : SimpleDoAfterEvent;

public sealed partial class ScpMaskEquipAttempt : CancellableEntityEventArgs;
public sealed partial class ScpMaskTargetEquipAttempt : CancellableEntityEventArgs;

#endregion

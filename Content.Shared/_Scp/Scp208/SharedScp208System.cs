using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Body.Systems;
using Content.Shared.Body.Components;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Serialization;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;

namespace Content.Shared._Scp.Scp208;

public sealed class SharedScp208System : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedBloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Scp208Component, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<Scp208Component, ComponentShutdown>(OnShutdown);

        SubscribeLocalEvent<Scp208Component, Scp208HealTargetActionEvent>(OnHealAction);
        SubscribeLocalEvent<Scp208Component, Scp208HealDoAfterEvent>(OnDoAfter);
    }

    private void OnStartup(Entity<Scp208Component> ent, ref ComponentStartup args)
	{
		_actions.AddAction(ent, ref ent.Comp.Action, ent.Comp.ActionHealId);
		Dirty(ent);
	}

    private void OnShutdown(Entity<Scp208Component> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.Action);
    }

    private void OnHealAction(Entity<Scp208Component> ent, ref Scp208HealTargetActionEvent args)
    {
        if (args.Handled)
            return;

        if (!CanHeal(ent, args.Target, out var errorMessage))
        {
            if (errorMessage != null)
                _popup.PopupClient(errorMessage, ent, ent);

            return;
        }

        args.Handled = TryStartHealing(ent, args.Target);
    }

    private bool TryStartHealing(Entity<Scp208Component> ent, EntityUid target)
    {
        _audio.PlayPredicted(ent.Comp.HealingBeginSound, ent, ent);

        var doAfterArgs = new DoAfterArgs(EntityManager, ent, ent.Comp.Delay, new Scp208HealDoAfterEvent(), ent, target: target)
        {
            BreakOnMove = true,
            NeedHand = false,
        };

        return _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnDoAfter(Entity<Scp208Component> ent, ref Scp208HealDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        if (!TryComp<DamageableComponent>(target, out var damageable))
            return;

        _damageable.TryChangeDamage(target, ent.Comp.Damage, true, origin: ent);

        if (ent.Comp.StopBleeding && TryComp<BloodstreamComponent>(target, out var bloodstream))
        {
            var wasBleeding = bloodstream.BleedAmount > 0;
            _bloodstream.TryModifyBleedAmount((target, bloodstream), ent.Comp.BloodlossModifier);

            if (wasBleeding && bloodstream.BleedAmount <= 0)
            {
                var popup = ent.Owner == target
                    ? Loc.GetString("medical-item-stop-bleeding-self")
                    : Loc.GetString("medical-item-stop-bleeding", ("target", Identity.Entity(target, EntityManager)));
                _popup.PopupClient(popup, target, ent);
            }
        }

        _audio.PlayPredicted(ent.Comp.HealingEndSound, ent, ent);

        if (_mobState.IsAlive(target) && HasDamageToHeal(target, damageable, ent.Comp))
            TryStartHealing(ent, target);

        args.Handled = true;
    }

    private bool CanHeal(Entity<Scp208Component> ent, EntityUid target, out string? errorMessage)
    {
        errorMessage = null;

        if (_standing.IsDown(ent.Owner))
            return false;

        if (!TryComp<DamageableComponent>(target, out var damageable))
            return false;

        if (!_mobState.IsAlive(target))
            return false;

        if (!HasDamageToHeal(target, damageable, ent.Comp))
            return false;

        return true;
    }

    private bool HasDamageToHeal(EntityUid target, DamageableComponent damageable, Scp208Component scp208)
    {
        var damage = _damageable.GetAllDamage((target, damageable));
        foreach (var (type, _) in scp208.Damage.DamageDict)
        {
            if (damage.DamageDict.TryGetValue(type, out var currentDamage) &&
                currentDamage > FixedPoint2.Zero)
            {
                return true;
            }
        }

        if (scp208.StopBleeding && TryComp<BloodstreamComponent>(target, out var bloodstream))
        {
            if (bloodstream.BleedAmount > 0)
                return true;
        }

        return false;
    }
}

[Serializable, NetSerializable]
public sealed partial class Scp208HealDoAfterEvent : SimpleDoAfterEvent
{
}

public sealed partial class Scp208HealTargetActionEvent : EntityTargetActionEvent
{
}

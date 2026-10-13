using System.Linq;
using System.Threading.Tasks;
using Content.Shared._Scp.Fear.Components;
using Content.Shared._Scp.Scp106.Components;
using Content.Shared._Scp.Scp106.Protection;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._Scp.Scp106.Systems;

public abstract partial class SharedScp106System : EntitySystem
{
	[Dependency] private SharedDoAfterSystem _doAfter = default!;
	[Dependency] private SharedPopupSystem _popup = default!;
	[Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private MobStateSystem _mob = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly SoundSpecifier TeleportSound = new SoundPathSpecifier("/Audio/_Scp/Scp106/return.ogg");

    private const float DamageInPocketDimensionMultiplier = 3f;
    protected static readonly TimeSpan TeleportTimeCompensation = TimeSpan.FromSeconds(0.1f);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Scp106Component, MeleeHitEvent>(OnMeleeHit);

        SubscribeLocalEvent<Scp106PhantomComponent, Scp106BecomeTeleportPhantomActionEvent>(OnBecomeTeleportPhantomActionEvent);

        InitializeAbilities();
        InitializePhantom();
        InitializeStore();
    }

    private void OnBecomeTeleportPhantomActionEvent(Entity<Scp106PhantomComponent> ent, ref Scp106BecomeTeleportPhantomActionEvent args)
    {
        if (args.Cancelled)
        {
            if (args.Args.EventTarget == null)
                return;

            if (_mind.TryGetMind(args.Args.EventTarget.Value, out var mindId, out _))
            {
                _mind.TransferTo(mindId, args.Args.User);
                _appearance.SetData(args.Args.User, Scp106Visuals.Visuals, Scp106VisualsState.Default);
                _mob.ChangeMobState(args.Args.EventTarget.Value, MobState.Dead);

                return;
            }
        }

        if (PhantomTeleport(args))
            args.Handled = true;
    }

    private bool TryDoTeleport<T>(Entity<Scp106Component> ent, ref T args, SimpleDoAfterEvent doAfterEvent)
        where T : Scp106ValuableActionEvent
    {
        if (args.Handled)
            return false;

        if (IsContained(ent))
            return false;

        if (!TryDeductEssence(ent, args.Cost))
            return false;

        var doAfterEventArgs = new DoAfterArgs(EntityManager, args.Performer, ent.Comp.TeleportationDuration, doAfterEvent, args.Performer, args.Performer)
        {
            NeedHand = false,
            BreakOnMove = true,
            BreakOnHandChange = false,
            BreakOnDamage = false,
            RequireCanInteract = false,
        };
        _doAfter.TryStartDoAfter(doAfterEventArgs);

        _stun.TryAddStunDuration(ent, ent.Comp.TeleportationDuration + TeleportTimeCompensation);
        _appearance.SetData(ent, Scp106Visuals.Visuals, Scp106VisualsState.Entering);

        args.Handled = true;
        return true;
    }

    private void OnMeleeHit(Entity<Scp106Component> ent, ref MeleeHitEvent args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        if (!args.IsHit || !args.HitEntities.Any())
            return;

        if (IsInDimension(ent))
            args.BonusDamage = args.BaseDamage * DamageInPocketDimensionMultiplier;

        foreach (var target in args.HitEntities)
        {
            if (target == ent.Owner)
                return;

            if (HasComp<Scp106ProtectionComponent>(target))
                continue;

            AbsorbFear(ent, target);
            _ = SendToBackrooms(target, ent);
        }
    }

    #region Helpers

    private void DoTeleportEffects(EntityUid uid)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        _audio.PlayPredicted(TeleportSound, uid, uid);
    }

    public bool TryDeductEssence(Entity<Scp106Component> ent, FixedPoint2 cost)
    {
        if (IsInDimension(ent))
            return true;

        if (ent.Comp.Essence < cost)
        {
            var message = Loc.GetString("not-enough-essence", ("count", cost - ent.Comp.Essence));
            _popup.PopupClient(message, ent, ent, PopupType.Medium);

            return false;
        }

        ent.Comp.Essence -= cost;
        Dirty(ent);

        return true;
    }

    public bool IsContained(Entity<Scp106Component> ent)
    {
        if (!ent.Comp.IsContained)
            return false;

        if (_timing.IsFirstTimePredicted)
            _popup.PopupClient(Loc.GetString("scp106-abilities-suppressed"), ent, ent,  PopupType.SmallCaution);

        return true;
    }

    public bool IsInDimension(EntityUid ent)
    {
        var mapUid = Transform(ent).MapUid;
        return HasComp<Scp106BackRoomMapComponent>(mapUid);
    }

    /// <summary>
    /// Высасывает уровень страха из жертвы и сохраняет в список.
    /// </summary>
    private void AbsorbFear(Entity<Scp106Component> ent, EntityUid target)
    {
        if (!TryComp<FearComponent>(target, out var fear))
            return;

        ent.Comp.AbsorbedFears.Add(fear.State);
        Dirty(ent);
    }

    #endregion

    #region Virtuals

    public virtual async Task SendToBackrooms(EntityUid target, Entity<Scp106Component>? scp106 = null)
    {
        await Task.CompletedTask;
    }

    public virtual void SendToStation(EntityUid target) {}

    // TODO: Реализовать
    public virtual void SendToHuman(EntityUid target) {}

    public virtual bool PhantomTeleport(Scp106BecomeTeleportPhantomActionEvent args) { return false; }

    public virtual void BecomeTeleportPhantom(EntityUid uid, ref Scp106BecomeTeleportPhantomAction args) {}

    public virtual void BecomePhantom(Entity<Scp106Component> ent, ref Scp106BecomePhantomAction args) {}

    #endregion
}

[NetSerializable, Serializable]
public enum Scp106VisualLayers : byte
{
    Digit1,
    Digit2,
    Digit3,
}

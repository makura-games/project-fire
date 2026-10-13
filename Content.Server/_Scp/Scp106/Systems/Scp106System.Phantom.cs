using Content.Server.Actions;
using Content.Server.Popups;
using Content.Shared._Scp.Scp106;
using Content.Shared._Scp.Scp106.Components;
using Content.Shared.DoAfter;
using Content.Shared.Humanoid;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Server.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Scp.Scp106.Systems;

public sealed partial class Scp106System
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private ActionsSystem _actions = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IPlayerManager _player = default!;

    private static readonly EntProtoId PhantomRemains = "Ash";

    private void InitializePhantom()
    {
        SubscribeLocalEvent<Scp106PhantomComponent, MobStateChangedEvent>(OnMobStateChangedEvent);
        SubscribeLocalEvent<Scp106PhantomComponent, EntityTerminatingEvent>(OnPhantomShutdown);
        SubscribeLocalEvent<Scp106PhantomComponent, Scp106ReverseActionEvent>(OnScp106ReverseActionEvent);
    }

    private void OnMobStateChangedEvent(Entity<Scp106PhantomComponent> ent, ref MobStateChangedEvent args)
    {
        if (!_mobState.IsIncapacitated(ent))
            return;

        if (args.Origin.HasValue)
        {
            // Проигрываем звук смерти фантома убийце фантома
            _audio.PlayGlobal(ent.Comp.DeathSound, args.Origin.Value);

            // Прикольное сообщение
            var message = Loc.GetString("scp106-phantom-killed");
            _popup.PopupCoordinates(message, Transform(ent).Coordinates, args.Origin.Value);
        }

        // И игроку на 106
        if (_player.TryGetSessionByEntity(ent, out var session))
            _audio.PlayGlobal(ent.Comp.DeathSound, session);

        TryAshAndReturnToBody(ent);
    }

    private void OnPhantomShutdown(Entity<Scp106PhantomComponent> ent, ref EntityTerminatingEvent args)
    {
        TryReturnToBody(ent);
    }

    private void OnScp106ReverseActionEvent(Entity<Scp106PhantomComponent> ent, ref Scp106ReverseActionEvent args)
    {
        if (args.Cancelled)
            return;

        if (args.Args.Target == null)
            return;

        var target = args.Args.Target.Value;

        if (!HasComp<HumanoidProfileComponent>(target))
            return;

        if (!_mobState.IsDead(target))
            return;

        if (!Exists(ent.Comp.Scp106BodyUid))
            return;

        var targetPos = Transform(target).Coordinates;

        _transform.SetCoordinates(ent.Comp.Scp106BodyUid.Value, targetPos);
        _ = SendToBackrooms(target);

        if (args.Args.EventTarget == null)
            return;

        _mobState.ChangeMobState(args.Args.EventTarget.Value, MobState.Dead);
    }

    public override void BecomeTeleportPhantom(EntityUid uid, ref Scp106BecomeTeleportPhantomAction args)
    {
        if (!_mind.TryGetMind(uid, out var mindId, out _))
            return;

        var phantom = Spawn(args.PhantomPrototype, Transform(uid).Coordinates);

        _mind.TransferTo(mindId, phantom);

        if (TryComp<Scp106PhantomComponent>(phantom, out var scp106PhantomComponent))
            scp106PhantomComponent.Scp106BodyUid = uid;

        var doAfterEventArgs = new DoAfterArgs(EntityManager, uid, args.Delay, new Scp106BecomeTeleportPhantomActionEvent(), phantom)
        {
            BreakOnMove = false,
            BreakOnDamage = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfterEventArgs))
            return;

        _appearance.SetData(uid, Scp106Visuals.Visuals, Scp106VisualsState.Entering);
    }

    public override void BecomePhantom(Entity<Scp106Component> ent, ref Scp106BecomePhantomAction args)
    {
        var scp106Phantom = Spawn(args.PhantomPrototype, Transform(ent).Coordinates);

        if (_mind.TryGetMind(ent, out var mindId, out _))
            _mind.TransferTo(mindId, scp106Phantom);

        if (!TryComp<Scp106PhantomComponent>(scp106Phantom, out var scp106PhantomComponent))
            return;

        scp106PhantomComponent.Scp106BodyUid = ent;
        Dirty(ent);

        _actions.SetCooldown(args.Action.AsNullable(), ent.Comp.PhantomCoolDown);
        args.Handled = true;
    }

    #region Helpers

    private bool TryAshAndReturnToBody(Entity<Scp106PhantomComponent> ent)
    {
        var returned = TryReturnToBody(ent);
        PhantomAsh(ent);
        return returned;
    }

    private void PhantomAsh(EntityUid uid)
    {
        Spawn(PhantomRemains, Transform(uid).Coordinates);
        QueueDel(uid);
    }

    private bool TryReturnToBody(Entity<Scp106PhantomComponent> ent)
    {
        if (!TryComp<MindContainerComponent>(ent, out var mind))
            return false;

        var anyMind = mind.Mind ?? mind.LastMindStored;

        if (!Exists(ent.Comp.Scp106BodyUid) || !Exists(anyMind))
            return false;

        _mind.TransferTo(anyMind.Value, ent.Comp.Scp106BodyUid);

        return true;
    }

    #endregion
}

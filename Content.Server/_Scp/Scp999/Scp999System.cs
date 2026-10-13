using Content.Server._Sunrise.VentCraw;
using Content.Server.Actions;
using Content.Server.Disposal.Unit;
using Content.Server.Popups;
using Content.Shared._Scp.Scp999;
using Content.Shared._Sunrise.VentCraw;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction.Components;
using Content.Shared.Mobs;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Tag;
using Robust.Server.Audio;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;

namespace Content.Server._Scp.Scp999;

public sealed partial class Scp999System : SharedScp999System
{
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private PhysicsSystem _physics = default!;
    [Dependency] private FixtureSystem _fixture = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private ContainerSystem _container = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private ActionBlockerSystem _action = default!;
    [Dependency] private ActionsSystem _actions = default!;
    [Dependency] private IRobustRandom _random = default!;

    private const string WallFixtureId = "fix2";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Scp999Component, Scp999WallifyActionEvent>(OnWallifyActionEvent);
        SubscribeLocalEvent<Scp999Component, Scp999RestActionEvent>(OnRestActionEvent);
        SubscribeLocalEvent<Scp999Component, MobStateChangedEvent>(OnMobStateChanged);

        SubscribeLocalEvent<Scp999Component, Scp999ChangeStateAttemptEvent>(OnChangeStateAttempt);
        SubscribeLocalEvent<Scp999Component, Scp999ChangedStateEvent>(OnChangedState);

        SubscribeLocalEvent<Scp999Component, DamageChangedEvent>(OnDamageChanged);

        SubscribeLocalEvent<Scp999Component, VentCrawlAttemptEvent>(OnEnterVent);

        SubscribeLocalEvent<Scp999Component, EntityFedEvent>(OnFeed);
    }

    #region Abilities

    private void OnMobStateChanged(Entity<Scp999Component> entity, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        entity.Comp.CurrentState = Scp999States.Default;
        Dirty(entity);
    }

    private void OnWallifyActionEvent(Entity<Scp999Component> ent, ref Scp999WallifyActionEvent args)
    {
        if (args.Handled)
            return;

        switch (ent.Comp.CurrentState)
        {
            // add buffs
            case Scp999States.Default:
                FromDefaultToWall(ent);
                break;

            // remove buffs
            case Scp999States.Wall:
                FromWallToDefault(ent);
                break;

            // Все остальное
            default:
                return;
        }

        args.Handled = true;
    }

    private void OnRestActionEvent(Entity<Scp999Component> ent, ref Scp999RestActionEvent args)
    {
        if (args.Handled)
            return;

        Scp999RestEvent ev;
        var netEntity = GetNetEntity(ent);

        switch (ent.Comp.CurrentState)
        {
            // add buffs
            // TODO: РЕАЛЬНЫЙ сон, а не вот это параша
            case Scp999States.Default:

                var toRestAttemptEvent = new Scp999ChangeStateAttemptEvent(Scp999States.Rest);
                RaiseLocalEvent(ent, toRestAttemptEvent);

                if (toRestAttemptEvent.Cancelled)
                    return;

                ev = new Scp999RestEvent(netEntity, ent.Comp.States[Scp999States.Rest]);

                ent.Comp.CurrentState = Scp999States.Rest;
                Dirty(ent);

                EnsureComp<BlockMovementComponent>(ent);
                EnsureComp<NoRotateOnInteractComponent>(ent);
                EnsureComp<NoRotateOnMoveComponent>(ent);

                _audio.PlayPvs(ent.Comp.SleepSound, ent);

                var toRestChangedEvent = new Scp999ChangedStateEvent(Scp999States.Rest);
                RaiseLocalEvent(ent, toRestChangedEvent);

                break;

            // remove buffs
            case Scp999States.Rest:

                var toDefaultAttemptEvent = new Scp999ChangeStateAttemptEvent(Scp999States.Default);
                RaiseLocalEvent(ent, toDefaultAttemptEvent);

                if (toDefaultAttemptEvent.Cancelled)
                    return;

                ev = new Scp999RestEvent(netEntity, ent.Comp.States[Scp999States.Default]);

                ent.Comp.CurrentState = Scp999States.Default;
                Dirty(ent);

                RemComp<BlockMovementComponent>(ent);
                RemComp<NoRotateOnMoveComponent>(ent);
                RemComp<NoRotateOnInteractComponent>(ent);

                _action.UpdateCanMove(ent);

                var toDefaultChangedEvent = new Scp999ChangedStateEvent(Scp999States.Default);
                RaiseLocalEvent(ent, toDefaultChangedEvent);

                break;

            // Все остальное
            default:
                return;
        }

        RaiseNetworkEvent(ev);

        args.Handled = true;
    }

    #endregion

    private void OnChangeStateAttempt(Entity<Scp999Component> ent, ref Scp999ChangeStateAttemptEvent args)
    {
        if (_container.IsEntityInContainer(ent) && args.TargetState == Scp999States.Wall)
            args.Cancel();

        if (HasComp<BeingDisposedComponent>(ent))
            args.Cancel();

        if (TryComp<VentCrawlerComponent>(ent, out var ventCrawler) && ventCrawler.InTube)
            args.Cancel();

        if (args.Cancelled)
            _popup.PopupEntity(Loc.GetString("scp-999-change-state-cancelled"), ent, ent);
    }

    private void OnChangedState(Entity<Scp999Component> ent, ref Scp999ChangedStateEvent args)
    {
        // Чтобы в момент превращения прекращать тащить и быть таскаемым.

        if (TryComp<PullableComponent>(ent, out var pullable))
            _pulling.TryStopPull(ent, pullable);

        if (TryComp<PullerComponent>(ent, out var puller) && puller.Pulling.HasValue && TryComp<PullableComponent>(puller.Pulling, out var pullable2))
            _pulling.TryStopPull(puller.Pulling.Value, pullable2, ent);
    }

    private void OnDamageChanged(Entity<Scp999Component> ent, ref DamageChangedEvent args)
    {
        if (ent.Comp.CurrentState != Scp999States.Wall)
            return;

        if (!args.DamageIncreased || args.DamageDelta == null)
            return;

        if (args.Origin == null)
            return;

        ent.Comp.CurrentTotalDamage += args.DamageDelta.GetTotal();
        if (ent.Comp.CurrentTotalDamage < ent.Comp.TotalDamageToChangeState)
            return;

        FromWallToDefault(ent);

        foreach (var action in _actions.GetActions(ent))
        {
            if (!action.Comp.UseDelay.HasValue)
                continue;

            _actions.SetIfBiggerCooldown(action.AsNullable(), action.Comp.UseDelay.Value * 3f);
        }
    }

    private void OnEnterVent(Entity<Scp999Component> ent, ref VentCrawlAttemptEvent args)
    {
        if (ent.Comp.CurrentState == Scp999States.Default)
            return;

        args.Cancel();
    }

    private void OnFeed(Entity<Scp999Component> scp, ref EntityFedEvent args)
    {
        if (!_tag.HasTag(args.Food, scp.Comp.CandyTag))
            return;

        if (!_random.Prob(scp.Comp.CreateJellyChance))
            return;

        Spawn(scp.Comp.Scp999Jelly, Transform(scp).Coordinates);

        _audio.PlayPvs(scp.Comp.CreateJellySound, scp);
    }
}

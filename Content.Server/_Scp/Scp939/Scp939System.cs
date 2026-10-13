using Content.Server.Actions;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._Scp.Scp939;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.StatusEffectNew;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;

namespace Content.Server._Scp.Scp939;

public sealed partial class Scp939System : EntitySystem
{
    [Dependency] private SmokeSystem _smokeSystem = default!;
    [Dependency] private SleepingSystem _sleepingSystem = default!;
    [Dependency] private ActionsSystem _actionsSystem = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!;
    [Dependency] private DamageableSystem _damageableSystem = default!;
    [Dependency] private AppearanceSystem _appearanceSystem = default!;
    [Dependency] private AudioSystem _audio = default!;

    private readonly SoundSpecifier _critSound = new SoundPathSpecifier("/Audio/_Scp/Scp939/crit.ogg");

    public override void Initialize()
    {
        base.Initialize();
        InitializeActions();

        SubscribeLocalEvent<Scp939Component, ComponentInit>(OnInit);

        SubscribeLocalEvent<Scp939Component, SleepStateChangedEvent>(OnSleepChanged);
        SubscribeLocalEvent<Scp939Component, MobStateChangedEvent>(OnMobStateChanged);


        InitializeVisibility();
    }

    private void OnMobStateChanged(Entity<Scp939Component> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Critical)
            return;

        TrySleep(ent, 360f);
        _audio.PlayPvs(_critSound, ent);
    }

    private void OnSleepChanged(Entity<Scp939Component> ent, ref SleepStateChangedEvent args)
    {
        if (TryComp<BloodstreamComponent>(ent, out var bloodstreamComponent))
        {
            if (args.FellAsleep)
                bloodstreamComponent.BloodRefreshAmount = 20;
            else
                bloodstreamComponent.BloodRefreshAmount = 1;

            Dirty(ent, bloodstreamComponent);
        }

        _appearanceSystem.SetData(ent, Scp939Visuals.Sleeping, args.FellAsleep);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateVisibilityTargets();

        var querySleeping = EntityQueryEnumerator<Scp939Component, SleepingComponent>();
        while (querySleeping.MoveNext(out var uid, out var scp939Component, out _))
        {
            _damageableSystem.TryChangeDamage(uid, scp939Component.HibernationHealingRate * frameTime);
        }

        var querySimple = EntityQueryEnumerator<Scp939Component>();
        while (querySimple.MoveNext(out var uid, out var scp939Component))
        {
            if (!scp939Component.PoorEyesight)
                continue;

            if (scp939Component.PoorEyesightTimeStart == null)
                continue;

            var timeDifference = _timing.CurTime - scp939Component.PoorEyesightTimeStart.Value;

            if (timeDifference > TimeSpan.FromSeconds(scp939Component.PoorEyesightTime))
            {
                scp939Component.PoorEyesight = false;
                scp939Component.PoorEyesightTimeStart = null;

                DirtyFields(uid,
                    scp939Component,
                    null,
                    nameof(Scp939Component.PoorEyesight),
                    nameof(Scp939Component.PoorEyesightTimeStart));
            }
        }
    }

    private void OnInit(Entity<Scp939Component> ent, ref ComponentInit args)
    {
        foreach (var action in ent.Comp.Actions)
        {
            _actionsSystem.AddAction(ent, action);
        }
    }
}

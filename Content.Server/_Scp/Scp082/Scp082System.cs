using Content.Server._Scp.Shaders.Highlighting;
using Content.Server.Popups;
using Content.Shared._Scp.Other.Events;
using Content.Shared._Scp.Scp082;
using Content.Shared._Scp.Shaders.Highlighting;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Scp.Scp082;

public sealed partial class Scp082System : SharedScp082System
{
    [Dependency] private HighlightSystem _highlight = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;

    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _highlightedTargets = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Scp082Component, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<Scp082Component, ScpIngestionConsumedEvent>(OnIngestionConsumed);
        SubscribeLocalEvent<Scp082Component, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<Scp082Component, ComponentShutdown>(OnShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<Scp082Component>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (_mobState.IsCritical(uid))
            {
                _damageable.TryChangeDamage(uid, component.CriticalStateHealingRate * frameTime);
                continue;
            }

            var curTime = _timing.CurTime;
            var hungerChanged = false;

            while (curTime >= component.NextHungerUpdate)
            {
                component.NextHungerUpdate += component.HungerUpdateInterval;
                SetHunger(uid, component, component.Hunger + component.HungerPerUpdate);
                hungerChanged = true;
            }

            if (hungerChanged)
                Dirty(uid, component);

            UpdatePopup(uid, component, curTime);
            UpdateHighlights(uid, component, curTime);
        }
    }

    private void OnShutdown(Entity<Scp082Component> entity, ref ComponentShutdown args)
    {
        ClearHighlights(entity.Owner);
    }

    private void OnMapInit(Entity<Scp082Component> entity, ref MapInitEvent args)
    {
        SetHunger(entity.Owner, entity.Comp, entity.Comp.Hunger);
        entity.Comp.NextHungerUpdate = _timing.CurTime + entity.Comp.HungerUpdateInterval;
        entity.Comp.NextAngerPopup = _timing.CurTime;
        Dirty(entity);

        if (!_body.TryGetOrganWithComponent<StomachComponent>(entity.Owner, out var stomach))
            return;
    }

    private void OnIngestionConsumed(Entity<Scp082Component> entity, ref ScpIngestionConsumedEvent args)
    {
        var hungerRestore = entity.Comp.MeatHungerRestore;
        if (args.Entity is { } food && _mobState.IsDead(food))
            hungerRestore = entity.Comp.CorpseHungerRestore;

        SetHunger(entity.Owner, entity.Comp, entity.Comp.Hunger - hungerRestore);
        Dirty(entity);
    }

    private void OnMobStateChanged(Entity<Scp082Component> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            ClearHighlights(ent);
    }

    private void SetHunger(EntityUid uid, Scp082Component component, float hunger)
    {
        component.Hunger = Math.Clamp(hunger, 0f, component.MaxHunger);
        component.Anger = Math.Clamp(component.Hunger * component.AngerPerHunger, 0f, component.MaxAnger);

        UpdateDigestibility((uid, component));

        var angerFraction = component.MaxAnger <= 0f ? 0f : component.Anger / component.MaxAnger;
        component.DamageModifier = 1f + angerFraction * (component.MaxDamageModifier - 1f);
        Dirty(uid, component);
    }

    private void UpdatePopup(EntityUid uid, Scp082Component component, TimeSpan curTime)
    {
        if (component.Anger < component.MinimumPopupAnger || component.AngerPopupMessages.Count == 0 || curTime < component.NextAngerPopup)
            return;

        _popup.PopupEntity(Loc.GetString(_random.Pick(component.AngerPopupMessages)), uid, uid, PopupType.MediumCaution);

        var angerFraction = Math.Clamp(component.Anger / component.MaxAnger, 0f, 1f);
        var interval = component.MaximumPopupInterval -
                       (component.MaximumPopupInterval - component.MinimumPopupInterval) * angerFraction;
        component.NextAngerPopup = curTime + interval;
    }

    private void UpdateHighlights(EntityUid uid, Scp082Component component, TimeSpan curTime)
    {
        if (component.Hunger < component.HighlightHungerThreshold)
        {
            ClearHighlights(uid);
            return;
        }

        if (curTime < component.NextHighlightUpdate)
            return;

        var hungerFraction = Math.Clamp(
            (component.Hunger - component.HighlightHungerThreshold) /
            Math.Max(1f, component.MaxHunger - component.HighlightHungerThreshold),
            0f,
            1f);

        component.NextHighlightUpdate = curTime + TimeSpan.FromSeconds(2.5f - hungerFraction * 1.8f);

        var nearbyEntities = new HashSet<EntityUid>();
        _lookup.GetEntitiesInRange(uid, component.HighlightRange, nearbyEntities, LookupFlags.Dynamic | LookupFlags.Sundries);

        var nextTargets = new HashSet<EntityUid>();
        _highlightedTargets.TryGetValue(uid, out var currentTargets);

        foreach (var target in nearbyEntities)
        {
            if (target == uid || !HasComp<HumanoidProfileComponent>(target))
                continue;

            nextTargets.Add(target);

            if (currentTargets == null || !currentTargets.Contains(target))
                _highlight.NetHighlight(target, uid, highlightTimes: -1);
        }

        if (currentTargets != null)
        {
            foreach (var target in currentTargets)
            {
                if (!nextTargets.Contains(target))
                    StopNetworkHighlight(target, uid);
            }
        }

        _highlightedTargets[uid] = nextTargets;
    }

    private void ClearHighlights(EntityUid uid)
    {
        if (!_highlightedTargets.Remove(uid, out var targets))
            return;

        foreach (var target in targets)
        {
            StopNetworkHighlight(target, uid);
        }
    }

    private void StopNetworkHighlight(EntityUid target, EntityUid recipient)
    {
        if (!Exists(target) || !TryComp<HighlightedComponent>(target, out var component) || component.Recipient != recipient)
            return;

        RaiseNetworkEvent(new HighLightEndEvent(GetNetEntity(target)), recipient);
        RemCompDeferred<HighlightedComponent>(target);
    }

}

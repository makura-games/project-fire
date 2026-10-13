using System.Numerics;
using Content.Server._Sunrise.ScaleSprite;
using Content.Server._Sunrise.VentCraw;
using Content.Server.Stack;
using Content.Shared._Scp.Helpers;
using Content.Shared._Scp.Mobs.Components;
using Content.Shared._Scp.Other.Events;
using Content.Shared._Scp.SafeTime;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.EntityEffects.Effects.Water;
using Content.Shared.Item;
using Content.Shared.Materials;
using Content.Shared.Physics;
using Content.Shared.Stacks;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Scp.Scp457;

// TODO: Анхардкод
public sealed partial class Scp457System : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedSafeTimeSystem _safeTime = default!;
    [Dependency] private ScaleSpriteSystem _scaleSprite = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private EntityQuery<FlammableComponent> _flammableQuery;
    private EntityQuery<ReactiveComponent> _reactiveQuery;
    private EntityQuery<PhysicalCompositionComponent> _compositionQuery;
    private EntityQuery<ScpComponent> _scpQuery;

    private TimeSpan DecayInterval = TimeSpan.FromSeconds(18);

    public override void Initialize()
    {
        base.Initialize();

        _flammableQuery = GetEntityQuery<FlammableComponent>();
        _reactiveQuery = GetEntityQuery<ReactiveComponent>();
        _compositionQuery = GetEntityQuery<PhysicalCompositionComponent>();
        _scpQuery = GetEntityQuery<ScpComponent>();

        SubscribeLocalEvent<Scp457Component, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<Scp457Component, ScpIngestionConsumedEvent>(OnConsumed);
        SubscribeLocalEvent<Scp457Component, HitByWaterEvent>(OnHitByWater);
        SubscribeLocalEvent<Scp457Component, VentCrawlAttemptEvent>(OnVentCrawlAttempt);
        SubscribeLocalEvent<Scp457Component, StartCollideEvent>(OnStartCollide);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<Scp457Component>();
        while (query.MoveNext(out var uid, out var scp457))
        {
            if (scp457.NextAutoAbsorb < _timing.CurTime)
            {
                TryAbsorbNearby((uid, scp457));
                scp457.NextAutoAbsorb = _timing.CurTime + scp457.AutoAbsorbInterval;
            }

            if (scp457.NextChangeObjectSize > _timing.CurTime)
                continue;

            scp457.NextChangeObjectSize = _timing.CurTime + DecayInterval;
            if (scp457.ObjectSize > scp457.MinimumObjectSize && scp457.ObjectSizeDecay > 0f)
                TryChangeSize((uid, scp457), -scp457.ObjectSizeDecay);
        }
    }

    private void OnMapInit(Entity<Scp457Component> ent, ref MapInitEvent args)
    {
        ent.Comp.AppliedObjectSize = ent.Comp.ObjectSize;
        ent.Comp.NextChangeObjectSize = _timing.CurTime + DecayInterval;
        ent.Comp.NextAutoAbsorb = _timing.CurTime + ent.Comp.AutoAbsorbInterval;

        if (TryComp<PassiveDamageComponent>(ent, out var passiveDamage))
            ent.Comp.BasePassiveDamage = new DamageSpecifier(passiveDamage.Damage);

        UpdateAppearanceAndPhysics(ent);
        UpdatePassiveDamage(ent);
    }

    private void OnConsumed(Entity<Scp457Component> ent, ref ScpIngestionConsumedEvent args)
    {
        if (args.Entity is not { } food || (!CanConsume(ent, food) && !IsBurnableSolution(ent, args.ConsumedSolution)))
            return;

        TryChangeSize(ent, ent.Comp.ObjectSizeFlammableAdd);
    }

    public bool TryAbsorbNearby(Entity<Scp457Component> ent)
    {
        using var targets = HashSetPool<EntityUid>.Rent();
        _lookup.GetEntitiesInRange(ent.Owner, 4f, targets.Value, LookupFlags.Uncontained);

        var absorbed = false;
        foreach (var target in targets.Value)
        {
            if (target == ent.Owner || !TryConsume(ent, target))
                continue;

            absorbed = true;
        }

        return absorbed;
    }

    private void OnHitByWater(Entity<Scp457Component> ent, ref HitByWaterEvent args)
    {
        TryChangeSize(ent, -ent.Comp.ObjectWaterSizeDecrease);
    }

    private void OnVentCrawlAttempt(Entity<Scp457Component> ent, ref VentCrawlAttemptEvent args)
    {
        if (ent.Comp.ObjectSize > ent.Comp.SmallFormSize)
            args.Cancel();
    }

    private void OnStartCollide(Entity<Scp457Component> ent, ref StartCollideEvent args)
    {
        if (ent.Comp.ObjectSize < ent.Comp.StructuralBreakSize ||
            args.OurFixtureId != ent.Comp.BodyFixtureId)
            return;

        if (!TryComp<DamageableComponent>(args.OtherEntity, out var damageable))
            return;

        var damage = new DamageSpecifier();
        damage.DamageDict["Structural"] = ent.Comp.StructuralDamage * ent.Comp.DamageModifier;
        _damageable.TryChangeDamage((args.OtherEntity, damageable), damage, interruptsDoAfters: false, origin: ent);
    }

    public bool CanConsume(Entity<Scp457Component> ent, EntityUid target)
    {
        if (!Exists(target) || !HasComp<ItemComponent>(target))
            return false;

        if (_scpQuery.HasComp(target)) // Что бы не кушал другие объекты (нп. 999, 1508, 2295, 131)
            return false;

        if (_flammableQuery.HasComponent(target))
            return true;

        if (_reactiveQuery.TryGetComponent(target, out var reactive) &&
            reactive.ReactiveGroups is { } reactiveGroups)
        {
            foreach (var group in reactiveGroups.Keys)
            {
                if (ent.Comp.ReactiveGroupsWhitelist.Contains(group))
                    return true;
            }
        }

        if (_compositionQuery.TryGetComponent(target, out var composition))
        {
            foreach (var material in composition.MaterialComposition.Keys)
            {
                if (ent.Comp.FlammableMaterialsWhitelist.Contains(material))
                    return true;
            }
        }

        if (!TryComp<SolutionManagerComponent>(target, out var manager))
            return false;

        if (!_solution.TryGetSolution((target, manager), manager.Container, out _, out var solution))
            return false;

        if (IsBurnableSolution(ent, solution))
            return true;

        return false;
    }

    private bool IsBurnableSolution(Entity<Scp457Component> ent, Solution solution)
    {
        foreach (var reagent in solution.Contents)
        {
            if (!_proto.TryIndex<ReagentPrototype>(reagent.Reagent.Prototype, out var reagentPrototype))
                continue;

            if (reagentPrototype.ReactiveEffects is not { } effects)
                continue;

            foreach (var group in effects.Keys)
            {
                if (ent.Comp.ReactiveGroupsWhitelist.Contains(group))
                    return true;
            }
        }

        return false;
    }

    public bool TryConsume(Entity<Scp457Component> ent, EntityUid target)
    {
        if (!CanConsume(ent, target))
            return false;

        if (TryComp<StackComponent>(target, out var stack) && !_stack.TryUse((target, stack), 1))
            return false;

        if (!HasComp<StackComponent>(target))
            QueueDel(target);

        TryChangeSize(ent, ent.Comp.ObjectSizeFlammableAdd);
        return true;
    }

    private void TryChangeSize(Entity<Scp457Component> ent, float delta)
    {
        if (_safeTime.IsInSafeTime(ent.Owner, true, false))
            return;

        var component = ent.Comp;
        var oldSize = component.ObjectSize;
        var newSize = Math.Clamp(oldSize + delta, component.MinimumObjectSize, component.ObjectSizeLimit);
        var actualDelta = newSize - oldSize;
        if (MathF.Abs(actualDelta) < 0.0001f)
            return;

        component.ObjectSize = newSize;

        var growthSteps = component.ObjectSizeFlammableAdd > 0f
            ? actualDelta / component.ObjectSizeFlammableAdd
            : 0f;
        component.DamageModifier = Math.Clamp(
            component.DamageModifier + growthSteps * component.DamageModifierFlammableAdd,
            1f,
            component.DamageModifierLimit);
        component.RegenerationModifier = Math.Clamp(
            component.RegenerationModifier + growthSteps * component.RegenerationModifierFlammableAdd,
            1f,
            component.RegenerationModifierLimit);

        UpdateAppearanceAndPhysics(ent);
        UpdatePassiveDamage(ent);
    }

    private void UpdateAppearanceAndPhysics(Entity<Scp457Component> ent)
    {
        var component = ent.Comp;
        _scaleSprite.Scale(ent.Owner, new Vector2(component.ObjectSize));

        if (component.AppliedObjectSize > 0f && !MathHelper.CloseTo(component.AppliedObjectSize, component.ObjectSize))
            _physics.ScaleFixtures(ent.Owner, component.ObjectSize / component.AppliedObjectSize);

        component.AppliedObjectSize = component.ObjectSize;

        if (!TryComp<FixturesComponent>(ent, out var fixtures))
            return;

        if (!fixtures.Fixtures.TryGetValue(component.BodyFixtureId, out var fixture))
            return;

        var collisionMask = component.ObjectSize <= component.SmallFormSize
            ? (int)CollisionGroup.SmallMobMask
            : (int)CollisionGroup.MobMask;
        if (fixture.CollisionMask == collisionMask)
            return;

        _physics.SetCollisionMask(ent.Owner, component.BodyFixtureId, fixture, collisionMask, fixtures);
    }

    private void UpdatePassiveDamage(Entity<Scp457Component> ent)
    {
        if (ent.Comp.BasePassiveDamage is not { } baseDamage)
            return;

        if (!TryComp<PassiveDamageComponent>(ent, out var passiveDamage))
            return;

        passiveDamage.Damage = DamageSpecifier.ApplyModifier(baseDamage, 1f, ent.Comp.RegenerationModifier);
        Dirty(ent.Owner, passiveDamage);
    }
}

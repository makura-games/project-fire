using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Popups;
using Content.Server.Hands.Systems;
using Content.Server.Popups;
using Content.Shared._Scp.Proximity;
using Content.Shared._Scp.Scp330;
using Content.Shared.Containers;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Storage;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Content.Shared.Body;

namespace Content.Server._Scp.Scp330;

// TODO: Добавить, что миску можно будет бросить. При этом конфеты высыпятся и приколиста кастрирует.
public sealed partial class Scp330System : SharedScp330System
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private ProximitySystem _proximity = default!;
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private ContainerSystem _container = default!;
    [Dependency] private IRobustRandom _random = default!;

    private readonly HashSet<Entity<HandsComponent>> _cachedEntities = [];

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Scp330BowlComponent, MapInitEvent>(OnBowlMapInit, after: [typeof(ContainerFillSystem)]);

        InitializeCandy();
    }

    #region Event handlers

    private void OnBowlMapInit(Entity<Scp330BowlComponent> ent, ref MapInitEvent args)
    {
        TryAssignEffects(ent);
    }

    #endregion

    #region Take candy

    public override bool TryTakeCandy(Entity<Scp330BowlComponent> ent, EntityUid user)
    {
        var container = _container.EnsureContainer<Container>(ent, StorageComponent.ContainerId);
        if (container.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("scp330-bowl-empty"), ent, user);
            return false;
        }

        var item = _random.Pick(container.ContainedEntities);

        if (!_hands.CanPickupAnyHand(user, item))
            return false;

        if (!TrySignCandy(ent, item, user))
            return false;

        if (!_hands.TryPickupAnyHand(user, item))
            return false;

        if (ent.Comp.ThiefCounter[user] > ent.Comp.PunishmentAfter)
            ApplyPunishment(ent, user);

        return true;
    }

    private bool TrySignCandy(Entity<Scp330BowlComponent> ent, Entity<Scp330CandyComponent?> candy, EntityUid user)
    {
        if (!Resolve(candy, ref candy.Comp, false))
            return false;

        candy.Comp.TakenBy = user;

        ent.Comp.ThiefCounter.TryAdd(user, 0);
        ent.Comp.ThiefCounter[user]++;
        Dirty(ent);

        return true;
    }

    #endregion

    #region Punishment

    protected override void ApplyPunishment(Entity<Scp330BowlComponent> ent, EntityUid user)
    {
        base.ApplyPunishment(ent, user);

        var extra = Math.Max(1, ent.Comp.ThiefCounter[user] - ent.Comp.PunishmentAfter);
        var radius = ent.Comp.BasePunishmentRadius + (extra * 0.5f);

        var bowlCoords = _transform.GetMapCoordinates(ent);
        _cachedEntities.Clear();
        _lookup.GetEntitiesInRange(bowlCoords, radius, _cachedEntities, LookupFlags.Dynamic);

        foreach (var target in _cachedEntities)
        {
            if (_mobState.IsIncapacitated(target))
                continue;

            // Проверяем на видимость. Подходят только цели без препятствий/за стеклом
            if (!_proximity.IsRightType(ent, target, LineOfSightBlockerLevel.Transparent))
                continue;

            var damage = CalculateDamage(target, bowlCoords, radius, in ent.Comp.BaseDamage);
            _damageable.TryChangeDamage(target.Owner, damage, true, origin: ent);
        }

        CutOffHands(ent, user);
    }

    private void CutOffHands(EntityUid bowl, EntityUid target)
    {
        if (!TryComp<BodyComponent>(target, out var body))
            return;

        _hands.RemoveHands(target);
        _popup.PopupEntity(Loc.GetString("scp330-removed-hands"), target, target, PopupType.LargeCaution);

        var selfEvent = new Scp330SelfPunishmentEvent(target);
        RaiseLocalEvent(bowl, ref selfEvent);

        var targetEvent = new Scp330TargetPunishmentEvent(bowl);
        RaiseLocalEvent(bowl, ref targetEvent);
    }

    #endregion

    #region Helpers

    private DamageSpecifier CalculateDamage(EntityUid target, MapCoordinates bowlCoords, float maxRange, in DamageSpecifier baseDamage)
    {
        var targetCoords = _transform.GetMapCoordinates(target);
        var distance = Vector2.Distance(bowlCoords.Position, targetCoords.Position);
        var falloff = Math.Clamp(1f - (distance / maxRange), 0f, 1f);

        var newDamage = new DamageSpecifier();
        foreach (var (type, value) in baseDamage.DamageDict)
        {
            newDamage.DamageDict[type] = value * falloff;
        }

        return newDamage;
    }

    #endregion
}

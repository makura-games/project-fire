using System.Linq;
using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Server.Ghost;
using Content.Server.Mind;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server.Storage.EntitySystems;
using Content.Server.Ghost.Roles.Raffles;
using Content.Server.Ghost.Roles.Components;
using Content.Shared._Scp.Scp035;
using Content.Shared.Chat;
using Content.Shared.Chemistry.Components;
using Content.Shared.Clothing;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Content.Shared.Item;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Light.Components;
using Content.Shared.Maps;
using Content.Shared.Pointing;
using Content.Shared.Popups;
using Content.Shared.Storage.Components;
using Content.Shared.Tag;
using Content.Shared.Throwing;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Content.Shared.Whitelist;
using Content.Shared.Chemistry.EntitySystems;
using Robust.Server.GameObjects;

namespace Content.Server._Scp.Scp035;

public sealed partial class Scp035System : SharedScp035System
{
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ChatSystem _chatSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPuddleSystem _puddle = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private EntityStorageSystem _entityStorage = default!;
    [Dependency] private GhostSystem _ghost = default!;
    [Dependency] private TileSystem _tile = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private MapSystem _map = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Scp035MaskComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<Scp035MaskUserComponent, MaskRaiseArmyActionEvent>(OnRaiseArmy);
        SubscribeLocalEvent<Scp035MaskUserComponent, AfterPointedAtEvent>(OnPointedAt);
    }

    private void OnMapInit(Entity<Scp035MaskComponent> ent, ref MapInitEvent args)
    {
        var toggleUsed = new ItemToggledEvent(false, true, null);
        RaiseLocalEvent(ent, ref toggleUsed);
    }

    protected override void OnMaskEquipped(Entity<Scp035MaskComponent> ent, ref ClothingGotEquippedEvent args)
    {
        base.OnMaskEquipped(ent, ref args);

        if (!_mind.TryGetMind(args.Wearer, out var mindId, out var mind))
            return;

        if (!_ghost.OnGhostAttempt(mindId, false, false, false, mind))
            return;

        EnsureComp<GhostTakeoverAvailableComponent>(args.Wearer);
        var ghostRoleComp = EnsureComp<GhostRoleComponent>(args.Wearer);
        ghostRoleComp.RoleName = Loc.GetString("scp-035-ghost-role-name");
        ghostRoleComp.RoleDescription = Loc.GetString("scp-035-ghost-role-desc");
        ghostRoleComp.RaffleConfig = new GhostRoleRaffleConfig(ent.Comp.GhostSettings);
    }

    private void OnRaiseArmy(Entity<Scp035MaskUserComponent> ent, ref MaskRaiseArmyActionEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.Servants.Count >= ent.Comp.MaxServants)
        {
            _popup.PopupEntity(Loc.GetString("scp-035-max-servants"), ent, ent, PopupType.MediumCaution);
            return;
        }

        var servant = Spawn(ent.Comp.ServantsProto, Transform(ent).Coordinates);
        var comp = EnsureComp<Scp035ServantComponent>(servant);
        comp.User = ent;
        Dirty(servant, comp);

        ent.Comp.Servants.Add(servant);
        _npc.SetBlackboard(servant, NPCBlackboard.FollowTarget, new EntityCoordinates(ent, Vector2.Zero));
        UpdateServantNpc(servant, ent.Comp.CurrentOrder);

        args.Handled = true;
    }

    private void OnPointedAt(Entity<Scp035MaskUserComponent> ent, ref AfterPointedAtEvent args)
    {
        if (ent.Comp.CurrentOrder != MaskOrderType.Kill)
            return;

        foreach (var servant in ent.Comp.Servants)
        {
            _npc.SetBlackboard(servant, NPCBlackboard.CurrentOrderedTarget, args.Pointed);
        }
    }

    public override void UpdateServantNpc(EntityUid uid, MaskOrderType orderType)
    {
        if (!TryComp<HTNComponent>(uid, out var htn))
            return;

        if (htn.Plan != null)
            _htn.ShutdownPlan(htn);

        _npc.SetBlackboard(uid, NPCBlackboard.CurrentOrders, orderType);
        _htn.Replan(htn);
    }


    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<Scp035MaskComponent>();
        while (query.MoveNext(out var uid, out var maskComponent))
        {
            if (maskComponent.User.HasValue)
                continue;

            HandleMessaging((uid, maskComponent));
            HandleLiquidSpawning((uid, maskComponent));
        }
    }

    private void HandleMessaging(Entity<Scp035MaskComponent> entity)
    {
        var curTime = _gameTiming.CurTime;

        if (curTime < entity.Comp.NextMessaging)
            return;

        if (entity.Comp.Messages.Count == 0)
            return;

        var message = Loc.GetString(_random.Pick(entity.Comp.Messages));
        _chatSystem.TrySendInGameICMessage(entity, message, InGameICChatType.Speak, ChatTransmitRange.Normal, ignoreActionBlocker: true);

        entity.Comp.NextMessaging = curTime + entity.Comp.NextMessageDelay;
    }

    private void HandleLiquidSpawning(Entity<Scp035MaskComponent> entity)
    {
        var curTime = _gameTiming.CurTime;
        if (curTime < entity.Comp.NextLiquidSpawning)
            return;

        var coords = Transform(entity).Coordinates;

        var tempSol = new Solution();
        tempSol.AddReagent(entity.Comp.ReagentName, 25);
        _puddle.TrySpillAt(coords, tempSol, out _);

        FixedPoint2 total = 0;
        var puddles = _lookup.GetEntitiesInRange<PuddleComponent>(coords, entity.Comp.ReagentRangeAvailable).ToList();
        foreach (var puddle in puddles)
        {
            if (!_solutionContainer.TryGetSolution(puddle.Owner, entity.Comp.ReagentName, out _, out var solution) ||
                solution == null)
                continue;

            var allReagents = solution.GetReagentPrototypes(_prototypeManager);
            total = allReagents.Where(reagent => reagent.Key.ID == entity.Comp.ReagentName).Aggregate(total, (current, reagent) => current + reagent.Value);
        }

        if (total >= entity.Comp.ReagentDestructLevel)
        {
            var xform = Transform(entity);
            if (!TryComp<MapGridComponent>(xform.GridUid, out var map))
                return;

            var tiles = _map.GetTilesIntersecting(xform.GridUid.Value, map, Box2.CenteredAround(_transformSystem.GetWorldPosition(xform),
                entity.Comp.CorrosionBox)).ToArray();

            _random.Shuffle(tiles);

            for (var i = 0; i < entity.Comp.MaxTilesCorrosionPry; i++)
            {
                if (!tiles.TryGetValue(i, out var value))
                    continue;

                _tile.PryTile(value);
            }

            var lookup = _lookup.GetEntitiesInRange(entity, entity.Comp.EntityCorrosionRange, LookupFlags.Approximate | LookupFlags.Static);
            var tags = GetEntityQuery<TagComponent>();
            var entityStorage = GetEntityQuery<EntityStorageComponent>();
            var items = GetEntityQuery<ItemComponent>();
            var lights = GetEntityQuery<PoweredLightComponent>();

            foreach (var ent in lookup)
            {

                // break whitelist entities
                if (_whitelist.CheckBoth(ent, entity.Comp.BlacklistStructures, entity.Comp.WhitelistStructures))
                    _damageable.TryChangeDamage(ent, entity.Comp.DamageSpecif, true);

                // randomly opens some lockers and such.
                if (entityStorage.TryGetComponent(ent, out var entstorecomp))
                    _entityStorage.OpenStorage(ent, entstorecomp);

                // chucks items
                if (items.HasComponent(ent) &&
                    TryComp<PhysicsComponent>(ent, out var phys) && phys.BodyType != BodyType.Static)
                    _throwing.TryThrow(ent, _random.NextAngle().ToWorldVec());

                // flicker lights
                if (lights.HasComponent(ent))
                    _ghost.DoGhostBooEvent(ent);
            }
        }

        entity.Comp.NextLiquidSpawning = curTime + entity.Comp.NextLiquidSpawnDelay;
    }
}

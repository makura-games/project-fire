using System.Numerics;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.Humanoid;
using Robust.Shared.Map;

namespace Content.Server._Scp.Backrooms.NPCAutoOrder;

// TODO: Избавиться от этого, когда питомцы будут вмержены в мастер санрайза

public sealed partial class NpcAutoOrderSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private NPCSystem _npc = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NpcAutoOrderComponent, ComponentInit>(OnInit);
    }

    private void OnInit(Entity<NpcAutoOrderComponent> npc, ref ComponentInit args)
    {
        var xformQuery = GetEntityQuery<TransformComponent>();
        var xform = xformQuery.GetComponent(npc);

        var mobs = new HashSet<Entity<HumanoidProfileComponent>>();
        _lookup.GetEntitiesInRange(xform.Coordinates, 1, mobs);

        foreach (var entity in mobs)
        {
            _npc.SetBlackboard(npc,
                NPCBlackboard.FollowTarget,
                new EntityCoordinates(entity, Vector2.Zero));
        }

        UpdatePetNpc(npc, npc.Comp.Order);
    }

    private void UpdatePetNpc(EntityUid uid, NpcAutoOrder orderType)
    {
        if (!TryComp<HTNComponent>(uid, out var htn))
            return;

        if (htn.Plan != null)
            _htn.ShutdownPlan(htn);

        _npc.SetBlackboard(uid, NPCBlackboard.CurrentOrders, orderType);

        _htn.Replan(htn);
    }
}

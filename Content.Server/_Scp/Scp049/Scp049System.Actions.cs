using Content.Server.DoAfter;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Hands.Systems;
using Content.Server.NPC.HTN;
using Content.Server.Popups;
using Content.Server.Zombies;
using Content.Shared._Scp.Fear.Components;
using Content.Shared._Scp.Mobs.Components;
using Content.Shared._Scp.Proximity;
using Content.Shared._Scp.Scp049;
using Content.Shared._Scp.Scp049.Scp049Protection;
using Content.Shared.Administration.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Traits.Assorted;
using Robust.Server.Player;

namespace Content.Server._Scp.Scp049;

public sealed partial class Scp049System
{
    [Dependency] private MobStateSystem _mob = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private RejuvenateSystem _rejuvenate = default!;
    [Dependency] private ZombieSystem _zombie = default!;
    [Dependency] private DoAfterSystem _doAfter = default!;
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private IPlayerManager _player = default!;

    private void InitializeActions()
    {
        SubscribeLocalEvent<Scp049Component, Scp049KillResurrectedAction>(OnKillResurrected);
        SubscribeLocalEvent<Scp049Component, Scp049ResurrectAction>(OnResurrect);
        SubscribeLocalEvent<Scp049Component, Scp049SelfHealAction>(OnSelfHeal);
        SubscribeLocalEvent<Scp049Component, Scp049HealMinionAction>(OnHealMinion);
    }

    private void OnHealMinion(Entity<Scp049Component> ent, ref Scp049HealMinionAction args)
    {
        if (args.Handled)
            return;

        if (!HasComp<Scp049MinionComponent>(args.Target))
            return;

        Heal(args.Target, args.Performer);

        args.Handled = true;
    }

    private void OnSelfHeal(Entity<Scp049Component> ent, ref Scp049SelfHealAction args)
    {
        if (args.Handled)
            return;

        Heal(args.Performer, args.Performer);

        args.Handled = true;
    }

    private void Heal(EntityUid target, EntityUid performer)
    {
        _rejuvenate.PerformRejuvenate(target);

        var targetName = Identity.Name(target, EntityManager);
        var localeMessage = Loc.GetString("scp049-heal-minion", ("target", targetName));
        _popup.PopupEntity(localeMessage, performer, PopupType.Medium);
    }

    private void OnResurrect(Entity<Scp049Component> scpEntity, ref Scp049ResurrectAction args)
    {
        if (args.Handled)
            return;

        var hasTool = false;

        foreach (var heldUid in _hands.EnumerateHeld(scpEntity.Owner))
        {
            var prototype = Prototype(heldUid);

            if (prototype == null)
                continue;

            if (prototype.ID != scpEntity.Comp.NextTool)
                continue;

            hasTool = true;
            break;
        }

        if (!hasTool)
        {
            var message = Loc.GetString("scp049-missing-surgery-tool", ("instrument", Loc.GetEntityData(scpEntity.Comp.NextTool).Name));
            _popup.PopupEntity(message, scpEntity, scpEntity, PopupType.MediumCaution);

            return;
        }

        var doAfterArgs = new DoAfterArgs(EntityManager,
            scpEntity,
            scpEntity.Comp.ResurrectionTime,
            new ScpResurrectionDoAfterEvent(),
            target: args.Target,
            eventTarget: scpEntity)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            BreakOnDropItem = true,
            BreakOnHandChange = true,
        };

        args.Handled = _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnKillResurrected(Entity<Scp049Component> ent, ref Scp049KillResurrectedAction args)
    {
        if (args.Handled)
            return;

        if (!_zombie.UnZombify(args.Target, args.Target, null))
            return;

        _mob.ChangeMobState(args.Target, MobState.Dead);
        RemComp<Scp049MinionComponent>(args.Target);

        EnsureComp<UnrevivableComponent>(args.Target);

        var targetName = Identity.Name(args.Target, EntityManager);
        var performerName = Identity.Name(args.Target, EntityManager);

        var localeMessage = Loc.GetString("scp049-touch-action-success",
            ("target", targetName),
            ("performer", performerName));

        _popup.PopupEntity(localeMessage, ent, PopupType.MediumCaution);

        args.Handled = true;
    }

    private bool TryMakeMinion(Entity<MobStateComponent> minionEntity, Entity<Scp049Component> scpEntity)
    {
        if (HasComp<Scp049ProtectionComponent>(minionEntity))
            return false;

        if (HasComp<ScpComponent>(minionEntity))
            return false;

        if (!HasComp<HumanoidProfileComponent>(minionEntity))
            return false;

        MakeMinion(minionEntity, scpEntity);

        return true;
    }

    private void MakeMinion(Entity<MobStateComponent> minionEntity, Entity<Scp049Component> scpEntity)
    {
        var minionComponent = EnsureComp<Scp049MinionComponent>(minionEntity.Owner);
        EnsureComp<ScpShow049HudComponent>(minionEntity);

        minionComponent.Scp049Owner = scpEntity;
        Dirty(minionEntity.Owner, minionComponent);

        scpEntity.Comp.Minions.Add(minionEntity);

        var zombieComponent = BuildZombieComponent(minionEntity);
        _zombie.ZombifyEntity(minionEntity, zombieComponentOverride: zombieComponent);

        EnsureComp<NonSpreaderZombieComponent>(minionEntity);

        _rejuvenate.PerformRejuvenate(minionEntity);
        _mob.ChangeMobState(minionEntity, MobState.Alive);

        RemComp<HTNComponent>(minionEntity);
        RemComp<FearComponent>(minionEntity);
        RemComp<ProximityTargetComponent>(minionEntity);

        TryMakeAGhostRole(minionEntity);
    }

    private void TryMakeAGhostRole(EntityUid minionUid)
    {
        // Если игрок в теле, то не даем призракам возможности занять его тело
        if (_player.TryGetSessionByEntity(minionUid, out _))
            return;

        var ghostRoleComponent = EnsureComp<GhostRoleComponent>(minionUid);
        ghostRoleComponent.RoleName = Loc.GetString("scp049-ghost-role-name");
        ghostRoleComponent.RoleDescription = Loc.GetString("scp049-ghost-role-description");
        ghostRoleComponent.RoleRules = Loc.GetString("scp049-ghost-role-rules");

        EnsureComp<GhostTakeoverAvailableComponent>(minionUid);
    }
}

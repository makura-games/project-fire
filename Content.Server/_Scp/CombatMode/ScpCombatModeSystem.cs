using Content.Shared.CombatMode;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Robust.Shared.Player;

namespace Content.Server._Scp.CombatMode;

/// <summary>
/// Disables combat mode when a mob dies or its mind leaves the body.
/// </summary>
public sealed partial class ScpCombatModeSystem : EntitySystem
{
    [Dependency] private SharedCombatModeSystem _combatMode = null!;
    [Dependency] private SharedMindSystem _mind = null!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CombatModeComponent, MindRemovedMessage>(OnMindRemoved);
        SubscribeLocalEvent<CombatModeComponent, PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<CombatModeComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMindRemoved(Entity<CombatModeComponent> ent, ref MindRemovedMessage args)
    {
        ResetCombatMode(ent);
    }

    private void OnPlayerDetached(Entity<CombatModeComponent> ent, ref PlayerDetachedEvent args)
    {
        // При отключении клиента разум остаётся в теле; Visit меняет CurrentEntity до отсоединения игрока.
        if (!_mind.TryGetMind(args.Player.UserId, out _, out var mind) || mind.CurrentEntity == ent.Owner)
            return;

        ResetCombatMode(ent);
    }

    private void OnMobStateChanged(Entity<CombatModeComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        ResetCombatMode(ent);
    }

    private void ResetCombatMode(Entity<CombatModeComponent> ent)
    {
        if (TerminatingOrDeleted(ent))
            return;

        _combatMode.SetInCombatMode(ent, false, ent.Comp);
    }
}

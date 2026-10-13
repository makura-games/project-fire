using Content.Shared._Scp.Holding.Components;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Actions.Events;
using Content.Shared.CombatMode;
using Content.Shared.Popups;

namespace Content.Shared._Scp.Holding.Systems;

public abstract partial class SharedScpHoldingSystem
{
    [Dependency] private SharedCombatModeSystem _combatMode = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedActionsSystem _actions = default!;

    private void InitializeRestrictions()
    {
        SubscribeLocalEvent<ScpHoldRestrictedComponent, ComponentInit>(OnRestrictionInit);
        SubscribeLocalEvent<ScpHoldRestrictedComponent, ComponentShutdown>(OnRestrictRemove);
        SubscribeLocalEvent<ScpHoldRestrictedComponent, ActionAttemptEvent>(OnHoldRestrictedActionAttempt);
    }

    private void OnRestrictionInit(Entity<ScpHoldRestrictedComponent> ent, ref ComponentInit args)
    {
        ValidateActions(ent.AsNullable());
    }

    private void OnRestrictRemove(Entity<ScpHoldRestrictedComponent> ent, ref ComponentShutdown args)
    {
        ValidateActions(ent.AsNullable());
    }

    private void ValidateAllActions(Entity<ActionsComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        foreach (var action in ent.Comp.Actions)
        {
            ValidateActions(action);
        }
    }

    private void ValidateActions(Entity<ScpHoldRestrictedComponent?> ent, ActionComponent? comp = null)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        if (!Resolve(ent, ref comp))
            return;

        if (!comp.AttachedEntity.HasValue)
            return;

        var shouldBeBlocked = IsHeldAtStage(comp.AttachedEntity.Value, ent.Comp.Stage);
        if (shouldBeBlocked)
        {
            if (TryComp<CombatModeComponent>(comp.AttachedEntity.Value, out var combat))
                _combatMode.SetInCombatMode(comp.AttachedEntity.Value, false, combat);
        }

        _actions.SetEnabled(ent.Owner, !shouldBeBlocked);
    }

    private void OnHoldRestrictedActionAttempt(Entity<ScpHoldRestrictedComponent> ent, ref ActionAttemptEvent args)
    {
        if (args.Cancelled || !IsHeldAtStage(args.User, ent.Comp.Stage))
            return;

        _popup.PopupClient(Loc.GetString("scp-hold-action-restricted"), args.User, args.User);
        args.Cancelled = true;
    }

    public bool IsHeldAtStage(EntityUid uid, ScpHoldStage stage)
    {
        return _activeHoldableQuery.TryComp(uid, out var held) && IsHeldAtStage((uid, held), stage);
    }

    private bool IsHeldAtStage(Entity<ActiveScpHoldableComponent> held, ScpHoldStage stage)
    {
        return stage switch
        {
            ScpHoldStage.Soft => true,
            ScpHoldStage.Full => _activeHoldableFullHoldStateQuery.HasComp(held),
            _ => false,
        };
    }
}

using Content.Shared._Scp.Amnesiac;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Scp.Amnesiac;

public sealed partial class AmnesiacSystem : EntitySystem
{
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private SleepingSystem _sleeping = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!;
    [Dependency] private IGameTiming _timing = default!;

    [Dependency] private EntityQuery<BloodstreamComponent> _bloodstreamQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AmnesiacStatusEffectComponent, StatusEffectAppliedEvent>(OnApplied);
        SubscribeNetworkEvent<AmnesiacConfirmEvent>(OnConfirm);
    }

    private void OnApplied(Entity<AmnesiacStatusEffectComponent> ent, ref StatusEffectAppliedEvent args)
    {
        ent.Comp.ConfirmAvailableAt = _timing.CurTime + ent.Comp.ConfirmDelay;

        if (!TryComp<ActorComponent>(args.Target, out var actor))
            return;

        RaiseNetworkEvent(
            new AmnesiacInfoEvent(ent.Comp.Reagent, ent.Comp.ErasedMemory, ent.Comp.ConfirmDelay),
            actor.PlayerSession);
    }

    private void OnConfirm(AmnesiacConfirmEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } target)
            return;

        if (!_statusEffects.TryEffectsWithComp<AmnesiacStatusEffectComponent>(target, out var effects))
            return;

        var confirmed = false;
        foreach (var effect in effects)
        {
            if (_timing.CurTime < effect.Comp1.ConfirmAvailableAt)
                continue;

            RemoveReagent(target, effect.Comp1.Reagent);
            QueueDel(effect.Owner);
            confirmed = true;
        }

        if (!confirmed)
            return;

        _statusEffects.TryRemoveStatusEffect(target, SleepingSystem.StatusEffectForcedSleeping);
        _sleeping.TryWaking(target, force: true);
    }

    private void RemoveReagent(EntityUid target, ProtoId<ReagentPrototype> reagent)
    {
        if (!_bloodstreamQuery.TryComp(target, out var bloodstream))
            return;

        if (!_solution.ResolveSolution(target, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out var solution))
            return;

        var amount = solution.GetTotalPrototypeQuantity(reagent);
        if (amount > 0)
            _solution.RemoveReagent(bloodstream.BloodSolution.Value, reagent.Id, amount);
    }
}

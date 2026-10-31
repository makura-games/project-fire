using Content.Shared._Scp.Amnesiac;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.StatusEffectNew;
using Content.Shared.StatusEffectNew.Components;
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

    [Dependency] private EntityQuery<AmnesiacStatusEffectComponent> _amnesiacQuery = default!;
    [Dependency] private EntityQuery<BloodstreamComponent> _bloodstreamQuery = default!;
    [Dependency] private EntityQuery<StatusEffectComponent> _statusEffectQuery = default!;

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
            new AmnesiacInfoEvent(GetNetEntity(ent), ent.Comp.Reagent, ent.Comp.ErasedMemory, ent.Comp.ConfirmDelay),
            actor.PlayerSession);
    }

    private void OnConfirm(AmnesiacConfirmEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } target)
            return;

        if (!TryGetEntity(ev.Effect, out var effect) ||
            !_amnesiacQuery.TryComp(effect, out var amnesiac) ||
            !_statusEffectQuery.TryComp(effect, out var statusEffect))
            return;

        if (statusEffect.AppliedTo != target)
            return;

        if (_timing.CurTime < amnesiac.ConfirmAvailableAt)
            return;

        RemoveReagent(target, amnesiac.Reagent);
        QueueDel(effect);

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

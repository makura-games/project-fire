using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.StatusEffectNew;
using Content.Shared.StatusEffectNew.Components;
using Robust.Shared.Analyzers;

namespace Content.Shared._CE.ZLevels.Damage.SoftLanding;

public sealed partial class CESoftLandingStatusEffectSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StandingStateSystem _standingState = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CESoftLandingStatusEffectComponent, StatusEffectRelayedEvent<CEZFallingDamageCalculateEvent>>(OnFallingDamageCalculate);
    }

    private void OnFallingDamageCalculate(Entity<CESoftLandingStatusEffectComponent> ent, ref StatusEffectRelayedEvent<CEZFallingDamageCalculateEvent> args)
    {
        if (!TryComp<StatusEffectComponent>(ent, out var effect) || effect.AppliedTo is not { } target)
            return;
        var fallArgs = args.Args;

        if (_standingState.IsDown(target))
            return;

        if (fallArgs.Speed <= ent.Comp.MaxSpeedLimit)
        {
            fallArgs.DamageMultiplier *= ent.Comp.DamageMultiplier;
            fallArgs.StunMultiplier *= ent.Comp.StunMultiplier;

            _popup.PopupPredicted(Loc.GetString("ce-soft-landing"), target, target);
        }
        else
        {
            fallArgs.DamageMultiplier *= ent.Comp.DamageHardFallMultiplier;
            fallArgs.StunMultiplier *= ent.Comp.StunHardFallMultiplier;

            _popup.PopupPredicted(Loc.GetString("ce-soft-landing-too-high"), target, target, PopupType.SmallCaution);
        }
    }
}

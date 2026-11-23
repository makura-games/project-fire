using Content.Shared._Scp.Fear;
using Content.Shared._Scp.Fear.Components;
using Content.Shared._Scp.Fear.Components.Traits;
using Content.Shared.Speech.Components;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Random;

namespace Content.Server._Scp.Fear;

public sealed partial class FearSystem
{
    [Dependency] private StatusEffectsSystem _statusEffects = default!;

    private void InitializeTraits()
    {
        SubscribeLocalEvent<FearStuporComponent, FearStateChangedEvent>(OnStuporFearStateChanged);
        SubscribeLocalEvent<FearStutteringComponent, FearStateChangedEvent>(OnStutteringFearStateChanged);
        SubscribeLocalEvent<FearFaintingComponent, FearStateChangedEvent>(OnFaintingFearStateChanged);
    }

    /// <summary>
    /// Обрабатывает событие изменения уровня страха.
    /// С шансом вызывает оцепенение у персонажа с трейтом оцепенения.
    /// </summary>
    private void OnStuporFearStateChanged(Entity<FearStuporComponent> ent, ref FearStateChangedEvent args)
    {
        // Если старый стейт больше, значит персонаж успокоился
        // От этого не нужно впадать в ступор
        if (!IsIncreasing(args.NewState, args.OldState))
            return;

        if (args.NewState < ent.Comp.RequiredState)
            return;

        if (!TryComp<FearComponent>(ent, out var fear))
            return;

        if (!_random.Prob(ent.Comp.Chance * fear.EffectStrength))
            return;

        _statusEffects.TryAddStatusEffectDuration(ent, ent.Comp.StatusEffect, ent.Comp.StuporTime * fear.EffectStrength);
    }

    private void OnStutteringFearStateChanged(Entity<FearStutteringComponent> ent, ref FearStateChangedEvent args)
    {
        if (args.NewState == FearState.None)
        {
            ent.Comp.AppliedStrength = 1f;
            RemComp<StutteringAccentComponent>(ent);
            return;
        }

        if (!TryComp<FearComponent>(ent, out var fear))
            return;

        var stuttering = EnsureComp<StutteringAccentComponent>(ent);
        var modifier = args.NewState == args.OldState ? 1f : GetGenericFearBasedModifier(args.NewState, 1);
        // При обновлении силы меняем только уже применённый коэффициент, не повторяем множитель уровня.
        modifier *= fear.EffectStrength / ent.Comp.AppliedStrength;
        ent.Comp.AppliedStrength = fear.EffectStrength;

        stuttering.CutRandomProb *= modifier;
        stuttering.FourRandomProb *= modifier;
        stuttering.ThreeRandomProb *= modifier;
    }

    private void OnFaintingFearStateChanged(Entity<FearFaintingComponent> ent, ref FearStateChangedEvent args)
    {
        // Проверяем, что стейт не увеличивается
        if (!IsIncreasing(args.NewState, args.OldState))
            return;

        if (args.NewState < ent.Comp.RequiredState)
            return;

        if (!TryComp<FearComponent>(ent, out var fear))
            return;

        if (!_random.Prob(ent.Comp.Chance * fear.EffectStrength))
            return;

        _statusEffects.TryAddStatusEffectDuration(ent, ent.Comp.StatusEffect, ent.Comp.Time * fear.EffectStrength);
    }
}

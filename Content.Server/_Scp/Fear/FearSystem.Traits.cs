using Content.Shared._Scp.Fear;
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

        if (!_random.Prob(ent.Comp.Chance))
            return;

        _statusEffects.TryAddStatusEffectDuration(ent, ent.Comp.StatusEffect, ent.Comp.StuporTime);
    }

    private void OnStutteringFearStateChanged(Entity<FearStutteringComponent> ent, ref FearStateChangedEvent args)
    {
        if (args.NewState == FearState.None)
        {
            RemComp<StutteringAccentComponent>(ent);
            return;
        }

        var stuttering = EnsureComp<StutteringAccentComponent>(ent);
        var modifier = GetGenericFearBasedModifier(args.NewState, 1);

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

        if (!_random.Prob(ent.Comp.Chance))
            return;

        _statusEffects.TryAddStatusEffectDuration(ent, ent.Comp.StatusEffect, ent.Comp.Time);
    }
}

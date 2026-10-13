using System.Numerics;
using Content.Server._Scp.Blood;
using Content.Server._Scp.Misc.LiquidParticlesGenerator;
using Content.Shared._Scp.Blood;
using Content.Shared._Scp.Scp096.Main.Components;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;

namespace Content.Server._Scp.Scp096;

public sealed partial class Scp096System
{
    [Dependency] private BloodSplatterSystem _bloodSplatter = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainer = default!;

    private static readonly Angle BloodAngle = Angle.FromDegrees(360f);
    private const float BloodRadians = (float)Math.PI * 2f;
    private static readonly Vector2 BloodDistance = new (3f, 20f);

    #region Virtuals, Tears logic

    protected override void SpawnBlood(Entity<BloodSplattererComponent?> ent)
    {
        base.SpawnBlood(ent);

        if (!Resolve(ent, ref ent.Comp, false))
            return;

        _bloodSplatter.SpawnBloodParticles(ent!, ent, BloodAngle, BloodRadians, BloodDistance);
    }

    protected override void ToggleTears(Entity<Scp096FaceComponent> ent, bool value)
    {
        base.ToggleTears(ent, value);

        if (!TryComp<LiquidParticlesGeneratorComponent>(ent, out var generator))
            return;

        generator.Enabled = value;
    }

    protected override void ToggleTearsReagent(Entity<Scp096FaceComponent> ent, bool useDefaultReagent)
    {
        base.ToggleTearsReagent(ent, useDefaultReagent);

        if (!TryComp<BloodstreamComponent>(ent, out var bloodstream))
            return;

        var reagent = useDefaultReagent
            ? ent.Comp.TearsReagent
            : ent.Comp.BloodReagent;

        var volume = bloodstream.BloodReferenceSolution.Volume;
        bloodstream.BloodReferenceSolution.RemoveAllSolution();
        bloodstream.BloodReferenceSolution.AddReagent(reagent, volume);

        if (TryComp<SolutionRegenerationComponent>(ent, out var regeneration))
        {
            regeneration.Generated?.RemoveAllSolution();
            regeneration.Generated?.AddReagent(reagent, 20f);
            Dirty(ent, regeneration);
        }

        if (bloodstream.BloodSolution == null)
            return;

        Entity<SolutionComponent> bloodEnt = (ent.Owner, bloodstream.BloodSolution.Value.Comp);
        _solutionContainer.RemoveAllSolution(bloodEnt);
        _solutionContainer.TryAddReagent(bloodEnt, reagent, volume, out _, null);

        Dirty(ent, bloodstream);
    }

    protected override void ModifyTearsSpawnSpeed(Entity<Scp096FaceComponent> ent, bool cryFaster)
    {
        base.ModifyTearsSpawnSpeed(ent, cryFaster);

        if (!TryComp<LiquidParticlesGeneratorComponent>(ent, out var generator))
            return;

        if (cryFaster)
        {
            // Уже ускорены — повторно не трогаем, чтобы не накапливать деление.
            if (ent.Comp.CachedLiquidSpawnCooldown != null && ent.Comp.CachedCooldownVariation != null)
                return;

            ent.Comp.CachedLiquidSpawnCooldown = generator.Cooldown;
            ent.Comp.CachedCooldownVariation = generator.CooldownVariation;

            generator.Cooldown /= ent.Comp.LiquidSpawnCooldownDivisor;
            generator.CooldownVariation /= ent.Comp.LiquidSpawnCooldownDivisor;
        }
        else if (ent.Comp.CachedLiquidSpawnCooldown != null && ent.Comp.CachedCooldownVariation != null)
        {
            generator.Cooldown = ent.Comp.CachedLiquidSpawnCooldown.Value;
            generator.CooldownVariation = ent.Comp.CachedCooldownVariation.Value;

            ent.Comp.CachedLiquidSpawnCooldown = null;
            ent.Comp.CachedCooldownVariation = null;
        }
    }

    #endregion
}

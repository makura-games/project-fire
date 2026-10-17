using Content.Shared._CE.ZLevels.Damage;
using Content.Shared.StatusEffectNew;
using Content.Shared.StatusEffectNew.Components;

namespace Content.Shared._Scp.ZLevels;

/// <summary>
/// Allows status effects to modify falling damage and stun durations.
/// </summary>
public sealed partial class ZLevelStatusEffectRelaySystem : EntitySystem
{
    [Dependency] private StatusEffectsSystem _statusEffects = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StatusEffectContainerComponent, CEZFallingDamageCalculateEvent>(OnFallingDamageCalculate);
    }

    private void OnFallingDamageCalculate(Entity<StatusEffectContainerComponent> ent, ref CEZFallingDamageCalculateEvent args)
    {
        _statusEffects.RelayEvent(ent, args);
    }
}

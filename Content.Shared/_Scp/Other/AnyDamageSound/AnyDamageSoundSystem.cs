using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._Scp.Other.AnyDamageSound;

public sealed partial class AnyDamageSoundSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AnyDamageSoundComponent, DamageChangedEvent>(OnDamageChanged);
    }

    private void OnDamageChanged(Entity<AnyDamageSoundComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased)
            return;

        if (args.DamageDelta == null || args.DamageDelta.GetTotal() == FixedPoint2.Zero)
            return;

        if (ent.Comp.RequireExistingOrigin && args.Origin == null)
            return;

        if (_timing.CurTime < ent.Comp.NextSoundTime)
            return;

        _audio.PlayPredicted(ent.Comp.Sound, ent, args.Origin);
        ent.Comp.NextSoundTime = _timing.CurTime + ent.Comp.Cooldown;
        Dirty(ent);
    }
}

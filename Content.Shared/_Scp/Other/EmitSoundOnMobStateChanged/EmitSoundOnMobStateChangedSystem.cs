using Content.Shared.Mobs;
using Robust.Shared.Audio.Systems;

namespace Content.Shared._Scp.Other.EmitSoundOnMobStateChanged;

public sealed partial class EmitSoundOnMobStateChangedSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EmitSoundOnMobStateChangedComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(Entity<EmitSoundOnMobStateChangedComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != ent.Comp.State)
            return;

        _audio.PlayPvs(ent.Comp.Sound, ent);
    }
}

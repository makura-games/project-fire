using Content.Shared.Mobs;
using Robust.Client.Audio;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Player;

namespace Content.Client._Scp.Other.DeathSound;

public sealed partial class DeathSoundSystem : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IPlayerManager _player = default!;

    private readonly SoundSpecifier _sound = new SoundPathSpecifier("/Audio/_Scp/Effects/die.ogg");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActorComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(Entity<ActorComponent> ent, ref MobStateChangedEvent args)
    {
        if (_player.LocalSession?.AttachedEntity != args.Target)
            return;

        if (args.NewMobState != MobState.Dead)
            return;

        _audio.PlayGlobal(_sound, ent);
    }
}

using Content.Shared._Scp.Blinking;
using Content.Shared.GameTicking;
using Robust.Server.Audio;
using Robust.Shared.Audio;

namespace Content.Server._Scp.Other.PlayerSpawnSound;

public sealed partial class PlayerSpawnSoundSystem : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;

    private static readonly SoundSpecifier SpawnSound = new SoundCollectionSpecifier("SpawnSound");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeAllEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
    }

    private void OnPlayerSpawned(PlayerSpawnCompleteEvent ev)
    {
        // У моргания свое "приветствие" игрока со своим звуком.
        if (HasComp<BlinkableComponent>(ev.Player.AttachedEntity))
            return;

        _audio.PlayGlobal(SpawnSound, ev.Player, AudioParams.Default.WithVolume(-3f));
    }
}

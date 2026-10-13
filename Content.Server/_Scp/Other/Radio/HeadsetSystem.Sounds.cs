#pragma warning disable IDE0130
using Content.Server.Emp;
using Content.Shared.Radio.Components;
using Robust.Server.Audio;

namespace Content.Server.Radio.EntitySystems;

public sealed partial class HeadsetSystem
{
    [Dependency] private AudioSystem _audio = default!;

    private void InitializeHeadsetSounds()
    {
        SubscribeLocalEvent<HeadsetComponent, RadioSendAttemptEvent>(OnHeadsetSendAttempt, after: [typeof(EmpSystem)]);
    }

    private void OnHeadsetSendAttempt(Entity<HeadsetComponent> ent, ref RadioSendAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var wearer = Transform(ent).ParentUid;
        if (!wearer.IsValid())
            return;

        _audio.PlayEntity(ent.Comp.SendSound, wearer, ent);
    }

    private void PlayHeadsetReceiveSound(Entity<HeadsetComponent> ent, RadioReceiveEvent args)
    {
        var wearer = Transform(ent).ParentUid;
        if (!wearer.IsValid() || wearer == args.MessageSource)
            return;

        _audio.PlayEntity(ent.Comp.ReceiveSound, wearer, ent);
    }
}

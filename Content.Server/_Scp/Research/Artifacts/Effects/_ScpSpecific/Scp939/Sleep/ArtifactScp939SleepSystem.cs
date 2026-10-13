using Content.Server._Scp.Scp939;
using Content.Shared._Scp.Scp939;
using Content.Shared.Xenoarchaeology.Artifact;
using Content.Shared.Xenoarchaeology.Artifact.XAE;
using Robust.Shared.Random;

namespace Content.Server._Scp.Research.Artifacts.Effects._ScpSpecific.Scp939.Sleep;

public sealed partial class ArtifactScp939SleepSystem : BaseXAESystem<ArtifactScp939SleepComponent>
{
    [Dependency] private Scp939System _scp939 = default!;
    [Dependency] private IRobustRandom _random = default!;

    protected override void OnActivated(Entity<ArtifactScp939SleepComponent> ent, ref XenoArtifactNodeActivatedEvent args)
    {
        if (!TryComp<Scp939Component>(ent, out var scp939Component))
            return;

        var time = _random.NextFloat(ent.Comp.MinSleepTime, ent.Comp.MaxSleepTime);

        _scp939.TrySleep((ent, scp939Component), time);
    }
}

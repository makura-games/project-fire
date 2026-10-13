using Content.Server._Scp.Scp173;
using Content.Shared.Xenoarchaeology.Artifact;
using Content.Shared.Xenoarchaeology.Artifact.XAE;

namespace Content.Server._Scp.Research.Artifacts.Effects._ScpSpecific.Scp173.Blind;

public sealed partial class ArtifactScp173BlindEveryoneInRangeSystem : BaseXAESystem<ArtifactScp173BlindEveryoneInRangeComponent>
{
    [Dependency] private Scp173System _scp173 = default!;

    protected override void OnActivated(Entity<ArtifactScp173BlindEveryoneInRangeComponent> ent, ref XenoArtifactNodeActivatedEvent args)
    {
        _scp173.BlindEveryoneInRange(ent, ent.Comp.Time, false);
    }
}

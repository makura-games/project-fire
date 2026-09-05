using Content.Shared._Scp.Other.ScpBlind;
using Content.Shared.Xenoarchaeology.Artifact;
using Content.Shared.Xenoarchaeology.Artifact.XAE;

namespace Content.Server._Scp.Research.Artifacts.Effects._ScpSpecific.Scp173.Blind;

public sealed class ArtifactScp173BlindEveryoneInRangeSystem : BaseXAESystem<ArtifactScp173BlindEveryoneInRangeComponent>
{
    [Dependency] private readonly SharedScpBlindSystem _scpBlind = default!;

    protected override void OnActivated(Entity<ArtifactScp173BlindEveryoneInRangeComponent> ent, ref XenoArtifactNodeActivatedEvent args)
    {
        _scpBlind.BlindEveryoneInRange(ent, ent.Comp.Time, false);
    }
}

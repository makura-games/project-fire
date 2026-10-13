using Content.Server._Scp.Scp106.Systems;
using Content.Shared.Xenoarchaeology.Artifact;
using Content.Shared.Xenoarchaeology.Artifact.XAE;
using Robust.Shared.Random;

namespace Content.Server._Scp.Research.Artifacts.Effects._ScpSpecific.Scp106.Teleport;

public sealed partial class ArtifactScp106TeleportSystem : BaseXAESystem<ArtifactScp106TeleportComponent>
{
    [Dependency] private Scp106System _scp106 = default!;
    [Dependency] private IRobustRandom _random = default!;

    protected override void OnActivated(Entity<ArtifactScp106TeleportComponent> ent, ref XenoArtifactNodeActivatedEvent args)
    {
        switch (ent.Comp.Mode)
        {
            case ArtifactScp106TeleportMode.Random:
                if (_random.Prob(0.5f))
                    _scp106.SendToStation(ent);
                else
                    _ = _scp106.SendToBackrooms(ent);

                break;
            case ArtifactScp106TeleportMode.Station:
                _scp106.SendToStation(ent);

                break;

            case ArtifactScp106TeleportMode.Dimension:
                _ = _scp106.SendToBackrooms(ent);

                break;
        }
    }
}

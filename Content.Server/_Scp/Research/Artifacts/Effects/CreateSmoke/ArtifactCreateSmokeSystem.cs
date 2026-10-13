using Content.Server.Fluids.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.Xenoarchaeology.Artifact;
using Content.Shared.Xenoarchaeology.Artifact.XAE;

namespace Content.Server._Scp.Research.Artifacts.Effects.CreateSmoke;

public sealed partial class ArtifactCreateSmokeSystem : BaseXAESystem<ArtifactCreateSmokeComponent>
{
    [Dependency] private SmokeSystem _smokeSystem = default!;

    protected override void OnActivated(Entity<ArtifactCreateSmokeComponent> ent, ref XenoArtifactNodeActivatedEvent args)
    {
        var xform = Transform(ent);
        var smokeEntity = Spawn(ent.Comp.Prototype, xform.Coordinates.SnapToGrid());

        var solution = new Solution(ent.Comp.Reagent, ent.Comp.Quantity);
        _smokeSystem.StartSmoke(smokeEntity, solution, ent.Comp.Duration, ent.Comp.SpreadRadius);
    }
}

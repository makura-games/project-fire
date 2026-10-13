using System.Linq;
using Content.Server._Scp.Scp096;
using Content.Server._Sunrise.Helpers;
using Content.Shared._Scp.Blinking;
using Content.Shared._Scp.Scp096.Main.Components;
using Content.Shared._Scp.ScpMask;
using Content.Shared._Sunrise.Helpers;
using Content.Shared.Xenoarchaeology.Artifact;
using Content.Shared.Xenoarchaeology.Artifact.XAE;
using Robust.Shared.Random;

namespace Content.Server._Scp.Research.Artifacts.Effects._ScpSpecific.Scp096.Madness;

public sealed partial class ArtifactScp096MadnessSystem : BaseXAESystem<ArtifactScp096MadnessComponent>
{
    [Dependency] private ScpMaskSystem _scpMask = default!;
    [Dependency] private Scp096System _scp096 = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SunriseHelpersSystem _helpers = default!;
    [Dependency] private IRobustRandom _random = default!;

    protected override void OnActivated(Entity<ArtifactScp096MadnessComponent> ent, ref XenoArtifactNodeActivatedEvent args)
    {
        if (!_helpers.TryGetFirst<Scp096Component>(out var scp096))
            return;

        var targets = _lookup.GetEntitiesInRange<BlinkableComponent>(Transform(scp096.Value).Coordinates, ent.Comp.Radius, LookupFlags.Dynamic)
            .ToList()
            .ShuffleRobust(_random)
            .TakePercentage(ent.Comp.Percent);

        foreach (var target in targets)
        {
            if (!_scp096.TryAddTarget(scp096.Value, target, true, true))
                continue;

            // TODO: Пофиксить разрыв маски много раз
            _scpMask.TryTear(scp096.Value);
        }

        // TODO: Звук
    }
}

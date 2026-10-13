using System.Linq;
using Content.Server.Research.Systems;
using Content.Server.Xenoarchaeology.Artifact;
using Content.Shared._Scp.Helpers;
using Content.Shared.Popups;
using Content.Shared.Research;
using Content.Shared.Xenoarchaeology.Equipment;
using Content.Shared.Xenoarchaeology.Equipment.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server.Xenoarchaeology.Equipment;

/// <inheritdoc />
public sealed partial class ArtifactAnalyzerSystem : SharedArtifactAnalyzerSystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ResearchSystem _research = default!;
    [Dependency] private XenoArtifactSystem _xenoArtifact = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AnalysisConsoleComponent, AnalysisConsoleExtractButtonPressedMessage>(OnExtractButtonPressed);
    }

    private void OnExtractButtonPressed(Entity<AnalysisConsoleComponent> ent, ref AnalysisConsoleExtractButtonPressedMessage args)
    {
        if (!TryGetArtifactFromConsole(ent, out var artifact))
            return;

        if (!_research.TryGetClientServer(ent, out var server, out var serverComponent))
            return;

        // Fire edit start - добавил поддержку разных очков исследования
        Dictionary <ProtoId<ResearchPointPrototype>, int> sumResearch = new ();
        foreach (var node in _xenoArtifact.GetAllNodes(artifact.Value))
        {
            var research = _xenoArtifact.GetResearchValue(node);
            _xenoArtifact.SetConsumedResearchValue(node, node.Comp.ConsumedResearchValue + research);

            sumResearch.AddOrIncrement(node.Comp.ResearchPointType, research);
        }

        if (sumResearch.Keys.Count <= 0)
            return;

        _research.ModifyServerPoints(server.Value, sumResearch, false, serverComponent); // Sunrise-Edit
        // Fire edit end
        _audio.PlayPvs(ent.Comp.ExtractSound, artifact.Value);
        _popup.PopupEntity(Loc.GetString("analyzer-artifact-extract-popup"), artifact.Value, PopupType.Large);
    }
}

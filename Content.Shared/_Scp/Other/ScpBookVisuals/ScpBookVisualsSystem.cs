using Content.Shared.Paper;

namespace Content.Shared._Scp.Other.ScpBookVisuals;

public sealed partial class ScpBookVisualsSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpBookVisualsComponent, BoundUIOpenedEvent>(OnOpen);
        SubscribeLocalEvent<ScpBookVisualsComponent, BoundUIClosedEvent>(OnClose);
        SubscribeLocalEvent<ScpBookVisualsComponent, PaperWrittenEvent>(OnWritten);
    }

    private void OnOpen(Entity<ScpBookVisualsComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateOpenState(ent);
    }

    private void OnClose(Entity<ScpBookVisualsComponent> ent, ref BoundUIClosedEvent args)
    {
        _appearance.SetData(ent, ScpBookVisualLayers.ScpBookVisualState, ScpBookVisualState.Closed);
    }

    private void OnWritten(Entity<ScpBookVisualsComponent> ent, ref PaperWrittenEvent args)
    {
        UpdateOpenState(ent);
    }

    private void UpdateOpenState(EntityUid ent)
    {
        if (!_ui.IsUiOpen(ent, PaperComponent.PaperUiKey.Key))
            return;

        var state = TryComp<PaperComponent>(ent, out var paper) && !string.IsNullOrEmpty(paper.Content)
            ? ScpBookVisualState.OpenWritten
            : ScpBookVisualState.Open;

        _appearance.SetData(ent, ScpBookVisualLayers.ScpBookVisualState, state);
    }
}

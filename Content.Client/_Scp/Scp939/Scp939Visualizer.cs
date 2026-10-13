using Content.Shared._Scp.Scp939;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Client.GameObjects;

namespace Content.Client._Scp.Scp939;

public sealed partial class Scp939Visualizer : VisualizerSystem<Scp939Component>
{
    [Dependency] private SharedAppearanceSystem _appearanceSystem = default!;

    protected override void OnAppearanceChange(EntityUid uid, Scp939Component component, ref AppearanceChangeEvent args)
    {
        base.OnAppearanceChange(uid, component, ref args);

        UpdateSprite(uid, args.Component, args.Sprite);
    }

    private void UpdateSprite(EntityUid uid, AppearanceComponent appearanceComponent, SpriteComponent? spriteComponent = null, MobStateComponent? mobStateComponent = null)
    {
        if (!Resolve(uid, ref spriteComponent) ||
            !Resolve(uid, ref mobStateComponent) ||
            !spriteComponent.LayerMapTryGet(Scp939Layers.Base, out var layerId))
        {
            return;
        }

        if (mobStateComponent.CurrentState is MobState.Dead or MobState.Critical)
        {
            spriteComponent.LayerSetState(layerId, "dead");
            return;
        }

        spriteComponent.LayerSetState(layerId, "alive");

        if (_appearanceSystem.TryGetData<bool>(uid, Scp939Visuals.Sleeping, out var sleeping) && sleeping)
        {
            spriteComponent.LayerSetState(layerId, "asleep");
        }
    }
}

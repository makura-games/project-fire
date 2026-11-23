/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.Throwing;
using Robust.Shared.Analyzers;

namespace Content.Shared._CE.ZLevels.Throwing;

/// <summary>
/// Keeps z-physics out of the way of a vanilla throw: while an entity is actually being
/// thrown, its horizontal flight distance/timing is fully governed by ThrowingSystem's
/// friction-based model, so z-physics gravity/ground-sync/BodyStatus-sync must not run
/// for it (that fight is what caused throws to land short or overshoot the cursor).
/// </summary>
public sealed partial class CEZLevelThrowingSystem : EntitySystem
{

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CEZPhysicsComponent, ThrownEvent>(OnThrown);
        SubscribeLocalEvent<CEZPhysicsComponent, StopThrowEvent>(OnStopThrow);
    }

    private void OnThrown(Entity<CEZPhysicsComponent> ent, ref ThrownEvent args)
    {
        ent.Comp.Disabled = true;
        DirtyField(ent, ent.Comp, nameof(CEZPhysicsComponent.Disabled));
    }

    private void OnStopThrow(Entity<CEZPhysicsComponent> ent, ref StopThrowEvent args)
    {
        ent.Comp.Disabled = false;
        DirtyField(ent, ent.Comp, nameof(CEZPhysicsComponent.Disabled));
    }
}

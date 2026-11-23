/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Events;

namespace Content.Shared._CE.ZLevels.Throwing;

/// <summary>
/// Keeps z-physics out of the way of a shot physical projectile (arrows, bolts).
/// The gun system launches projectiles with BodyStatus.InAir so tile friction doesn't apply,
/// but z-physics would sync BodyStatus back to OnGround (the projectile is at ground height),
/// making it slide to a stop after a few tiles. Z-physics is resumed once the projectile embeds.
/// </summary>
public sealed partial class CEZLevelProjectileSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CEZPhysicsComponent, ProjectileShotEvent>(OnShot);
        SubscribeLocalEvent<CEZPhysicsComponent, EmbedEvent>(OnEmbed);
    }

    private void OnShot(Entity<CEZPhysicsComponent> ent, ref ProjectileShotEvent args)
    {
        ent.Comp.Disabled = true;
        DirtyField(ent, ent.Comp, nameof(CEZPhysicsComponent.Disabled));
    }

    private void OnEmbed(Entity<CEZPhysicsComponent> ent, ref EmbedEvent args)
    {
        ent.Comp.Disabled = false;
        DirtyField(ent, ent.Comp, nameof(CEZPhysicsComponent.Disabled));
    }
}

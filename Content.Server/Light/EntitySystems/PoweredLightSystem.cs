using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;

namespace Content.Server.Light.EntitySystems;

/// <inheritdoc/>
public sealed partial class PoweredLightSystem : SharedPoweredLightSystem
{
    [SubscribeLocalEvent]
    private void OnMapInit(Entity<PoweredLightComponent> ent, ref MapInitEvent args)
    {
        // TODO: Use ContainerFill dog
        if (ent.Comp.HasLampOnSpawn != null)
        {
            // Fire edit start - для того, чтобы при переносе сломанных лампочек из раунда в раунд стандартные лампочкине валялась на полу
            var entity = Spawn(ent.Comp.HasLampOnSpawn, Transform(ent).Coordinates);
            var canInsert = ContainerSystem.Insert(entity, ent.Comp.LightBulbContainer);

            if (!canInsert)
            {
                QueueDel(entity);
                return;
            }
            // Fire edit end
        }
        // need this to update visualizers
        UpdateLight(ent, ent.Comp);
    }
}

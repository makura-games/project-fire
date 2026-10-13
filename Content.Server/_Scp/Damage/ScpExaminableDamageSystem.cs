using Content.Server.Destructible;
using Content.Shared._Scp.Damage.ExaminableDamage;
using Content.Shared.Examine;

namespace Content.Server._Scp.Damage;

public sealed partial class ScpExaminableDamageSystem : SharedScpExaminableDamageSystem
{
    [Dependency] private DestructibleSystem _destructible = default!;

    protected override void StructureExamine(Entity<ScpExaminableDamageComponent> ent, EntityUid target, ref ExaminedEvent args)
    {
        base.StructureExamine(ent, target, ref args);

        CreateMessage(ent, target, _destructible.DestroyedAt(target), ref args);
    }
}

using Content.Server._Scp.ZLevels;

namespace Content.Server.Projectiles;

public sealed partial class ProjectileSystem
{
    // Ограничение урона снарядов, выпущенных на соседний этаж.
    [Dependency] private ZLevelProjectileSystem _zLevelProjectiles = default!;
}

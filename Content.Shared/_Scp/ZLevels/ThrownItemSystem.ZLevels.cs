using Content.Shared._CE.ZLevels.Core.EntitySystems;

namespace Content.Shared.Throwing;

public sealed partial class ThrownItemSystem
{
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;
}

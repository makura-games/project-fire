using Content.Shared._CE.ZLevels.Core.EntitySystems;

namespace Content.Client.Parallax;

public sealed partial class ParallaxOverlay
{
    private CESharedZLevelsSystem? _cachedZLevels;
    private CESharedZLevelsSystem _zLevel => _cachedZLevels ??= _entManager.System<CESharedZLevelsSystem>();
}

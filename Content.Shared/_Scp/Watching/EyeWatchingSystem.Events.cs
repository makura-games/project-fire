using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._Scp.Watching;

public sealed partial class EyeWatchingSystem
{
    [Dependency] private IConfigurationManager _cfg = default!;

    private void InitializeEvents()
    {
        SubscribeLocalEvent<WatchingTargetComponent, MapInitEvent>(OnMapInit);

        _cfg.OnValueChanged(CVars.NetMaxUpdateRange, OnPvsRageChanged, true);
    }

    private void ShutdownEvents()
    {
        _cfg.UnsubValueChanged(CVars.NetMaxUpdateRange, OnPvsRageChanged);
    }

    private void OnMapInit(Entity<WatchingTargetComponent> ent, ref MapInitEvent args)
    {
        SetNextTime(ent);
    }

    private void OnPvsRageChanged(float newRange)
    {
        // Потому что игрок в середине экрана, а SeeRange работает как радиус.
        SeeRange = newRange / 2f;
    }
}

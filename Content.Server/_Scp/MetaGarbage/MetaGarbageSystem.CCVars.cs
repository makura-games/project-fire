using Content.Shared._Scp.ScpCCVars;
using Robust.Shared.Configuration;

namespace Content.Server._Scp.MetaGarbage;

public sealed partial class MetaGarbageSystem
{
    [Dependency] private IConfigurationManager _cfg = default!;

    private ConfigurationMultiSubscriptionBuilder _configSub = default!;

    private bool _enableSaving;
    private bool _enableSpawning;
    private bool _enableSpawningWithoutRule;

    private void InitializeCCVars()
    {
        _configSub = _cfg.SubscribeMultiple()
            .OnValueChanged(ScpCCVars.MetaGarbageEnableSaving, x => _enableSaving = x, true)
            .OnValueChanged(ScpCCVars.MetaGarbageEnableSpawning, x => _enableSpawning = x, true)
            .OnValueChanged(ScpCCVars.MetaGarbageEnableSpawningWithoutRule, x => _enableSpawningWithoutRule = x, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _configSub.Dispose();
    }
}

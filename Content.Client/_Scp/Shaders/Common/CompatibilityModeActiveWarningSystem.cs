using Content.Client._Scp.Shaders.Common.Grain;
using Content.Client._Scp.Shaders.FieldOfView;
using Content.Client._Scp.UI;
using Content.Shared._Scp.ScpCCVars;
using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Client._Scp.Shaders.Common;

/// <summary>
/// Система, отвечающая за оповещение игрока о включенном режиме совместимости.
/// Также выключает все шейдеры, которые ведут себя неправильно при включенном режиме совместимости, пока игрок не выключит режим.
/// </summary>
public sealed partial class CompatibilityModeActiveWarningSystem : EntitySystem
{
    [Dependency] private GrainOverlaySystem _grain = default!;
    [Dependency] private FieldOfViewOverlayManagementSystem _fovManagement = default!;
    [Dependency] private IConfigurationManager _configuration = default!;

    /// <summary>
    /// Окно с предупреждением о режиме совместимости.
    /// </summary>
    private CompatibilityModeActiveWarningWindow? _window;

    /// <summary>
    /// Включен ли режим совместимости у текущего локального игрока?
    /// </summary>
    public bool IsCompatibilityModeEnabled { get; private set; }

    /// <summary>
    /// Будут ли использоваться шейдеры при включенном режиме совместимости?
    /// </summary>
    public bool CompabilityUseShaders { get; private set; }

    /// <summary>
    /// Должен ли клиент использовать шейдеры?
    /// </summary>
    public bool ShouldUseShaders => !IsCompatibilityModeEnabled || CompabilityUseShaders;

    public override void Initialize()
    {
        base.Initialize();

        CheckCompatibilityMode();
        _configuration.OnValueChanged(ScpCCVars.CompatibilityModeUseShaders, ToggleShaders);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _configuration.UnsubValueChanged(ScpCCVars.CompatibilityModeUseShaders, ToggleShaders);
    }

    private void CheckCompatibilityMode()
    {
        IsCompatibilityModeEnabled = _configuration.GetCVar(CVars.DisplayCompat);

        if (!IsCompatibilityModeEnabled)
            return;

        var showWarning = _configuration.GetCVar(ScpCCVars.CompatibilityModeShowWarning);
        var useShaders = _configuration.GetCVar(ScpCCVars.CompatibilityModeUseShaders);

        if (showWarning)
            ShowWindow();

        if (!useShaders)
            ToggleShaders(false);
    }

    private void ShowWindow()
    {
        if (_window != null)
            return;

        _window = new CompatibilityModeActiveWarningWindow();
        _window.OnClose += () => _window = null;
        _window.OpenCentered();
    }

    private void ToggleShaders(bool value)
    {
        CompabilityUseShaders = value;

        if (!IsCompatibilityModeEnabled)
            return;

        _grain.Enabled = value;
        _grain.ToggleOverlay(value);

        if (_fovManagement.OverlaysPresented && value)
            _fovManagement.AddConeOverlay();
        else if (!value)
            _fovManagement.RemoveConeOverlay();
    }
}

using Content.Shared._Scp.Proximity;
using Content.Shared._Scp.ScpCCVars;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Client._Scp.Shaders.ConeLighting;

/// <summary>
/// Система, управляющая эффектом свечения конусов.
/// Высчитывает, какие сущности будут иметь эффект и передает в оверлей.
/// </summary>
public sealed partial class ConeLightingOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private ProximitySystem _proximity = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private EntityQuery<EyeComponent> _eyeQuery;
    private EntityQuery<TransformComponent> _transformQuery;

    private ConeLightingOverlay _cone = default!;

    private static readonly ProtoId<ShaderPrototype> Shader = "LightingOverlay";

    private bool _optimizationsEnabled;
    private float _coneOpacity;
    private float _strength;

    private ConfigurationMultiSubscriptionBuilder _configSub = default!;

    public override void Initialize()
    {
        base.Initialize();

        _cone = new ConeLightingOverlay(_prototypeManager, _sprite, Shader);

        _transformQuery = GetEntityQuery<TransformComponent>();
        _eyeQuery = GetEntityQuery<EyeComponent>();

        _configSub = _cfg.SubscribeMultiple()
            .OnValueChanged(ScpCCVars.LightBloomConeEnable, OnConeEnabledChanged, true)
            .OnValueChanged(ScpCCVars.LightBloomConeOpacity, x => _coneOpacity = x, true)
            .OnValueChanged(ScpCCVars.LightBloomConeOptimizations, b => _optimizationsEnabled = b, true)
            .OnValueChanged(ScpCCVars.LightBloomConeStrength, OnStrengthChanged, true);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_player.LocalEntity == null)
            return;

        _cone.Entities.Clear();

        var drawFov = _eyeQuery.TryComp(_player.LocalEntity.Value, out var eye) && eye.DrawFov;

        // Просчитываем, какие сущности будут иметь эффект свечения и будет ли это видно игроку.
        // Если сущность проходит проверки -> добавляем ее в список и отправляем список в оверлеи.
        var query = AllEntityQuery<ConeLightingOverlayVisualsComponent, PointLightComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var pointLight, out var xform))
        {
            if (!pointLight.Enabled)
                continue;

            // Оптимизации слабых видеокарт. Если источник не виден игроку -> не добавляем в список рендера.
            // Опционально, так как приводит к резкому падению FPS из-за сложности проверок на видимость.
            if (_optimizationsEnabled
                && drawFov
                && !_proximity.IsRightType(_player.LocalEntity.Value, uid, LineOfSightBlockerLevel.Transparent, out _))
                continue;

            var (worldPos, _, worldMatrix) = _transform.GetWorldPositionRotationMatrix(xform, _transformQuery);

            var color = pointLight.Color;
            _cone.Entities.Add((xform, worldMatrix, worldPos, color.WithAlpha(color.A * _coneOpacity)));
        }
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlayManager.RemoveOverlay(_cone);
        _cone.Dispose();

        _configSub.Dispose();
    }

    private void OnConeEnabledChanged(bool value)
    {
        _cone.Enabled = value;

        ToggleOverlay(_cone.Enabled, _cone);
    }

    private void OnStrengthChanged(float value)
    {
        _strength = Math.Clamp(value, 0.1f, 1f);

        _cone.Strength = _strength;
    }

    private void ToggleOverlay(bool value, Overlay overlay)
    {
        var hasOverlay = _overlayManager.HasOverlay(overlay.GetType());

        if (value && !hasOverlay)
            _overlayManager.AddOverlay(overlay);
        else if (!value && hasOverlay)
            _overlayManager.RemoveOverlay(overlay);
    }
}

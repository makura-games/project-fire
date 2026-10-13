using Content.Client.Overlays;
using Content.Client.SSDIndicator;
using Content.Client.Stealth;
using Content.Shared._Scp.Scp939;
using Content.Shared._Scp.Scp939.Protection;
using Content.Shared.Examine;
using Content.Shared.Inventory.Events;
using Content.Shared.Movement.Components;
using Content.Shared.StatusIcon.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Physics.Components;

namespace Content.Client._Scp.Scp939;

public sealed partial class Scp939HudSystem : EquipmentHudSystem<Scp939Component>
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    internal readonly List<(Entity<SpriteComponent> Ent, float BaseAlpha)> CachedBaseAlphas = new(64);

    private Scp939SetAlphaOverlay _setAlphaOverlay = default!;
    private Scp939ResetAlphaOverlay _resetAlphaOverlay = default!;

    // TODO: Выделить значения плохого зрения в отдельный компонент, не связанный с 939
    private Scp939Component? _scp939Component;

    private EntityQuery<EyeComponent> _eyeQuery;
    private EntityQuery<Scp939ProtectionComponent> _scp939ProtectionQuery;
    private EntityQuery<MovementSpeedModifierComponent> _movementSpeedQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;

    private bool _overlaysPresented;
    private float _lastUpdateTime;

    private const float UpdateInterval = 0.05f;

    public override void Initialize()
    {
        base.Initialize();

        InitializeVisibility();
        InitializeOverlay();

        SubscribeLocalEvent<ActiveScp939VisibilityComponent, GetStatusIconsEvent>(OnGetStatusIcons, after: [typeof(SSDIndicatorSystem)] );
        SubscribeLocalEvent<ActiveScp939VisibilityComponent, ExamineAttemptEvent>(OnExamine);

        _eyeQuery = GetEntityQuery<EyeComponent>();
        _scp939ProtectionQuery = GetEntityQuery<Scp939ProtectionComponent>();
        _movementSpeedQuery = GetEntityQuery<MovementSpeedModifierComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();

        _setAlphaOverlay = new();
        _resetAlphaOverlay = new();

        UpdatesAfter.Add(typeof(StealthSystem));
    }

    public override void Shutdown()
    {
        RestoreCachedBaseAlphas();
        RemoveOverlays();

        _setAlphaOverlay.Dispose();
        _resetAlphaOverlay.Dispose();

        base.Shutdown();
    }

    private void OnExamine(Entity<ActiveScp939VisibilityComponent> ent, ref ExamineAttemptEvent args)
    {
        if (!IsActive)
            return;

        var visibility = GetVisibility(ent);

        if (visibility < 0.2f)
            args.Cancel();
    }

    private void OnGetStatusIcons(Entity<ActiveScp939VisibilityComponent> ent, ref GetStatusIconsEvent args)
    {
        if (!IsActive)
            return;

        var visibility = GetVisibility(ent);

        if (visibility <= 0.5f)
            args.StatusIcons.Clear();
    }

    protected override void UpdateInternal(RefreshEquipmentHudEvent<Scp939Component> args)
    {
        base.UpdateInternal(args);

        _scp939Component = args.Components.Count > 0 ? args.Components[0] : null;
        AddOverlays();
    }

    protected override void DeactivateInternal()
    {
        base.DeactivateInternal();

        _scp939Component = null;
        _lastUpdateTime = 0f;

        RestoreCachedBaseAlphas();
        RemoveOverlays();
    }

    internal bool CanDraw(in OverlayDrawArgs args)
    {
        if (!IsActive)
            return false;

        if (_playerManager.LocalEntity is not { } player)
            return false;

        if (!_eyeQuery.TryComp(player, out var eye))
            return false;

        return args.Viewport.Eye == eye.Eye;
    }

    internal void RestoreCachedBaseAlphas()
    {
        foreach (var (ent, baseAlpha) in CachedBaseAlphas)
        {
            if (!_entityManager.EntityExists(ent))
                continue;

            _sprite.SetColor(ent.AsNullable(), ent.Comp.Color.WithAlpha(baseAlpha));
        }

        CachedBaseAlphas.Clear();
    }

    internal static float GetVisibility(Entity<ActiveScp939VisibilityComponent> ent)
    {
        var acc = ent.Comp.VisibilityAcc;

        if (acc > ent.Comp.HideTime)
            return 0;

        return Math.Clamp(1f - (acc / ent.Comp.HideTime), 0f, 1f);
    }
}

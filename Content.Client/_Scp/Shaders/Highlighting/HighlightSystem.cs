using Content.Shared._Scp.Shaders.Highlighting;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._Scp.Shaders.Highlighting;

/// <summary>
/// Система, регулирующая добавление шейдера подсвечивания.
/// </summary>
public sealed partial class HighlightSystem : SharedHighlightSystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private ISharedPlayerManager _player = default!;

    private static readonly ProtoId<ShaderPrototype> ShaderProtoId = "HighlightWave";

    /// <summary>
    /// Шейдер подсвечивания.
    /// Будет накладываться на текстуры используя внутренние методы спрайта.
    /// </summary>
    private ShaderInstance _shader = default!;

    public override void Initialize()
    {
        base.Initialize();

        _shader = _prototype.Index(ShaderProtoId).InstanceUnique();

        // Серверная отметка может прийти без отдельного события начала подсветки.
        SubscribeLocalEvent<HighlightedComponent, ComponentStartup>(OnHighlightStartup);
        SubscribeLocalEvent<HighlightedComponent, AfterAutoHandleStateEvent>(OnHighlightState);
        SubscribeLocalEvent<HighlightedComponent, ComponentShutdown>(OnHighlightShutdown);
        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);
        EntityManager.ComponentAdded += OnComponentAdded;

        SubscribeLocalEvent<HighlightedComponent, HighLightStartEvent>(OnHighlightStarted);
        SubscribeLocalEvent<SpriteComponent, HighLightEndEvent>(OnHighlightEnded);

        SubscribeNetworkEvent<HighLightStartEvent>(OnNetworkHighlightStarted);
        SubscribeNetworkEvent<HighLightEndEvent>(OnNetworkHighlightEnded);
    }

    public override void Shutdown()
    {
        EntityManager.ComponentAdded -= OnComponentAdded;
        base.Shutdown();

        _shader.Dispose();
    }

    private void OnHighlightStartup(Entity<HighlightedComponent> ent, ref ComponentStartup args)
    {
        StartHighlight(ent.AsNullable());
    }

    private void OnHighlightState(Entity<HighlightedComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        StartHighlight(ent.AsNullable());
    }

    private void OnHighlightShutdown(Entity<HighlightedComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<SpriteComponent>(ent, out var sprite))
            EndHighlight((ent, sprite));
    }

    private void OnComponentAdded(AddedComponentEventArgs args)
    {
        if (args.BaseArgs.Component is SpriteComponent &&
            TryComp<HighlightedComponent>(args.BaseArgs.Owner, out var highlight))
            StartHighlight((args.BaseArgs.Owner, highlight));
    }

    private void OnHighlightStarted(Entity<HighlightedComponent> ent, ref HighLightStartEvent args)
    {
        StartHighlight(ent.AsNullable());
    }

    private void OnHighlightEnded(Entity<SpriteComponent> ent, ref HighLightEndEvent args)
    {
        EndHighlight(ent.AsNullable());
    }

    private void OnNetworkHighlightStarted(HighLightStartEvent args)
    {
        var uid = GetEntity(args.Entity);
        if (!uid.HasValue)
            return;

        StartHighlight(uid.Value);
    }

    private void OnNetworkHighlightEnded(HighLightEndEvent args)
    {
        var uid = GetEntity(args.Entity);
        if (!uid.HasValue)
            return;

        EndHighlight(uid.Value);
    }

    private void OnPlayerAttached(LocalPlayerAttachedEvent args)
    {
        UpdateHighlights();
    }

    private void OnPlayerDetached(LocalPlayerDetachedEvent args)
    {
        UpdateHighlights();
    }

    private void UpdateHighlights()
    {
        // Состояния сущностей приходят раньше прикрепления локального игрока к получателю.
        var query = AllEntityQuery<HighlightedComponent>();
        while (query.MoveNext(out var uid, out var highlight))
            StartHighlight((uid, highlight));
    }

    private void StartHighlight(Entity<HighlightedComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (ent.Comp.Recipient.HasValue && _player.LocalEntity != ent.Comp.Recipient)
        {
            EndHighlight((ent, sprite));
            return;
        }

        if (sprite.PostShader != _shader)
            sprite.PostShader = _shader;
    }

    private void EndHighlight(Entity<SpriteComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        if (ent.Comp.PostShader != _shader)
            return;

        ent.Comp.PostShader = null;
    }
}

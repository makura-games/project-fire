using System.Linq;
using Content.Shared._Scp.Scp106.Components;
using Content.Shared.DoAfter;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._Scp.Scp106.Containment;

public abstract partial class SharedScp106ContainmentSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Scp106Component, Scp106RecontainmentAttemptEvent>(OnRecontainment);

        SubscribeLocalEvent<Scp106ContainmentCatwalkComponent, StartCollideEvent>(OnContainmentCatwalkCollideStart);
        SubscribeLocalEvent<Scp106ContainmentCatwalkComponent, EndCollideEvent>(OnContainmentCatwalkCollideEnd);
    }

    /// <summary>
    /// Отвечает за логику после начала реконтейма.
    /// Отключает все текущие дуафтеры, чтобы оборвать текущие способности.
    /// <remarks>
    /// Логика телепорта находится в методе <see cref="TryContain"/>
    /// </remarks>
    /// </summary>
    private void OnRecontainment(Entity<Scp106Component> ent, ref Scp106RecontainmentAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (!TryComp<DoAfterComponent>(ent, out var doAfter))
            return;

        foreach (var doAfterId in doAfter.DoAfters.Values)
        {
            _doAfter.Cancel(doAfterId.Id);
        }
    }

    #region Containment chamber stuff

    private void OnContainmentCatwalkCollideStart(Entity<Scp106ContainmentCatwalkComponent> ent, ref StartCollideEvent args)
    {
        if (!TryComp<Scp106Component>(args.OtherEntity, out var scp106Component))
            return;

        var mapUid = Transform(args.OtherEntity).MapUid;

        if (HasComp<Scp106DimensionShiftingMapComponent>(mapUid))
            return;

        scp106Component.IsContained = true;
        Dirty(ent, scp106Component);

        if (!TryComp<FixturesComponent>(args.OtherEntity, out var fixturesComponent))
            return;

        foreach (var (id, fixture) in fixturesComponent.Fixtures)
        {
            _physics.SetCollisionMask(args.OtherEntity, id, fixture, 30);
        }
    }

    private void OnContainmentCatwalkCollideEnd(Entity<Scp106ContainmentCatwalkComponent> ent, ref EndCollideEvent args)
    {
        if (!TryComp<Scp106Component>(args.OtherEntity, out var scp106Component))
            return;

        // Проверка, стоит ли SCP-106 всё ещё на другом контейнере
        var isStillOnContainer = _lookup.GetEntitiesInRange(Transform(args.OtherEntity).Coordinates, 0.1f)
            .Any(HasComp<Scp106ContainmentCatwalkComponent>);

        if (isStillOnContainer)
            return;

        // Если он не на другом контейнере, сбрасываем состояние
        scp106Component.IsContained = false;
        Dirty(args.OtherEntity, scp106Component);

        if (!TryComp<FixturesComponent>(args.OtherEntity, out var fixturesComponent))
            return;

        foreach (var (id, fixture) in fixturesComponent.Fixtures)
        {
            _physics.SetCollisionMask(args.OtherEntity, id, fixture, 18);
        }
    }

    #endregion
}

/// <summary>
/// Ивент, являющийся последней частью проверки на возможность реконтейма SCP-106.
/// Позволяет отменить реконтейм извне системы реконтейма или подписаться на событие реконтейма.
/// <remarks>
/// Для подписки на событие реконтейма достаточно просто сделать проверку, что ивент не был отменен
/// </remarks>
/// </summary>
public sealed class Scp106RecontainmentAttemptEvent : CancellableEntityEventArgs;

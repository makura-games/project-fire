using System.Diagnostics.CodeAnalysis;
using Content.Shared._Scp.Watching;
using Content.Shared.ActionBlocker;
using Content.Shared.Interaction.Events;
using Content.Shared.Movement.Events;
using Content.Shared.Storage.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;

namespace Content.Shared._Scp.Other.ScpRestrictMovementOnVisibility;

public sealed class SharedScpRestrictMovementOnVisibilitySystem : EntitySystem
{
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly EyeWatchingSystem _watching = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;

    private EntityQuery<InsideEntityStorageComponent> _insideQuery;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpRestrictMovementOnVisibilityComponent, AttackAttemptEvent>(OnAttackAttempt);

        SubscribeLocalEvent<ScpRestrictMovementOnVisibilityComponent, ChangeDirectionAttemptEvent>(OnDirectionAttempt);
        SubscribeLocalEvent<ScpRestrictMovementOnVisibilityComponent, UpdateCanMoveEvent>(OnMoveAttempt);
        SubscribeLocalEvent<ScpRestrictMovementOnVisibilityComponent, MoveInputEvent>(OnMoveInput);
        SubscribeLocalEvent<ScpRestrictMovementOnVisibilityComponent, MoveEvent>(OnMove);

        _insideQuery = GetEntityQuery<InsideEntityStorageComponent>();
    }

    public void OnAttackAttempt(Entity<ScpRestrictMovementOnVisibilityComponent> ent, ref AttackAttemptEvent args)
    {
        if (IsInContainer(ent, out _))
        {
            args.Cancel();
            return;
        }

        if (_watching.IsWatchedByAny(ent, useTimeCompensation: true))
        {
            args.Cancel();
            return;
        }
    }

    public void OnDirectionAttempt(Entity<ScpRestrictMovementOnVisibilityComponent> ent, ref ChangeDirectionAttemptEvent args)
    {
        // В контейнере можно двигаться
        if (IsInContainer(ent, out _))
            return;

        if (!_watching.IsWatchedByAny(ent, useTimeCompensation: true))
            return;

        args.Cancel();
    }

    public void OnMoveAttempt(Entity<ScpRestrictMovementOnVisibilityComponent> ent, ref UpdateCanMoveEvent args)
    {
        // В контейнере можно двигаться
        if (IsInContainer(ent, out _))
            return;

        if (!_watching.IsWatchedByAny(ent, useTimeCompensation: true))
            return;

        args.Cancel();
    }

    public void OnMoveInput(Entity<ScpRestrictMovementOnVisibilityComponent> ent, ref MoveInputEvent args)
    {
        // Метод подвязанный на MoveInputEvent так же нужен, вместе с методом на MoveEvent
        // Этот метод исправляет проблему, когда сущность должен мочь двинуться, но ему об этом никто не сказал
        // То есть последний вопрос от сущности МОГУ ЛИ Я ДВИНУТЬСЯ был когда он еще мог двинуться, через MoveEvent
        // Потом он перестал мочь, и следственно больше НЕ МОЖЕТ задать вопрос, может они двинуться
        // Это фикслось в игре сменой направления спрайта мышкой
        // Но данный метод как раз будет спрашивать у сущности, может ли он сдвинуться, когда как раз не двигается
        _blocker.UpdateCanMove(ent);
    }

    public void OnMove(Entity<ScpRestrictMovementOnVisibilityComponent> ent, ref MoveEvent args)
    {
        _blocker.UpdateCanMove(ent);
    }

    public bool IsInContainer(Entity<ScpRestrictMovementOnVisibilityComponent> ent, [NotNullWhen(true)] out EntityUid? storage)
    {
        storage = null;

        if (!_insideQuery.TryComp(ent, out var insideEntityStorageComponent))
            return false;

        if (!_containerSystem.TryGetContainingContainer(ent.Owner, out var container))
            return false;

        if (!_whitelist.CheckBoth(container.Owner, ent.Comp.ContainersMoveBlacklist, ent.Comp.ContainersMoveWhitelist))
            return false;

        storage = insideEntityStorageComponent.Storage;
        return true;
    }
}

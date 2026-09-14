
using System.Diagnostics.CodeAnalysis;
using Content.Shared._Scp.Blinking;
using Content.Shared._Scp.Helpers;
using Content.Shared._Scp.Proximity;
using Content.Shared._Scp.Watching;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Popups;
using Content.Shared.Storage.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;

namespace Content.Shared._Scp.Other.ScpBlind;

public sealed class SharedScpBlindSystem : EntitySystem
{
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly EyeWatchingSystem _watching = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedActionsSystem _actionsSystem = default!;
    [Dependency] private readonly SharedBlinkingSystem _blinking = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    private EntityQuery<InsideEntityStorageComponent> _insideQuery;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpBlindComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<ScpBlindComponent, ComponentShutdown>(OnShutdown);

        SubscribeLocalEvent<ScpBlindComponent, ScpBlindActionEvent>(OnStartedBlind);
        SubscribeLocalEvent<ScpBlindComponent, ScpActionStartBlind>(OnBlind);

        _insideQuery = GetEntityQuery<InsideEntityStorageComponent>();
    }

    private void OnInit(Entity<ScpBlindComponent> ent, ref ComponentInit args)
    {
        var actionEnt = _actionsSystem.AddAction(ent, ent.Comp.ActionProto);
        ent.Comp.ActionEnt = actionEnt;
        Dirty(ent);
    }

    private void OnShutdown(Entity<ScpBlindComponent> ent, ref ComponentShutdown args)
    {
        _actionsSystem.RemoveAction(ent.Owner, ent.Comp.ActionEnt);
        ent.Comp.ActionEnt = null;
        Dirty(ent);
    }

    public void OnStartedBlind(Entity<ScpBlindComponent> ent, ref ScpBlindActionEvent args)
    {
        if (args.Handled)
            return;

        if (!CanBlind(ent))
            return;

        var doAfterEventArgs = new DoAfterArgs(EntityManager, args.Performer, ent.Comp.StartBlindTime, new ScpActionStartBlind(), args.Performer)
        {
            Hidden = true,
            RequireCanInteract = false,
        };

        args.Handled = _doAfter.TryStartDoAfter(doAfterEventArgs);
    }

    public void OnBlind(Entity<ScpBlindComponent> ent, ref ScpActionStartBlind args)
    {
        if (args.Handled || args.Cancelled)
            return;

        if (!CanBlind(ent))
            return;

        // По причине акшена это не предиктится.
        // Активация акшена у игрока не предугадывается другими игроками. Параша
        BlindEveryoneInRange(ent, ent.Comp.BlindnessTime, false);
        args.Handled = true;
    }

    public void BlindEveryoneInRange(EntityUid scp, TimeSpan time, bool predicted = true)
    {
        using var blinkableList = ListPoolEntity<BlinkableComponent>.Rent();
        if (!_watching.TryGetAllEntitiesVisibleTo(scp, blinkableList.Value, flags: LookupFlags.Dynamic | LookupFlags.Approximate))
            return;

        foreach (var eye in blinkableList.Value)
        {
            _blinking.ForceBlind(eye.AsNullable(), time, predicted);
        }

        // TODO: Add sound.
    }

    private bool CanBlind(Entity<ScpBlindComponent> ent, bool showPopups = true)
    {
        if (ent.Comp.MustBeAllowedToBlind && !IsAllowed(ent, ent.Comp.SearchAllowerRadius))
        {
            if (showPopups)
                _popup.PopupClient(Loc.GetString("scp173-blind-failed-not-in-chamber"), ent, ent);

            return false;
        }

        if (IsBlocked(ent, ent.Comp.SearchBlockerRadius) && !ent.Comp.IgnoreBlockers)
        {
            if (showPopups)
                _popup.PopupClient(Loc.GetString("scp173-blind-failed-not-in-chamber"), ent, ent);

            return false;
        }

        if (IsInContainer(ent, out var container))
        {
            if (showPopups)
                _popup.PopupClient(Loc.GetString("scp-cage-suppress-ability", ("container", Name(container.Value))), ent, ent);

            return false;
        }

        if (ent.Comp.MinWatchersToBlind == null) // Пропускаем проверку количества смотрящих, если не указан минимум
            return true;

        if (!_watching.TryGetWatchers(ent, out var watchers))
        {
            if (showPopups)
                _popup.PopupClient(Loc.GetString("scp173-blind-failed-too-few-watchers"), ent, ent);

            return false;
        }

        if (watchers < ent.Comp.MinWatchersToBlind)
        {
            if (showPopups)
                _popup.PopupClient(Loc.GetString("scp173-blind-failed-too-few-watchers"), ent, ent);

            return false;
        }

        return true;
    }

    public bool IsInContainer(Entity<ScpBlindComponent> ent, [NotNullWhen(true)] out EntityUid? storage)
    {
        storage = null;

        if (!_insideQuery.TryComp(ent, out var insideEntityStorageComponent))
            return false;

        if (!_containerSystem.TryGetContainingContainer(ent.Owner, out var container))
            return false;

        if (!_whitelist.CheckBoth(container.Owner, ent.Comp.InContainersBlindBlacklist, ent.Comp.InContainersBlindWhitelist))
            return false;

        storage = insideEntityStorageComponent.Storage;
        return true;
    }

    public bool IsAllowed(EntityUid uid, float radius)
    {
        return _watching.TryGetAnyEntitiesVisibleTo<ScpBlindAllowerComponent>(uid,
            LineOfSightBlockerLevel.None,
            LookupFlags.Sensors | LookupFlags.Sundries,
            radius
        );
    }

    public bool IsBlocked(EntityUid uid, float radius)
    {
        return _watching.TryGetAnyEntitiesVisibleTo<ScpBlindBlockerComponent>(uid,
            LineOfSightBlockerLevel.None,
            LookupFlags.Sensors | LookupFlags.Sundries,
            radius);
    }
}

using Content.Server.Nuke;
using Content.Shared._Scp.NukeCodeConsole;
using Content.Shared.Audio;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Interaction;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Scp.NukeCodeConsole;

public sealed partial class NukeCodeConsoleSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private NukeSystem _nuke = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NukeCodeConsoleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NukeCodeConsoleComponent, EntInsertedIntoContainerMessage>(OnKeyInserted);
        SubscribeLocalEvent<NukeCodeConsoleComponent, EntRemovedFromContainerMessage>(OnKeyRemoved);

        SubscribeLocalEvent<NukeCodeConsoleComponent, NukeCodeConsoleActivateButtonMessage>(OnActivateButtonPressed);
        SubscribeLocalEvent<NukeCodeConsoleComponent, NukeCodeConsoleLockButtonMessage>(OnLockButtonPressed);

        SubscribeLocalEvent<NukeCodeConsoleComponent, NukeCodeConsoleKeypadMessage>(OnKeypadButtonPressed);
        SubscribeLocalEvent<NukeCodeConsoleComponent, NukeCodeConsoleKeypadClearMessage>(OnClearButtonPressed);
        SubscribeLocalEvent<NukeCodeConsoleComponent, NukeCodeConsoleKeypadEnterMessage>(OnEnterButtonPressed);

        SubscribeLocalEvent<NukeCodeConsoleComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<NukeCodeConsoleComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
    }

    private void OnMapInit(Entity<NukeCodeConsoleComponent> ent, ref MapInitEvent args)
    {
        _itemSlots.AddItemSlot(ent, NukeCodeConsoleComponent.KeySlotId, ent.Comp.KeySlot);

        if (string.IsNullOrWhiteSpace(ent.Comp.Code))
        {
            if (ent.Comp.CodeLength <= 0 || ent.Comp.CodeLength > 6)
                return;

            var nukeQuery = EntityQueryEnumerator<NukeComponent>();
            while (nukeQuery.MoveNext(out var uid, out var comp))
            {
                if (Transform(uid).GridUid == Transform(ent).GridUid)
                    ent.Comp.Code = comp.Code;
            }
        }

        UpdateUserInterface(ent);
    }

    private void OnKeyInserted(Entity<NukeCodeConsoleComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != NukeCodeConsoleComponent.KeySlotId)
            return;

        UpdateUserInterface(ent);
    }

    private void OnKeyRemoved(Entity<NukeCodeConsoleComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != NukeCodeConsoleComponent.KeySlotId)
            return;

        ent.Comp.IsLocked = true;
        ent.Comp.EnteredCode = string.Empty;
        UpdateUserInterface(ent);
    }

    private void OnActivateButtonPressed(Entity<NukeCodeConsoleComponent> ent, ref NukeCodeConsoleActivateButtonMessage args)
    {
        if (!HasKey(ent))
            return;

        _audio.PlayPvs(ent.Comp.KeypadPressSound, ent.Owner);

        if (ent.Comp.IsLocked)
            return;

        if (_timing.CurTime < ent.Comp.NextActivateButtonAvailable)
            return;

        var nukeQuery = EntityQueryEnumerator<NukeComponent>();
        while (nukeQuery.MoveNext(out var uid, out var comp))
        {
            if (Transform(uid).GridUid == Transform(ent).GridUid)
            {
                _nuke.SetRemainingTime(uid, ent.Comp.NukeRemainingTimeStart, comp);
                _nuke.ToggleBomb(uid, comp);

                ent.Comp.NextActivateButtonAvailable = _timing.CurTime + ent.Comp.ActivateButtonDelay;
            }
        }
    }

    private void OnLockButtonPressed(Entity<NukeCodeConsoleComponent> ent, ref NukeCodeConsoleLockButtonMessage args)
    {
        if (!HasKey(ent))
            return;

        if (ent.Comp.IsLocked)
            return;

        UpdateStatus(ent);
        UpdateUserInterface(ent);
    }

    private void OnKeypadButtonPressed(Entity<NukeCodeConsoleComponent> ent, ref NukeCodeConsoleKeypadMessage args)
    {
        if (!HasKey(ent))
            return;

        if (args.Value < 0 || args.Value > 9)
            return;

        PlayKeypadSound(ent, args.Value);

        if (!ent.Comp.IsLocked)
            return;

        if (ent.Comp.EnteredCode.Length >= ent.Comp.CodeLength)
            return;

        ent.Comp.EnteredCode += args.Value.ToString();
        UpdateUserInterface(ent);
    }

    private void OnClearButtonPressed(Entity<NukeCodeConsoleComponent> ent, ref NukeCodeConsoleKeypadClearMessage args)
    {
        if (!HasKey(ent))
            return;

        _audio.PlayPvs(ent.Comp.KeypadPressSound, ent.Owner);

        if (!ent.Comp.IsLocked)
            return;

        ent.Comp.EnteredCode = "";
        UpdateUserInterface(ent);
    }

    private void OnEnterButtonPressed(Entity<NukeCodeConsoleComponent> ent, ref NukeCodeConsoleKeypadEnterMessage args)
    {
        if (!HasKey(ent))
            return;

        if (!ent.Comp.IsLocked)
        {
            _audio.PlayPvs(ent.Comp.KeypadPressSound, ent.Owner);
            return;
        }

        UpdateStatus(ent);
        UpdateUserInterface(ent);
    }

    private void OnInteractUsing(Entity<NukeCodeConsoleComponent> ent, ref InteractUsingEvent args)
    {
        if (TryComp<NetworkConfiguratorComponent>(args.Used, out _) && ent.Comp.IsSealed)
        {
            args.Handled = true;
        }
    }

    private void OnGetVerbs(Entity<NukeCodeConsoleComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (ent.Comp.IsSealed)
            return;

        if (!args.CanInteract || !args.CanAccess)
            return;

        var verb = new AlternativeVerb()
        {
            Text = Loc.GetString("code-console-verb-seal-name"),
            Message = Loc.GetString("code-console-verb-seal-desc"),
            Icon = new SpriteSpecifier.Texture(
                new("/Textures/Interface/VerbIcons/lock.svg.192dpi.png")),
            Act = () => { ent.Comp.IsSealed = true; }
        };
        args.Verbs.Add(verb);
    }

    private void UpdateStatus(Entity<NukeCodeConsoleComponent> ent)
    {

        if (ent.Comp.IsLocked)
        {
            if (ent.Comp.EnteredCode == ent.Comp.Code)
            {
                ent.Comp.IsLocked = false;
                _audio.PlayPvs(ent.Comp.AccessGrantedSound, ent.Owner);
            }
            else
            {
                ent.Comp.EnteredCode = "";
                _audio.PlayPvs(ent.Comp.AccessDeniedSound, ent.Owner);
            }
        }
        else
        {
            ent.Comp.IsLocked = true;
            ent.Comp.EnteredCode = "";
            _audio.PlayPvs(ent.Comp.KeypadPressSound, ent.Owner);
        }
    }

    private void PlayKeypadSound(Entity<NukeCodeConsoleComponent> ent, int number)
    {
        // This is a C mixolydian blues scale.
        // 1 2 3    C D Eb
        // 4 5 6    E F F#
        // 7 8 9    G A Bb
        var semitoneShift = number switch
        {
            1 => 0,
            2 => 2,
            3 => 3,
            4 => 4,
            5 => 5,
            6 => 6,
            7 => 7,
            8 => 9,
            9 => 10,
            0 => 8,
            _ => 0
        };

        var opts = ent.Comp.KeypadPressSound.Params;
        opts = AudioHelpers.ShiftSemitone(opts, semitoneShift).AddVolume(-5f);
        _audio.PlayPvs(ent.Comp.KeypadPressSound, ent.Owner, opts);
    }

    private void UpdateUserInterface(Entity<NukeCodeConsoleComponent> ent)
    {
        if (!_ui.HasUi(ent.Owner, NukeCodeConsoleUiKey.Key))
            return;

        var state = new NukeCodeConsoleUiState
        {
            HasKey = HasKey(ent),
            IsLocked = ent.Comp.IsLocked,
            EnteredCodeLength = ent.Comp.EnteredCode.Length,
            MaxCodeLength = ent.Comp.CodeLength
        };

        _ui.SetUiState(ent.Owner, NukeCodeConsoleUiKey.Key, state);
    }

    private bool HasKey(Entity<NukeCodeConsoleComponent> ent)
    {
        return ent.Comp.KeySlot.Item is { } key && HasComp<NukeCodeConsoleKeyComponent>(key);
    }
}

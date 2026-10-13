using System.Linq;
using Content.Shared._Scp.Other.Events;
using Content.Shared.Audio;
using Content.Shared.Examine;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory.Events;
using Content.Shared.Popups;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared._Scp.Other.Radio;

public abstract partial class SharedScpRadioSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] protected IPrototypeManager PrototypeManager = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAmbientSoundSystem _ambientSound = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpRadioComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ScpRadioComponent, EncryptionChannelsChangedEvent>(OnEncryptionChannelsChanged);
        SubscribeLocalEvent<ScpRadioComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(AddVerbs);
        SubscribeLocalEvent<ScpRadioComponent, ExaminedEvent>(OnExamine);

        SubscribeLocalEvent<ScpRadioComponent, EntParentChangedMessage>(OnAmbienceChanged);
        SubscribeLocalEvent<ScpRadioComponent, GotEquippedEvent>(OnAmbienceChanged);
        SubscribeLocalEvent<ScpRadioComponent, GotUnequippedEvent>(OnAmbienceChanged);
        SubscribeLocalEvent<ScpRadioComponent, GotEquippedHandEvent>(OnAmbienceChanged);
        SubscribeLocalEvent<ScpRadioComponent, GotUnequippedHandEvent>(OnAmbienceChanged);
        SubscribeLocalEvent<ScpRadioComponent, EntityInsertedIntoStorageEvent>(OnAmbienceChanged);
        SubscribeLocalEvent<ScpRadioComponent, EntityRemovedFromStorageEvent>(OnAmbienceChanged);
    }

    protected virtual void OnStartup(Entity<ScpRadioComponent> ent, ref ComponentStartup args)
    {
        UpdateChannels(ent);
        UpdateAmbience(ent);
    }

    protected virtual void OnEncryptionChannelsChanged(Entity<ScpRadioComponent> ent, ref EncryptionChannelsChangedEvent args)
    {
        UpdateChannels(ent, args.Component);
    }

    private void OnActivate(Entity<ScpRadioComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        ToggleMicrophone(ent, args.User);

        args.Handled = true;
    }

    private void AddVerbs(GetVerbsEvent<Verb> ev)
    {
        if (!ev.CanInteract || !ev.CanComplexInteract)
            return;

        if (!TryComp<ScpRadioComponent>(ev.Target, out var scpRadio))
            return;

        AddCycleChannelVerb((ev.Target, scpRadio), ev);
        AddToggleRadioVerb((ev.Target, scpRadio), ev);
    }

    private void OnExamine(Entity<ScpRadioComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(ScpRadioComponent)))
        {
            if (ent.Comp.ActiveChannel is { } activeChannel
                && PrototypeManager.TryIndex(activeChannel, out var proto))
            {
                args.PushMarkup(Loc.GetString("handheld-radio-component-chennel-examine",
                    ("channel", proto.LocalizedName)));
            }

            args.PushMarkup(Loc.GetString("scp-radio-radio-status",
                ("value", ent.Comp.Enabled)));
            args.PushMarkup(Loc.GetString("scp-radio-microphone-status",
                ("value", ent.Comp.MicrophoneEnabled)));
        }
    }

    private void OnAmbienceChanged<T>(Entity<ScpRadioComponent> ent, ref T _)
    {
        UpdateAmbience(ent);
    }

    protected void UpdateAmbience(Entity<ScpRadioComponent> ent)
    {
        _ambientSound.SetAmbience(ent, ent.Comp.Enabled && ShouldPlayAmbience(ent));
    }

    protected void UpdateChannels(Entity<ScpRadioComponent> ent, EncryptionKeyHolderComponent? keyHolder = null)
    {
        if (!Resolve(ent.Owner, ref keyHolder, false))
        {
            ent.Comp.Channels.Clear();
            ent.Comp.ActiveChannel = null;
            DirtyFields(ent!, null, nameof(ScpRadioComponent.Channels), nameof(ScpRadioComponent.ActiveChannel));
            return;
        }

        ent.Comp.Channels = keyHolder.Channels
            .OrderBy(channel => PrototypeManager.Index(channel).Frequency)
            .ThenBy(channel => channel.ToString())
            .ToList();
        DirtyField(ent!, nameof(ScpRadioComponent.Channels));

        if (ent.Comp.ActiveChannel is { } activeChannel
            && ent.Comp.Channels.Contains(activeChannel))
        {
            return;
        }

        if (keyHolder.DefaultChannel is { } defaultChannel)
        {
            var protoId = new ProtoId<RadioChannelPrototype>(defaultChannel);

            if (ent.Comp.Channels.Contains(protoId))
            {
                ent.Comp.ActiveChannel = protoId;
                DirtyField(ent!, nameof(ScpRadioComponent.ActiveChannel));

                return;
            }
        }

        ent.Comp.ActiveChannel = ent.Comp.Channels.Count > 0
            ? (ProtoId<RadioChannelPrototype>?) ent.Comp.Channels[0]
            : null;
        DirtyField(ent!, nameof(ScpRadioComponent.ActiveChannel));
    }

    private void AddCycleChannelVerb(Entity<ScpRadioComponent> ent, GetVerbsEvent<Verb> ev)
    {
        if (ent.Comp.Channels.Count <= 1)
            return;

        var verb = new Verb
        {
            Text = Loc.GetString("scp-radio-cycle-channel"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/refresh.svg.192dpi.png")),
            Act = () =>
            {
                CycleChannel(ent, ev.User);
            },
        };

        ev.Verbs.Add(verb);
    }

    private void AddToggleRadioVerb(Entity<ScpRadioComponent> ent, GetVerbsEvent<Verb> ev)
    {
        var verb = new Verb
        {
            Text = Loc.GetString("scp-radio-toggle-radio"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/dot.svg.192dpi.png")),
            Act = () =>
            {
                ToggleRadio(ent, !ent.Comp.Enabled, ev.User);
            },
        };

        ev.Verbs.Add(verb);
    }

    private void CycleChannel(Entity<ScpRadioComponent> ent, EntityUid user)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        var next = GetNextChannel(ent.Comp.Channels, ent.Comp.ActiveChannel);

        if (ent.Comp.ActiveChannel == next)
            return;

        if (!PrototypeManager.TryIndex(next, out var nextPrototype))
            return;

        ent.Comp.ActiveChannel = next;

        var message = Loc.GetString("scp-radio-current-channel", ("name", nextPrototype.LocalizedName));
        _popup.PopupClient(message, ent, user);
        _audio.PlayLocal(ent.Comp.ChannelCycleSound, user, ent);
    }

    protected virtual void ToggleMicrophone(Entity<ScpRadioComponent> ent, EntityUid user)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        ent.Comp.MicrophoneEnabled = !ent.Comp.MicrophoneEnabled;

        var message = Loc.GetString("scp-radio-microphone", ("value", ent.Comp.MicrophoneEnabled));
        _popup.PopupClient(message, ent, user);
        _audio.PlayLocal(ent.Comp.ToggleSound, user, ent);
    }

    protected virtual void ToggleRadio(Entity<ScpRadioComponent> ent, bool value, EntityUid? user = null) { }

    private bool ShouldPlayAmbience(EntityUid uid)
    {
        if (!_container.TryGetContainingContainer((uid, null, null), out var container))
            return true;

        return _hands.TryGetHand(container.Owner, container.ID, out _);
    }

    private static ProtoId<RadioChannelPrototype> GetNextChannel(List<ProtoId<RadioChannelPrototype>> channels,
        ProtoId<RadioChannelPrototype>? current)
    {
        if (channels.Count == 0)
            throw new InvalidOperationException("Cannot cycle channels on a radio without encryption keys.");

        if (current == null)
            return channels[0];

        var count = channels.Count;
        var index = channels.IndexOf(current.Value);

        if (index == -1)
            return channels[0];

        var nextIndex = index + 1;

        if (nextIndex >= count)
            return channels[0];

        return channels[nextIndex];
    }
}

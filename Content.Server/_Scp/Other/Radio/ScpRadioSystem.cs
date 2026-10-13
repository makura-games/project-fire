using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server.Popups;
using Content.Server.Power.EntitySystems;
using Content.Server.Radio;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Scp.Other.Radio;
using Content.Shared.Chat;
using Content.Shared.Emp;
using Content.Shared.Mobs.Components;
using Content.Shared.PowerCell;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Speech;
using Content.Shared.Speech.Components;
using Robust.Server.Audio;
using Robust.Server.Containers;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Scp.Other.Radio;

public sealed partial class ScpRadioSystem : SharedScpRadioSystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private PowerCellSystem _powerCell = default!;
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private ContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpRadioComponent, ListenEvent>(OnListen);
        SubscribeLocalEvent<ScpRadioComponent, ListenAttemptEvent>(OnAttemptListen);
        SubscribeLocalEvent<ScpRadioComponent, RadioReceiveEvent>(OnReceive);
        SubscribeLocalEvent<ScpRadioComponent, RadioReceiveAttemptEvent>(OnAttemptReceive);

        SubscribeLocalEvent<ScpRadioComponent, PowerCellChangedEvent>(OnPowerCellChanged);
        SubscribeLocalEvent<ScpRadioComponent, EmpPulseEvent>(OnEmpPulse);
    }

    private void OnListen(Entity<ScpRadioComponent> ent, ref ListenEvent args)
    {
        if (ent.Comp.ActiveChannel is not { } activeChannel)
            return;

        var channel = PrototypeManager.Index(activeChannel);
        _radio.SendRadioMessage(args.Source, args.Message, channel, ent);
        _audio.PlayEntity(ent.Comp.SendSound, args.Source, ent);

        // Это находится здесь, а не в начале, чтобы при малом заряде рация проиграла сообщения и сдохла.
        // Вместо того чтобы половина сообщений просто пропускалась из-за малого заряда.
        TryTakeCharge(ent, true);
    }

    private void OnAttemptListen(Entity<ScpRadioComponent> ent, ref ListenAttemptEvent args)
    {
        if (!ent.Comp.Enabled)
            args.Cancel();

        if (!ent.Comp.MicrophoneEnabled)
            args.Cancel();

        if (ent.Comp.ActiveChannel == null)
            args.Cancel();

        if (HasComp<EmpDisabledComponent>(ent))
            args.Cancel();
    }

    private void OnReceive(Entity<ScpRadioComponent> ent, ref RadioReceiveEvent args)
    {
        var receiverUid = GetUser(ent);

        if (!TryComp<ActorComponent>(receiverUid, out var actor))
        {
            // Если радио не находится у игрока - пусть говорит в чатик.
            SayMessage(ent, args.MessageSource, args.Message);
            _audio.PlayPvs(ent.Comp.ReceiveSound, ent);

            return;
        }

        if (args.MessageSource != receiverUid)
            _audio.PlayEntity(ent.Comp.ReceiveSound, receiverUid, ent);

        _net.ServerSendMessage(args.ChatMsg, actor.PlayerSession.Channel);
        if (receiverUid != args.MessageSource && !args.Receivers.Contains(receiverUid))
            args.Receivers.Add(receiverUid);

        // Это находится здесь, а не в начале, чтобы при малом заряде рация проиграла сообщения и сдохла.
        // Вместо того чтобы половина сообщений просто пропускалась из-за малого заряда.
        TryTakeCharge(ent);
    }

    private void OnAttemptReceive(Entity<ScpRadioComponent> ent, ref RadioReceiveAttemptEvent args)
    {
        if (!ent.Comp.Enabled)
            args.Cancelled = true;

        if (HasComp<EmpDisabledComponent>(ent))
            args.Cancelled = true;

        if (args.Cancelled)
            return;

        if (HasHeadsetForChannel(ent, args.Channel.ID))
            args.Cancelled = true;
    }

    protected override void OnEncryptionChannelsChanged(Entity<ScpRadioComponent> ent, ref EncryptionChannelsChangedEvent args)
    {
        base.OnEncryptionChannelsChanged(ent, ref args);

        UpdateMicrophone(ent);
        UpdateSpeaker(ent);
    }

    protected override void ToggleMicrophone(Entity<ScpRadioComponent> ent, EntityUid user)
    {
        base.ToggleMicrophone(ent, user);

        UpdateMicrophone(ent);
    }

    protected override void OnStartup(Entity<ScpRadioComponent> ent, ref ComponentStartup args)
    {
        base.OnStartup(ent, ref args);

        UpdateMicrophone(ent);
        UpdateSpeaker(ent);
    }

    private bool TryTakeCharge(Entity<ScpRadioComponent> ent, bool sending = false)
    {
        if (!_powerCell.TryGetBatteryFromSlot(ent.Owner, out var battery))
        {
            ToggleRadio(ent, false);
            return false;
        }

        var wattage = sending ? ent.Comp.WattageSendMessage : ent.Comp.WattageReceiveMessage;

        if (!_battery.TryUseCharge(battery.Value.AsNullable(), wattage))
        {
            ToggleRadio(ent, false);

            // Дообъедаем остаток заряда, чтобы избежать проблем с минимальными остатками типа 0.0001%
            var currentCharge = _battery.GetCharge(battery.Value.AsNullable());
            _battery.TryUseCharge(battery.Value.AsNullable(), currentCharge);

            return false;
        }

        return true;
    }

    private void SayMessage(Entity<ScpRadioComponent> ent, EntityUid source, string message)
    {
        var nameEv = new TransformSpeakerNameEvent(source, Name(source));
        RaiseLocalEvent(source, nameEv);

        var name = Loc.GetString("speech-name-relay",
            ("speaker", Name(ent)),
            ("originalName", nameEv.VoiceName));

        _chat.TrySendInGameICMessage(ent, message, InGameICChatType.Whisper, ChatTransmitRange.GhostRangeLimit, nameOverride: name, checkRadioPrefix: false);
    }

    protected override void ToggleRadio(Entity<ScpRadioComponent> ent, bool value, EntityUid? user = null)
    {
        base.ToggleRadio(ent, value, user);

        user ??= GetUser(ent);

        // Если мы включаем(value == true) и недостаточно заряда -> выходим из метода
        // Если выключаем(value == false) -> проверка на заряд не нужна, просто выключаем.
        if ((!_powerCell.TryGetBatteryFromSlot(ent.Owner, out var battery)
             || MathHelper.CloseTo(_battery.GetCharge(battery.Value.AsNullable()), 0f))
            && value)
        {
            var failMessage = Loc.GetString("scp-radio-not-enough-charge");
            _popup.PopupEntity(failMessage, ent, user.Value);

            return;
        }

        ent.Comp.Enabled = value;
        Dirty(ent);

        var message = Loc.GetString("scp-radio-toggle-message", ("name", Name(ent)), ("value", ent.Comp.Enabled));

        _popup.PopupEntity(message, ent, user.Value);
        UpdateAmbience(ent);
        _audio.PlayEntity(ent.Comp.ToggleSound, user.Value, ent);

        UpdateMicrophone(ent);
        UpdateSpeaker(ent);
    }

    private void UpdateMicrophone(Entity<ScpRadioComponent> ent)
    {
        if (ent.Comp.MicrophoneEnabled && ent.Comp.ActiveChannel != null)
            EnsureComp<ActiveListenerComponent>(ent).Range = ent.Comp.ListenRange;
        else
            RemCompDeferred<ActiveListenerComponent>(ent);
    }

    private void UpdateSpeaker(Entity<ScpRadioComponent> ent)
    {
        if (ent.Comp.Enabled && ent.Comp.Channels.Count > 0)
            EnsureComp<ActiveRadioComponent>(ent).Channels = ent.Comp.Channels.ToHashSet();
        else
            RemCompDeferred<ActiveRadioComponent>(ent);
    }

    private void OnPowerCellChanged(Entity<ScpRadioComponent> ent, ref PowerCellChangedEvent args)
    {
        if (!ent.Comp.Enabled)
            return;

        if (args.Ejected)
            ToggleRadio(ent, false);
    }

    private void OnEmpPulse(Entity<ScpRadioComponent> ent, ref EmpPulseEvent args)
    {
        args.Affected = true;
        args.Disabled = true;
    }

    private bool HasHeadsetForChannel(Entity<ScpRadioComponent> ent, ProtoId<RadioChannelPrototype> channel)
    {
        if (!TryGetUser(ent, out var user))
            return false;

        if (!TryComp<WearingHeadsetComponent>(user, out var wearingHeadset))
            return false;

        return TryComp<EncryptionKeyHolderComponent>(wearingHeadset.Headset, out var keyHolder)
               && keyHolder.Channels.Contains(channel);
    }

    private EntityUid GetUser(EntityUid radio)
    {
        var user = Transform(radio).ParentUid;

        // Дополнительная проверка для раций, находящихся в сумке/другой вещи
        // Если первый парент не человек(например сумка), то второй должен быть носитель этой сумки(игрок)
        if (!HasComp<MobStateComponent>(user) && _container.IsEntityInContainer(radio))
            user = Transform(user).ParentUid;

        // Вдруг, по какой-то случайности, ParentUid будет не существовать вообще.
        if (!Exists(user))
            Log.Error("Found non-existing user while toggling radio");

        return user;
    }

    private bool TryGetUser(Entity<ScpRadioComponent> ent, out EntityUid user)
    {
        user = GetUser(ent);
        return Exists(user);
    }
}

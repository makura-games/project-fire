using Content.Server.Radio.EntitySystems;
using Content.Server.Popups;
using Content.Shared._Scp.Helpers;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Pinpointer;
using Content.Shared.Timing;
using Content.Shared._Scp.Trigger.TriggerOnSignalSwitch;
using Robust.Server.GameObjects;

namespace Content.Server._Scp.Other.Radio;

public sealed partial class RadioCallButtonSystem : EntitySystem
{
    private const string RadioCallUseDelayId = "RadioCall";

    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private UseDelaySystem _delay = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RadioCallButtonComponent, SignalSwitchActivatedEvent>(OnButtonPressed,
            before: [typeof(TriggerOnSignalSwitchSystem)]);
        SubscribeLocalEvent<RadioCallButtonComponent, ExaminedEvent>(OnExamined);
    }

    private void OnButtonPressed(Entity<RadioCallButtonComponent> ent, ref SignalSwitchActivatedEvent args)
    {
        if (!_delay.TryResetDelay(ent.Owner, checkDelayed: true, id: RadioCallUseDelayId))
        {
            args.Cancelled = true;
            return;
        }

        var locationName = GetLocationName(ent);

        // Get the localized message.
        var message = Loc.GetString(ent.Comp.MessageKey, ("location", locationName));

        // Send the radio message.
        foreach (var channel in ent.Comp.RadioChannel)
        {
            _radio.SendRadioMessage(ent.Owner, message, channel, ent.Owner);
        }

        if (args.User is { } user)
            _popup.PopupCursor(Loc.GetString("scp-radio-button-request-sent"), user, PopupType.Medium);
    }

    private void OnExamined(Entity<RadioCallButtonComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(RadioCallButtonComponent)))
        {
            args.PushMarkup(Loc.GetString("scp-radio-button-location-examine", ("location", GetLocationName(ent))));
        }
    }

    private string GetLocationName(Entity<RadioCallButtonComponent> ent)
    {
        if (!string.IsNullOrEmpty(ent.Comp.RoomName))
            return ent.Comp.RoomName;

        var locationName = Loc.GetString("scp-radio-button-unknown-location");
        return ExtractLocationName(ent, locationName);
    }

    private string ExtractLocationName(Entity<RadioCallButtonComponent> ent, string locationName)
    {
        var coordinates = _transform.GetMapCoordinates(ent);
        var closestDistanceSquared = ent.Comp.BeaconSearchRadius * ent.Comp.BeaconSearchRadius;

        using var beacons = HashSetPoolEntity<NavMapBeaconComponent>.Rent();
        _lookup.GetEntitiesInRange(coordinates, ent.Comp.BeaconSearchRadius, beacons.Value, LookupFlags.StaticSundries);

        foreach (var beacon in beacons.Value)
        {
            var beaconXform = Transform(beacon);

            if (!beacon.Comp.Enabled || !beaconXform.Anchored || coordinates.MapId != beaconXform.MapID)
                continue;

            if (string.IsNullOrEmpty(beacon.Comp.Text))
                continue;

            var beaconCoords = _transform.GetMapCoordinates(beacon, beaconXform);
            var distanceSquared = (coordinates.Position - beaconCoords.Position).LengthSquared();

            if (distanceSquared <= closestDistanceSquared)
            {
                closestDistanceSquared = distanceSquared;
                locationName = beacon.Comp.Text;
            }
        }

        return locationName;
    }
}

using System.Diagnostics.CodeAnalysis;
using Content.Server._Sunrise.Helpers;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Server.Radio.EntitySystems;
using Content.Server.Roles.Jobs;
using Content.Server.Station.Systems;
using Content.Shared.GameTicking;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._Scp.Misc.AnnounceOnSpawn;

public sealed partial class ScpAnnounceOnSpawnSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private SunriseHelpersSystem _helpers = default!;
    [Dependency] private JobSystem _job = default!;
    [Dependency] private MindSystem _mind = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpAnnounceOnSpawnComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ScpAnnounceOnSpawnComponent, PlayerSpawnCompleteEvent>(OnArrival);
        SubscribeLocalEvent<ScpAnnounceOnSpawnComponent, EntParentChangedMessage>(OnParentChanged);

        Log.Level = LogLevel.Info;
    }

    private void OnMapInit(Entity<ScpAnnounceOnSpawnComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Announced)
            return;

        if (!ent.Comp.AnnounceOnMapInit)
            return;

        if (!CanAnnounce(ent.Comp))
            return;

        Announce(ent);
    }

    private void OnArrival(Entity<ScpAnnounceOnSpawnComponent> ent, ref PlayerSpawnCompleteEvent args)
    {
        if (ent.Comp.Announced)
            return;

        if (!ent.Comp.AnnounceOnArrival)
            return;

        if (!CanAnnounce(ent.Comp))
            return;

        Announce(ent);
    }

    private void OnParentChanged(Entity<ScpAnnounceOnSpawnComponent> ent, ref EntParentChangedMessage args)
    {
        if (ent.Comp.Announced)
            return;

        if (!ent.Comp.AnnounceOnStationGridEntered)
            return;

        // Если старой карты нет - значит сущность тут заспавнилась. А это другой случай
        if (!args.OldMapId.HasValue)
            return;

        var station = _station.GetOwningStation(ent, args.Transform);
        if (!station.HasValue)
            return;

        if (!CanAnnounce(ent.Comp))
            return;

        Announce(ent);
    }

    private void Announce(Entity<ScpAnnounceOnSpawnComponent> ent)
    {
        if (!_helpers.TryGetFirst<ScpAnnounceOnSpawnSourceComponent>(out var source))
        {
            Log.Warning($"Failed to found any entity with {nameof(ScpAnnounceOnSpawnSourceComponent)} while announcing spawn in radio!");
            return;
        }

        var jobName = string.Empty;
        if (ent.Comp.IncludeJobName && !TryGetJobName(ent, out jobName))
        {
            Log.Error($"Failed to get job name for {ToPrettyString(ent)} while announcing spawn!");
            return;
        }

        ent.Comp.Text = Loc.GetString(ent.Comp.Text, ("name", Name(ent)), ("job", jobName));

        AnnounceRadio(ent, source.Value);
        AnnounceGlobal(ent, source.Value);

        var map = _transform.GetMapId(ent.Owner);
        _audio.PlayGlobal(ent.Comp.GlobalAnnouncementSound, Filter.BroadcastMap(map), true);

        ent.Comp.Announced = true;
    }

    private void AnnounceRadio(Entity<ScpAnnounceOnSpawnComponent> ent, EntityUid source)
    {
        if (ent.Comp.Channels == null || ent.Comp.Channels.Count == 0)
            return;

        foreach (var channel in ent.Comp.Channels)
        {
            _radio.SendRadioMessage(source, ent.Comp.Text, channel, source);
        }
    }

    private void AnnounceGlobal(Entity<ScpAnnounceOnSpawnComponent> ent, EntityUid source)
    {
        if (!ent.Comp.UseGlobalAnnouncement)
            return;

        var name = Loc.GetString("scp-announce-on-spawn-source-name");
        _chat.DispatchStationAnnouncement(source,
            ent.Comp.Text,
            name,
            colorOverride: ent.Comp.StationAnnouncementColor,
            announceVoice: ent.Comp.StationAnnouncementVoice,
            announcementSound: ent.Comp.StationAnnouncementSound);
    }

    private bool TryGetJobName(EntityUid uid, [NotNullWhen(true)] out string? name)
    {
        name = null;

        if (!_mind.TryGetMind(uid, out var mind, out _))
            return false;

        if (!_job.MindTryGetJobName(mind, out name))
            return false;

        return true;
    }

    private bool CanAnnounce(ScpAnnounceOnSpawnComponent component)
    {
        return component.RequiredGameRunLevel == null ||
               _gameTicker.RunLevel == component.RequiredGameRunLevel;
    }
}

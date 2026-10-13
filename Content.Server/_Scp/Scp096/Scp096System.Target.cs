using Content.Shared._Scp.Audio;
using Content.Shared._Scp.Fear;
using Content.Shared._Scp.Scp096.Main.Components;
using Content.Shared.Audio;
using Robust.Server.Audio;
using Robust.Server.GameStates;
using Robust.Server.Player;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Scp.Scp096;

public sealed partial class Scp096System
{
    [Dependency] private PvsOverrideSystem _pvsOverride = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IPlayerManager _player = default!;

    private static readonly ProtoId<AmbientMusicPrototype> TargetAmbience = "Scp096Target";

    private void InitializeTarget()
    {
        SubscribeLocalEvent<Scp096TargetComponent, FearCalmDownAttemptEvent>(OnFearCalmDown);
    }

    private static void OnFearCalmDown(Entity<Scp096TargetComponent> ent, ref FearCalmDownAttemptEvent args)
    {
        args.Cancel();
    }

    protected override void OnTargetStartup(Entity<Scp096TargetComponent> ent, ref ComponentStartup args)
    {
        base.OnTargetStartup(ent, ref args);

        var filter = Filter.Empty().AddWhereAttachedEntity(Scp096Query.HasComp);
        _pvsOverride.AddSessionOverrides(ent, filter);

        RaiseNetworkEvent(new NetworkAmbientMusicEvent(TargetAmbience), ent);
        _audio.PlayGlobal(ent.Comp.SeenSound, ent);

        _meta.AddFlag(ent, MetaDataFlags.ExtraTransformEvents);
    }

    protected override void OnTargetShutdown(Entity<Scp096TargetComponent> ent, ref ComponentShutdown args)
    {
        base.OnTargetShutdown(ent, ref args);

        var query = EntityQueryEnumerator<Scp096Component>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!_player.TryGetSessionByEntity(uid, out var session))
                continue;

            _pvsOverride.RemoveSessionOverride(ent, session);
        }

        RaiseNetworkEvent(new NetworkAmbientMusicEventStop(), ent);

        _meta.RemoveFlag(ent, MetaDataFlags.ExtraTransformEvents);
    }
}

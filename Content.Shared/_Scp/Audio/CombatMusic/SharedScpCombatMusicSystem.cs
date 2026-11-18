using System.Diagnostics.CodeAnalysis;
using Content.Shared._Scp.Mobs.Components;
using Content.Shared._Scp.Scp035;
using Content.Shared._Scp.Scp049;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Audio.CombatMusic;

/// <summary>
/// Resolves the local player's combat music group.
/// </summary>
public sealed partial class SharedScpCombatMusicSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = null!;

    /// <summary>
    /// Soundtrack used by bodies outside the explicitly configured groups.
    /// </summary>
    public static readonly ProtoId<ScpCombatMusicPrototype> FallbackMusic = "ScpCombatMusicFallback";

    private static readonly ProtoId<ScpCombatMusicPrototype> ScpMusic = "ScpCombatMusicSCP";

    /// <summary>
    /// Uses the SCP soundtrack for SCP bodies and profession music for SCP-035 hosts and SCP-049 minions.
    /// </summary>
    public bool TryGetMusic(EntityUid uid, [NotNullWhen(true)] out ScpCombatMusicPrototype? music)
    {
        music = null;

        if (HasComp<ScpComponent>(uid) &&
            !HasComp<Scp035MaskUserComponent>(uid) &&
            !HasComp<Scp049MinionComponent>(uid))
        {
            return _prototype.TryIndex(ScpMusic, out music);
        }

        var musicId = TryComp<ScpCombatMusicComponent>(uid, out var component)
            ? component.Music ?? FallbackMusic
            : FallbackMusic;

        return _prototype.TryIndex(musicId, out music);
    }
}

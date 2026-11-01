using System.Diagnostics.CodeAnalysis;
using Content.Shared._Scp.Mobs.Components;
using Content.Shared._Scp.Scp035;
using Content.Shared._Scp.Scp049;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Audio.CombatMusic;

/// <summary>
/// Provides the server-selected combat music to the local player.
/// </summary>
public sealed partial class SharedScpCombatMusicSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = null!;

    /// <summary>
    /// Soundtrack used by bodies outside the explicitly configured groups.
    /// </summary>
    public static readonly ProtoId<ScpCombatMusicPrototype> FallbackMusic = "ScpCombatMusicFallback";

    /// <summary>
    /// Returns the body's selected music or the fallback, excluding SCPs other than SCP-035 hosts and SCP-049 minions.
    /// </summary>
    public bool TryGetMusic(EntityUid uid, [NotNullWhen(true)] out ScpCombatMusicPrototype? music)
    {
        music = null;

        if (HasComp<ScpComponent>(uid) &&
            !HasComp<Scp035MaskUserComponent>(uid) &&
            !HasComp<Scp049MinionComponent>(uid))
        {
            return false;
        }

        var musicId = TryComp<ScpCombatMusicComponent>(uid, out var component)
            ? component.Music ?? FallbackMusic
            : FallbackMusic;

        return _prototype.TryIndex(musicId, out music);
    }
}

using Content.Shared.Roles;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Audio.CombatMusic;

/// <summary>
/// Music for a job group and its priority when departments overlap.
/// </summary>
[Prototype]
public sealed partial class ScpCombatMusicPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    /// <summary>
    /// Track or collection from which a new combat soundtrack is selected.
    /// </summary>
    [DataField(required: true)]
    public SoundSpecifier Sound = null!;

    /// <summary>
    /// Departments whose jobs use this group. Empty for groups assigned by other rules.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<DepartmentPrototype>> Departments = [];

    /// <summary>
    /// Higher values win when a job belongs to multiple matching departments.
    /// </summary>
    [DataField]
    public int Priority;
}

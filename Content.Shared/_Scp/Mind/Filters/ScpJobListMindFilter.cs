using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Prototypes;

#pragma warning disable IDE0130 // Пространство имён оставлено совместимым с ванильными mind filter.
namespace Content.Shared.Mind.Filters;
#pragma warning restore IDE0130

/// <summary>
/// Фильтр по определённому списку должностей
/// </summary>
public sealed partial class ScpJobListMindFilter : MindFilter
{
    /// <summary>
    /// Допустимые job-id
    /// </summary>
    [DataField(required: true)]
    public List<ProtoId<JobPrototype>> Jobs = [];

    protected override bool ShouldRemove(Entity<MindComponent> mind, EntityUid? exclude, IEntityManager entMan)
    {
        var jobSys = entMan.System<SharedJobSystem>();

        foreach (var job in Jobs)
        {
            if (jobSys.MindHasJobWithId(mind, job))
                return false;
        }

        return true;
    }
}

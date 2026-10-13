using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Rounding;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Damage.ExaminableDamage;

/// <summary>
/// <para>Система для создания описания о повреждениях.</para>
/// Поддерживает живых сущностей и структуры.
/// Метод выведения сообщения определяется настройкой в компоненте <see cref="ScpExaminableDamageMode"/>
/// </summary>
/// <remarks>
/// <para>Для живых сущностей требуются компоненты <see cref="MobStateComponent"/> и <see cref="MobThresholdsComponent"/></para>
/// Для структуры требуется <see cref="DestructibleComponent"/>
/// </remarks>
// TODO: Написать больше описаний для различных объектов, вроде SCP-049, SCP-049-2
public abstract partial class SharedScpExaminableDamageSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private SharedJobSystem _job = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    public const int Priority = -99;
    public const double FullPercent = 1d;

    private const string DefaultPrefix = "scp-examinable-damage-message-prefix";
    private const string DepartmentMessagePrefix = "scp-examinable-damage-department-specific-message-prefix";
    private const string JobMessagePrefix = "scp-examinable-damage-job-specific-message-prefix";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpExaminableDamageComponent, ExaminedEvent>(OnExamine);
    }

    private void OnExamine(Entity<ScpExaminableDamageComponent> ent, ref ExaminedEvent args)
    {
        var target = GetProperEntity(ent);
        switch (ent.Comp.Mode)
        {
            case ScpExaminableDamageMode.Structure:
                StructureExamine(ent, target, ref args);
                break;

            case ScpExaminableDamageMode.MobToCritical:
                MobExamine(ent, target, MobState.Critical, ref args);
                break;

            case ScpExaminableDamageMode.MobToDeath:
                MobExamine(ent, target, MobState.Dead, ref args);
                break;

            default:
                throw new NotImplementedException($"Found not implemented type {ent.Comp.Mode}");
        }
    }

    private void MobExamine(Entity<ScpExaminableDamageComponent> ent, EntityUid target, MobState toState, ref ExaminedEvent args)
    {
        if (!_mobThreshold.TryGetThresholdForState(target, toState, out var max))
            return;

        CreateMessage(ent, target, max.Value, ref args);
    }

    protected virtual void StructureExamine(Entity<ScpExaminableDamageComponent> ent, EntityUid target, ref ExaminedEvent args) {}

    protected void CreateMessage(Entity<ScpExaminableDamageComponent> ent,
        EntityUid target,
        FixedPoint2 maxDamage,
        ref ExaminedEvent args)
    {
        var percent = GetDamagePercent(target, maxDamage);

        TryAddGeneralMessage(ent, percent, ref args);
        TryAddSpecificMessage(ent, percent, ref args);
    }

    private bool TryAddGeneralMessage(Entity<ScpExaminableDamageComponent> ent, float percent, ref ExaminedEvent args)
    {
        if (!_prototype.TryIndex(ent.Comp.GeneralMessages, out var messages))
            return false;

        if (messages.Values.Count == 0)
            return false;

        var level = ContentHelpers.RoundToNearestLevels(percent, FullPercent, messages.Values.Count - 1);
        var message = messages.Values[level];
        var prefix = Loc.GetString(DefaultPrefix);
        var color = ent.Comp.Color.ToHex();

        message = Loc.GetString(message);

        var formatted = $"\n{ prefix }\n[color={ color }]{ message }[/color]";
        args.PushMarkup(formatted, Priority);

        return true;
    }

    private bool TryAddSpecificMessage(Entity<ScpExaminableDamageComponent> ent, float percent, ref ExaminedEvent args)
    {
        if (!_mind.TryGetMind(args.Examiner, out var mind, out _))
            return false;

        if (!_job.MindTryGetJob(mind, out var job))
            return false;

        if (!TryAddJobSpecificMessage(ent, percent, job, ref args)
            && !TryAddDepartmentSpecificMessage(ent, percent, job.ID, ref args))
            return false;

        return true;
    }

    private bool TryAddJobSpecificMessage(Entity<ScpExaminableDamageComponent> ent,
        float percent,
        JobPrototype job,
        ref ExaminedEvent args)
    {
        if (ent.Comp.JobMessages.Count == 0)
            return false;

        if (!ent.Comp.JobMessages.TryGetValue(job.ID, out var messageList))
            return false;

        if (!_prototype.TryIndex(messageList, out var messages))
            return false;

        if (messages.Values.Count == 0)
            return false;

        var level = ContentHelpers.RoundToNearestLevels(percent, FullPercent, messages.Values.Count - 1);
        var message = messages.Values[level];
        var prefix = Loc.GetString(JobMessagePrefix, ("job", job.LocalizedName));
        var color = ent.Comp.Color.ToHex();

        message = Loc.GetString(message);

        var formatted = $"\n{ prefix }\n[color={ color }]{ message }[/color]";
        args.PushMarkup(formatted, Priority - 1);

        return true;
    }

    private bool TryAddDepartmentSpecificMessage(Entity<ScpExaminableDamageComponent> ent,
        float percent,
        ProtoId<JobPrototype> job,
        ref ExaminedEvent args)
    {
        if (ent.Comp.DepartmentMessages.Count == 0)
            return false;

        if (!_job.TryGetDepartment(job, out var department))
            return false;

        if (!ent.Comp.DepartmentMessages.TryGetValue(department.ID, out var messageList))
            return false;

        if (!_prototype.TryIndex(messageList, out var messages))
            return false;

        if (messages.Values.Count == 0)
            return false;

        var level = ContentHelpers.RoundToNearestLevels(percent, FullPercent, messages.Values.Count - 1);
        var message = messages.Values[level];
        var departmentColor = department.Color.ToHex();
        var prefix = Loc.GetString(DepartmentMessagePrefix, ("department", $"[color={departmentColor}]{Loc.GetString(department.Name)}[/color]"));
        var color = ent.Comp.Color.ToHex();

        message = Loc.GetString(message);

        var formatted = $"\n{ prefix }\n[color={ color }]{ message }[/color]";
        args.PushMarkup(formatted, Priority - 2);

        return true;
    }

    #region Helpers

    /// <summary>
    /// Возвращает значение между 0 и 1, показывающее степень повреждение сущности,
    /// где 0 это отсутствие урона, а 1 максимальная степень урона.
    /// </summary>
    /// <returns>Насколько сущность получила урона в оценке от 0 до 1</returns>
    public float GetDamagePercent(Entity<DamageableComponent?> ent, FixedPoint2 damageThreshold)
    {
        if (damageThreshold <= 0)
            return 0f;

        if (!Resolve(ent, ref ent.Comp))
            return 0f;

        return (_damageable.GetTotalDamage(ent) / damageThreshold).Float();
    }

    /// <summary>
    /// Отправляет ивент, который может быть перехвачен системами, чтобы передать другую сущность для проверки урона.
    /// Может быть полезно, если требуется для одной сущности выводить описание урона другой сущности.
    /// </summary>
    /// <param name="defaultEntity">Сущность по умолчанию. Именно ее осмотрел игрок</param>
    /// <returns>Самую предпочтительную сущность для проверки на уровень урона.</returns>
    protected EntityUid GetProperEntity(EntityUid defaultEntity)
    {
        var ev = new ExaminableDamageRelayEvent();
        RaiseLocalEvent(defaultEntity, ref ev);

        return ev.Entity ?? defaultEntity;
    }

    #endregion
}

[ByRefEvent]
public record struct ExaminableDamageRelayEvent(EntityUid? Entity = null);

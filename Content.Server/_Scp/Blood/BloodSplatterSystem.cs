using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._Scp.Blood;
using Content.Shared._Scp.Other.Events;
using Content.Shared._Sunrise.Weapons.Events;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee.Events;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Random;

namespace Content.Server._Scp.Blood;

/// <summary>
/// Система, управляющая брызгами крови.
/// Позволяет создавать красивые частички крови, которые некоторое время разлетаются в разные стороны.
/// </summary>
public sealed partial class BloodSplatterSystem : SharedBloodSplatterSystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private TransformSystem _transform = default!;

    private const string SolutionName = "blood";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BloodstreamComponent, AttackedEvent>(OnAttacked);
        SubscribeLocalEvent<BloodstreamComponent, HitScanAttackedEvent>(OnHitscanAttacked);
        SubscribeLocalEvent<BloodSplattererComponent, ProjectileHitEvent>(OnProjectileHit);

        InitializeParticles();

        Log.Level = LogLevel.Info;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Проходимся по всем частичкам крови и двигаем их
        UpdateParticles();
    }

    private void OnAttacked(Entity<BloodstreamComponent> ent, ref AttackedEvent args)
    {
        if (!TryGetSource(args.User, args.Used, out var source))
            return;

        TrySplat(source.Value, ent);
    }

    private void OnHitscanAttacked(Entity<BloodstreamComponent> ent, ref HitScanAttackedEvent args)
    {
        if (!TryComp<BloodSplattererComponent>(args.Gun, out var splatterer))
            return;

        TrySplat((args.Gun, splatterer), ent);
    }

    private void OnProjectileHit(Entity<BloodSplattererComponent> ent, ref ProjectileHitEvent args)
    {
        if (!TryComp<BloodstreamComponent>(args.Target, out var bloodstream))
            return;

        TrySplat(ent, (args.Target, bloodstream));
    }

    /// <summary>
    /// Пытается создать брызги крови, летящие от цели при ее атаке.
    /// </summary>
    /// <remarks>
    /// Количество брызг, их скорость и т.п. определяется параметрами в компоненте.
    /// </remarks>
    /// <param name="ent">Сущность, которая атакует персонажа и поддерживает брызги при ударе</param>
    /// <param name="target">Цель, которую атакуют</param>
    public bool TrySplat(Entity<BloodSplattererComponent> ent, Entity<BloodstreamComponent> target)
    {
        var ev = new HitScanPierceAttemptEvent(ent.Comp.PierceLevel, true);
        RaiseLocalEvent(target, ref ev);

        // Броня не пробита
        if (!ev.Pierced)
            return false;

        // Ранний выход, если у персонажа не осталось крови.
        if (!_solution.ResolveSolution(target.Owner, target.Comp.BloodSolutionName, ref target.Comp.BloodSolution, out var bloodSolution)
            || bloodSolution.Volume == FixedPoint2.Zero)
            return false;

        Splat(ent, target);
        _audio.PlayPvs(ent.Comp.AttackSound, target);

        return true;
    }

    /// <summary>
    /// Создает капли крови, летящие от цели.
    /// </summary>
    /// <param name="ent">Сущность, которая инициировала выделение частичек крови(оружие, персонаж)</param>
    /// <param name="target">Цель, которая будет терять кровь и испускать частички</param>
    public void Splat(Entity<BloodSplattererComponent> ent, Entity<BloodstreamComponent> target)
    {
        var victimPosition = _transform.GetWorldPosition(target);
        var attackerPosition = _transform.GetWorldPosition(ent);

        // Вычисляем базовое направление от атакующего к жертве
        var baseDirection = (victimPosition - attackerPosition).Normalized();
        var baseAngle = MathF.Atan2(baseDirection.Y, baseDirection.X);

        // Вычисляем случайный угол в пределах заданного разброса
        var spreadRadians = MathF.PI * ent.Comp.SpreadAngle / 180f; // Конвертируем градусы в радианы

        if (_random.Prob(ent.Comp.BloodLineProbability))
            CreateBloodLine(ent, target);

        if (_random.Prob(ent.Comp.Probability))
            SpawnBloodParticles(ent, target, Angle.FromDegrees(baseAngle), spreadRadians);
    }

    /// <summary>
    /// Пытается определить инициатора, который запустит выделение частичек крови.
    /// Это может быть атакующий персонаж или оружие, которым атакуют.
    /// </summary>
    /// <param name="user">Персонаж, который атакует</param>
    /// <param name="used">Предмет, который используется для атаки</param>
    /// <param name="splatterer">Инициатор выделения частичек крови</param>
    /// <returns>Удалось ли найти инициатора</returns>
    private bool TryGetSource(Entity<BloodSplattererComponent?> user,
        Entity<BloodSplattererComponent?> used,
        [NotNullWhen(true)] out Entity<BloodSplattererComponent>? splatterer)
    {
        splatterer = null;

        if (Resolve(user, ref user.Comp, false))
        {
            splatterer = (user, user.Comp);
            return true;
        }

        if (Resolve(used, ref used.Comp, false))
        {
            splatterer = (used, used.Comp);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Пытается рассчитать необходимое количество крови, которое нужно взять у персонажа.
    /// После пытается переместить нужное количество крови в частичку <see cref="transferTo"/>
    /// </summary>
    /// <param name="target">Цель, из которой будет черпаться кровь.</param>
    /// <param name="amount">Минимальное и максимальное значение, из которого будет рандомиться количество крови для передачи</param>
    /// <param name="transferTo">Сущность, в которую будет передана кровь из персонажа</param>
    /// <returns>Удалось передать кровь или нет</returns>
    private bool TryTakeBlood(Entity<BloodstreamComponent?> target, Vector2 amount, EntityUid transferTo)
    {
        if (!Resolve(target, ref target.Comp))
            return false;

        // Получаем кровь персонажа
        if (!_solution.ResolveSolution(target.Owner, target.Comp.BloodSolutionName, ref target.Comp.BloodSolution, out var bloodSolution)
            || bloodSolution.Volume == FixedPoint2.Zero)
            return false;

        // Рассчитываем количество крови, которое нужно взять у персонажа и переместить в частичку
        var randomizedBlood = _random.NextFloat(amount.X, amount.Y);
        var amountToTake = FixedPoint2.Min(randomizedBlood, bloodSolution.Volume);
        var solution = _solution.SplitSolution(target.Comp.BloodSolution.Value, amountToTake);

        // Если в человеке закончилась кровь.
        if (solution.Volume == FixedPoint2.Zero)
            return false;

        if (!_solution.TryGetSolution(transferTo, SolutionName, out var solutionEntity, out _))
        {
            Log.Error($"Found blood splatter without any solution, prototype: {Prototype(transferTo)}");
            return false;
        }

        if (!_solution.TryAddSolution(solutionEntity.Value, solution))
            return false;

        return true;
    }
}


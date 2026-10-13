using System.Diagnostics.CodeAnalysis;
using Content.Server.Power.EntitySystems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;
using Content.Server.Jittering;
using Content.Shared._Scp.Research.Misc;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Content.Shared.Jittering;
using Content.Shared.Power;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Shared.Kitchen.Components;

namespace Content.Server._Scp.Research.ReagentSynthesizer;

public sealed partial class ReagentSynthesizerSystem : EntitySystem
{
    [Dependency] private SharedSolutionContainerSystem _solutionContainersSystem = default!;
    [Dependency] private ItemSlotsSystem _itemSlotsSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private JitteringSystem _jitter = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private SharedEntityEffectsSystem _effects = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;

    private readonly FixedPoint2 _requiredVolume = 30f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActiveReagentSynthesizerComponent, ComponentStartup>((uid, _, _) => AddVisuals(uid));
        SubscribeLocalEvent<ActiveReagentSynthesizerComponent, ComponentRemove>((uid, _, _) => RemoveVisuals(uid));

        SubscribeLocalEvent<ReagentSynthesizerComponent, InteractUsingEvent>(OnInteractUsing);

        SubscribeLocalEvent<ReagentSynthesizerComponent, EntInsertedIntoContainerMessage>(OnContainerModified);
        SubscribeLocalEvent<ReagentSynthesizerComponent, EntRemovedFromContainerMessage>(OnContainerModified);
        SubscribeLocalEvent<ReagentSynthesizerComponent, ContainerIsRemovingAttemptEvent>(OnEntRemoveAttempt);

        SubscribeLocalEvent<ReagentSynthesizerComponent, PowerChangedEvent>(OnPowerChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ActiveReagentSynthesizerComponent, ReagentSynthesizerComponent>();
        while (query.MoveNext(out var uid, out var active, out var synthesizer))
        {
            if (!this.IsPowered(uid, EntityManager))
                continue;

            if (active.EndTime > _timing.CurTime)
                continue;

            synthesizer.AudioStream = _audioSystem.Stop(synthesizer.AudioStream);
            RemCompDeferred<ActiveReagentSynthesizerComponent>(uid);

            var container = _itemSlotsSystem.GetItemOrNull(uid, ReagentGrinderComponent.BeakerSlotId);

            if (!container.HasValue)
                continue;

            if (!IsSynthesisable(container.Value, out var solutionEntity))
                continue;

            SynthesizeSolution(synthesizer, solutionEntity.Value);
        }
    }

    private void OnEntRemoveAttempt(Entity<ReagentSynthesizerComponent> entity, ref ContainerIsRemovingAttemptEvent args)
    {
        if (HasComp<ActiveReagentSynthesizerComponent>(entity))
            args.Cancel();
    }

    private void OnContainerModified(EntityUid uid, ReagentSynthesizerComponent reagentGrinder, ContainerModifiedMessage args)
    {
        if (HasComp<ActiveReagentSynthesizerComponent>(uid))
            return;

        if (!this.IsPowered(uid, EntityManager))
            return;

        DoWork((uid, reagentGrinder));
    }

    private void OnInteractUsing(Entity<ReagentSynthesizerComponent> entity, ref InteractUsingEvent args)
    {
        var heldEnt = args.Used;
        var inputContainer = _containerSystem.EnsureContainer<ContainerSlot>(entity.Owner, ReagentGrinderComponent.BeakerSlotId);

        if (!HasComp<FitsInDispenserComponent>(heldEnt))
        {
            _popupSystem.PopupEntity(Loc.GetString("reagent-grinder-component-cannot-put-entity-message"), entity.Owner, args.User);

            return;
        }

        if (args.Handled)
            return;

        if (!_containerSystem.Insert(heldEnt, inputContainer))
            return;

        args.Handled = true;
    }

    private void DoWork(Entity<ReagentSynthesizerComponent> synthesizer)
    {
        var container = _containerSystem.EnsureContainer<ContainerSlot>(synthesizer, ReagentGrinderComponent.BeakerSlotId);
        var containerUid = _itemSlotsSystem.GetItemOrNull(synthesizer, ReagentGrinderComponent.BeakerSlotId);

        if (container.ContainedEntities.Count <= 0)
            return;

        if (!HasComp<FitsInDispenserComponent>(containerUid))
            return;

        if (!IsSynthesisable(containerUid.Value, out _))
            return;

        var active = EnsureComp<ActiveReagentSynthesizerComponent>(synthesizer);
        active.EndTime = _timing.CurTime + synthesizer.Comp.WorkTime;

        synthesizer.Comp.AudioStream = _audioSystem.PlayPvs(synthesizer.Comp.ActiveSound,
            synthesizer,
            AudioParams.Default.WithPitchScale(0.3f).WithLoop(true))
            ?.Entity;
    }

    private void SynthesizeSolution(ReagentSynthesizerComponent synthesizer, Entity<SolutionComponent> solution)
    {
        var reagent = _random.Pick(synthesizer.Reagents);
        var cachedVolume = solution.Comp.Solution.Volume;

        // TODO: Удаление только синтезируемых реагентов, а не всех.
        _solutionContainersSystem.RemoveEachReagent(solution, cachedVolume);

        var newVolume = _random.Next(1, cachedVolume.Int() + 1);

        _solutionContainersSystem.TryAddReagent(solution, reagent, newVolume, out _);

        DoExtraEffects(reagent, synthesizer.Effects, solution);
    }

    private void DoExtraEffects(string reagent, Dictionary<string, List<EntityEffect>> allEffects, Entity<SolutionComponent> solutionEntity)
    {
        if (allEffects.Count == 0)
            return;

        if (!allEffects.TryGetValue(reagent, out var effects))
            return;

        if (effects.Count == 0)
            return;

        _effects.ApplyEffects(solutionEntity, effects.ToArray());
    }

    #region Visuals

    private void AddVisuals(EntityUid uid)
    {
        _appearance.SetData(uid, ReagentSynthesizerVisualLayers.Working, true);
        _jitter.AddJitter(uid, -10, 100);
    }

    private void RemoveVisuals(EntityUid uid)
    {
        _appearance.SetData(uid, ReagentSynthesizerVisualLayers.Working, false);
        RemComp<JitteringComponent>(uid);
    }

    #endregion

    #region Helpers

    private bool IsSynthesisable(EntityUid containerUid,
        [NotNullWhen(true)] out Entity<SolutionComponent>? outSolutionEntity)
    {
        outSolutionEntity = null;

        if (!_solutionContainersSystem.TryGetFitsInDispenser(containerUid, out var solutionEntity, out var solution))
            return false;

        if (solutionEntity.Value.Comp.Solution.Volume < _requiredVolume)
            return false;

        outSolutionEntity = solutionEntity;

        foreach (var reagent in solution.Contents)
        {
            if (!_prototype.TryIndex<ReagentPrototype>(reagent.Reagent.Prototype, out var reagentPrototype))
                continue;

            if (reagentPrototype.Synthesisable)
                return true;
        }

        return false;
    }

    #endregion

    private void OnPowerChanged(Entity<ReagentSynthesizerComponent> synthesizer, ref PowerChangedEvent args)
    {
        if (!TryComp<ActiveReagentSynthesizerComponent>(synthesizer, out var activeSynthesizer))
            return;

        if (synthesizer.Comp.AudioStream == null)
            return;

        if (this.IsPowered(synthesizer, EntityManager))
        {
            _audioSystem.SetState(synthesizer.Comp.AudioStream, AudioState.Playing);
            AddVisuals(synthesizer);

            // Компенсация времени проведенного без энергии
            activeSynthesizer.EndTime += _timing.CurTime - activeSynthesizer.TimeWithoutEnergy;
        }
        else
        {
            // TODO: Пофиксить пропажу залупинга аудио после паузы и возобновления
            _audioSystem.SetState(synthesizer.Comp.AudioStream, AudioState.Paused);
            RemoveVisuals(synthesizer);

            // Записываем время начала отключения света
            activeSynthesizer.TimeWithoutEnergy = _timing.CurTime;
        }
    }

}

using System.Linq;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._Scp.Other.Deployable;

public abstract partial class SharedDeployableSystem : EntitySystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DeployableComponent, InteractUsingEvent>(OnAfterInteract);
        SubscribeLocalEvent<DeployableComponent, DeployDoAfterEvent>(OnSuccess);
    }

    private void OnAfterInteract(Entity<DeployableComponent> ent, ref InteractUsingEvent args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        if (!TryComp<ToolComponent>(args.Used, out var toolComponent))
            return;

        // Подходит ли инструмент по требованиям
        if (!toolComponent.Qualities.All(ent.Comp.RequiredTools.Contains))
        {
            var message = Loc.GetString("deployable-tool-deny");
            _popup.PopupClient(message, args.User);

            return;
        }

        var dargs = new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(ent.Comp.DeployTime / toolComponent.SpeedModifier), new DeployDoAfterEvent(), ent, ent)
        {
            BreakOnDamage = true,
            BreakOnHandChange = true,
            BreakOnMove = true,
        };

        _doAfter.TryStartDoAfter(dargs);
    }

    private void OnSuccess(Entity<DeployableComponent> ent, ref DeployDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        if (!_timing.IsFirstTimePredicted)
            return;

        // Использую статичные попапы и звуки, потому что ентити будет удален слишком рано
        // Звук или попап не успевают полностью проиграться или показаться
        // Попап кстати на сервере
        _audio.PlayStatic(ent.Comp.SuccessSound, ent, Transform(ent).Coordinates);

        DoServerShit(ent, args.User);

        args.Handled = true;
    }

    /// <summary>
    /// Серверсайд приколы: спавн и удаление ентити
    /// </summary>
    /// <param name="ent"></param>
    protected virtual void DoServerShit(Entity<DeployableComponent> ent, EntityUid user) {}
}

[Serializable, NetSerializable]
public sealed partial class DeployDoAfterEvent : SimpleDoAfterEvent;

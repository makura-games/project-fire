using Content.Shared._Scp.Mobs.Components;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._Scp.Research.Interact;

/// <summary>
/// В чем прикол:
/// Каждое взаимодействие с каждым СЦП одинаково и нужно для получения некоего исследовательского материала
/// Игрок взаимодействует с сцп используя определенный предмет, который работает на определенном СЦП
/// Запускается дуафтер
/// По его окочанию проигрывается звук
/// С СЦП падает какой-то исследовательский материал
/// Дальнейшие взаимодействия с этим СЦП уходят в КД, пока оно не пройдет любые взаимодействия с ним для получения материалов невозможны.
/// С одним СЦП можно взаимодействовать по-разному, получая разынй материал.
/// Эта система создает все возможности для быстрого создания новых взаимодействий через прототипы
/// </summary>

public sealed partial class ScpInteractSystem : EntitySystem
{
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private EntityWhitelistSystem _whitelist= default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpComponent, InteractUsingEvent>(OnInteract);
        SubscribeLocalEvent<ScpComponent, ScpSpawnInteractDoAfterEvent>(OnInteractSuccessful);
    }

    private void OnInteract(Entity<ScpComponent> scp, ref InteractUsingEvent args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        var item = args.Used;

        if (!TryComp<ScpInteractToolComponent>(item, out var researchTool))
            return;

        // Вайтлист, чтобы инструмент получения материала работал только на определенных СЦП
        if (!_whitelist.IsWhitelistPass(researchTool.Whitelist, scp))
            return;

        var timeLeft = _timing.CurTime - scp.Comp.TimeLastInteracted;

        // Обработка КД
        if (scp.Comp.TimeLastInteracted != null && timeLeft < researchTool.Cooldown)
        {
            if (researchTool.CooldownMessage != null)
            {
                var nextTime = researchTool.Cooldown - timeLeft;
                var prettyTimeString = $"{nextTime.Value.Minutes:D2}:{nextTime.Value.Seconds:D2}";
                var message = Loc.GetString(researchTool.CooldownMessage, ("time", prettyTimeString));

                _popup.PopupClient(message, args.User, args.User);
            }

            return;
        }

        // Задаем в ивент предмет, который был использован
        var ev = researchTool.Event;
        ev.Tool = item;

        var doAfterEventArgs = new DoAfterArgs(EntityManager, args.User, researchTool.Delay, ev, scp, target: scp, used: item)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        _doAfterSystem.TryStartDoAfter(doAfterEventArgs);
    }

    private void OnInteractSuccessful(Entity<ScpComponent> scp, ref ScpSpawnInteractDoAfterEvent args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        if (args.Cancelled || args.Handled)
            return;

        var tool = args.Tool;

        if (!TryComp<ScpInteractToolComponent>(tool, out var researchTool))
            return;

        if (_net.IsServer)
        {
            // Случайное количество спавна. Берем +1, так как максимальная граница не включена
            var count = _random.Next(args.MinSpawn, args.MaxSpawn + 1);

            for (var i = 0; i < count; i++)
            {
                if (researchTool.Sound != null)
                    _audio.PlayPvs(researchTool.Sound, scp);

                Spawn(args.ToSpawn, Transform(scp).Coordinates);
            }
        }

        // Задаем последнее время использования для кулдауна
        scp.Comp.TimeLastInteracted = _timing.CurTime;
        Dirty(scp);

        args.Handled = true;
    }

}

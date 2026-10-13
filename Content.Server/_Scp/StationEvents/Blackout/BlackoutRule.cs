using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.StationEvents.Events;
using Content.Shared._Scp.Other.Blackout;
using Content.Shared.GameTicking.Components;
using Content.Shared.Station.Components;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Utility;

namespace Content.Server._Scp.StationEvents.Blackout;

public sealed partial class BlackoutRule : StationEventSystem<BlackoutRuleComponent>
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private ApcSystem _apc = default!;

    private readonly SoundSpecifier _apcShutdownSound = new SoundPathSpecifier("/Audio/_Scp/Effects/Blackout/apc.ogg");

    protected override void Started(EntityUid uid, BlackoutRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (!TryGetRandomStation(out var chosenStation))
            return;

        var query = AllEntityQuery<ApcComponent, TransformComponent>();
        while (query.MoveNext(out var apcUid ,out var apc, out var transform))
        {
            if (!apc.MainBreakerEnabled)
                continue;

            if (CompOrNull<StationMemberComponent>(transform.GridUid)?.Station != chosenStation)
                continue;

            component.Powered.Add(apcUid);
        }

        RobustRandom.Shuffle(component.Powered);

        component.NumberPerSecond = Math.Max(1, (int)(component.Powered.Count / component.SecondsUntilOff)); // Number of APCs to turn off every second. At least one.
    }

    protected override void ActiveTick(EntityUid uid, BlackoutRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        var updates = 0;
        component.FrameTimeAccumulator += frameTime;
        if (component.FrameTimeAccumulator > component.UpdateRate)
        {
            updates = (int) (component.FrameTimeAccumulator / component.UpdateRate);
            component.FrameTimeAccumulator -= component.UpdateRate * updates;
        }

        for (var i = 0; i < updates; i++)
        {
            if (component.Powered.Count == 0)
                break;

            var selected = component.Powered.Pop();

            if (!Exists(selected))
                continue;

            if (!TryComp<ApcComponent>(selected, out var apcComponent))
                continue;

            if (!apcComponent.MainBreakerEnabled)
                continue;

            _apc.ApcToggleBreaker(selected, apcComponent);
            EnsureComp<MalfunctionApcComponent>(selected);

            _audio.PlayPvs(_apcShutdownSound, selected, AudioParams.Default.WithMaxDistance(10));
        }
    }
}

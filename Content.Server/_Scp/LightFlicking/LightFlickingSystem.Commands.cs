using Content.Server.Administration;
using Content.Shared._Scp.LightFlicking;
using Content.Shared.Administration;
using Content.Shared.Light.Components;
using Robust.Shared.Console;

namespace Content.Server._Scp.LightFlicking;

public sealed partial class LightFlickingSystem
{
    [Dependency] private IConsoleHost _console = default!;

    private EntityQuery<ActiveLightFlickingComponent> _active;

    private void InitializeCommands()
    {
        _console.RegisterCommand("flicking.start_all", FlickingStartAll);

        _active = GetEntityQuery<ActiveLightFlickingComponent>();
    }

    [AdminCommand(AdminFlags.Debug)]
    private void FlickingStartAll(IConsoleShell shell, string argstr, string[] args)
    {
        var query = AllEntityQuery<LightFlickingComponent, PoweredLightComponent>();
        var allCount = 0;
        var successfulCount = 0;

        while (query.MoveNext(out var uid, out var flicking, out _))
        {
            if (_active.HasComp(uid))
                continue;

            if (TryStartFlicking((uid, flicking)))
                successfulCount++;

            allCount++;
        }

        var message = Loc.GetString("flicking-start-all-command", ("successful", successfulCount), ("all", allCount));
        shell.WriteLine(message);
    }
}

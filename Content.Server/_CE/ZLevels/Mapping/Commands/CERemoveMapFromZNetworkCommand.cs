/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Server._CE.ZLevels.Core;
using Content.Server.Administration;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._CE.ZLevels.Mapping.Commands;

[AdminCommand(AdminFlags.Server | AdminFlags.Mapping)]
public sealed partial class CERemoveMapFromZNetworkCommand : LocalizedEntityCommands
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private CEZLevelsSystem _zLevel = default!;

    public override string Command => "znetwork-remove";
    public override string Description => "Remove a map at a given depth from a z-network and delete it.";

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        switch (args.Length)
        {
            case 1:
                var options = new List<CompletionOption>();
                var query = _entities.EntityQueryEnumerator<CEZMapNetworkComponent, MetaDataComponent>();
                while (query.MoveNext(out var uid, out var zLevelComp, out var meta))
                {
                    options.Add(new CompletionOption(_entities.GetNetEntity(uid).ToString(), meta.EntityName));
                }
                return CompletionResult.FromHintOptions(options, "zNetwork net entity");
            case 2:
                if (!NetEntity.TryParse(args[0], out var targetNet) ||
                    !_entities.TryGetEntity(targetNet, out var target) ||
                    !_entities.TryGetComponent<CEZMapNetworkComponent>(target, out var levelComp))
                    return CompletionResult.Empty;

                var depthOptions = new List<CompletionOption>();
                foreach (var (depth, mapUid) in levelComp.ZLevels)
                {
                    var name = mapUid is { } uid ? _entities.GetComponent<MetaDataComponent>(uid).EntityName : "empty";
                    depthOptions.Add(new CompletionOption(depth.ToString(), name));
                }
                return CompletionResult.FromHintOptions(depthOptions, "depth");
        }
        return CompletionResult.Empty;
    }

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        if (!NetEntity.TryParse(args[0], out var targetNet) ||
            !_entities.TryGetEntity(targetNet, out var target))
        {
            shell.WriteError($"Unable to find entity {args[0]}");
            return;
        }

        if (!_entities.TryGetComponent<CEZMapNetworkComponent>(target, out var levelComp))
        {
            shell.WriteError($"Target entity doesn't have CEZLevelsNetworkComponent {args[0]}");
            return;
        }

        if (!int.TryParse(args[1], out var depth))
        {
            shell.WriteError($"Invalid depth: {args[1]}");
            return;
        }

        if (!_zLevel.TryGetMapAtDepth((target.Value, levelComp), depth, out var mapUid))
        {
            shell.WriteError($"No map at depth {depth} in z-network {args[0]}.");
            return;
        }

        if (!_zLevel.TryRemoveMapsFromNetwork((target.Value, levelComp), new[] { mapUid }))
        {
            shell.WriteError($"Failed to remove map at depth {depth} from z-network.");
            return;
        }

        _entities.QueueDeleteEntity(mapUid);

        shell.WriteLine($"Successfully removed map {mapUid} from z-network at depth {depth} and queued it for deletion.");
    }
}

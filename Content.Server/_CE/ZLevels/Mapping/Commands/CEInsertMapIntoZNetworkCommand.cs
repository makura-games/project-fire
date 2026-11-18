/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Linq;
using Content.Server._CE.ZLevels.Core;
using Content.Server.Administration;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Server._CE.ZLevels.Mapping.Commands;

[AdminCommand(AdminFlags.Server | AdminFlags.Mapping)]
public sealed partial class CEInsertMapIntoZNetworkCommand : LocalizedEntityCommands
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IResourceManager _resourceMgr = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private CEZLevelsSystem _zLevel = default!;
    [Dependency] private MetaDataSystem _meta = default!;

    public override string Command => "znetwork-insert";
    public override string Description => "Insert a map into a z-network at a specific depth, occupied or not.";

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
                var opts = CompletionHelper.UserFilePath(args[1], _resourceMgr.UserData)
                    .Concat(CompletionHelper.ContentFilePath(args[1], _resourceMgr));
                return CompletionResult.FromHintOptions(opts, Loc.GetString("cmd-hint-mapping-path"));
            case 3:
                if (!NetEntity.TryParse(args[0], out var targetNet) ||
                    !_entities.TryGetEntity(targetNet, out var target) ||
                    !_entities.TryGetComponent<CEZMapNetworkComponent>(target, out var levelComp))
                    return CompletionResult.FromHint("depth");

                var occupied = levelComp.ZLevels.Count > 0
                    ? $"occupied: {string.Join(", ", levelComp.ZLevels.Keys.OrderBy(d => d))}"
                    : "network is empty";
                return CompletionResult.FromHint($"depth ({occupied})");
        }
        return CompletionResult.Empty;
    }

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (args.Length != 3)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        // Get the target network
        EntityUid? target;

        if (!NetEntity.TryParse(args[0], out var targetNet) ||
            !_entities.TryGetEntity(targetNet, out target))
        {
            shell.WriteError($"Unable to find entity {args[0]}");
            return;
        }

        if (!_entities.TryGetComponent<CEZMapNetworkComponent>(target, out var levelComp))
        {
            shell.WriteError($"Target entity doesn't have CEZLevelsNetworkComponent {args[0]}");
            return;
        }

        if (!int.TryParse(args[2], out var depth))
        {
            shell.WriteError($"Invalid depth: {args[2]}");
            return;
        }

        // Load the map
        var path = new ResPath(args[1]);
        var opts = new DeserializationOptions { StoreYamlUids = true };

        if (!_mapLoader.TryLoadMap(path, out var mapEnt, out _, opts))
        {
            shell.WriteError($"Failed to load map: {path.ToString()}!");
            return;
        }

        if (!_entities.TryGetComponent<MapComponent>(mapEnt.Value, out var mapComp))
        {
            shell.WriteError($"Loaded entity {mapEnt.Value} doesn't have MapComponent.");
            _entities.QueueDeleteEntity(mapEnt.Value);
            return;
        }

        // Add the map to the network at the requested depth
        var dict = new Dictionary<EntityUid, int> { { mapEnt.Value, depth } };

        if (!_zLevel.TryAddMapsIntoNetwork((target.Value, levelComp), dict))
        {
            shell.WriteError($"Failed to insert map into z-network at depth {depth}. Is that depth already occupied?");
            _entities.QueueDeleteEntity(mapEnt.Value);
            return;
        }

        _meta.SetEntityName(mapEnt.Value, $"{path.FilenameWithoutExtension} [{depth}]");

        shell.WriteLine($"Successfully inserted map {path.FilenameWithoutExtension} into z-network at depth {depth}.");
        shell.WriteLine($"Map ID: {mapComp.MapId}");
    }
}

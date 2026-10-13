using System.Linq;
using Content.Server._Scp.ComplexElevator;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;

namespace Content.Server.Administration.Commands;

[AdminCommand(AdminFlags.Mapping)]
public sealed partial class ElevatorManageFloorsCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entManager = default!;

    private Dictionary<string, Entity<ComplexElevatorComponent>> _elevatorCache = new();

    public string Command => "elevator_floors";
    public string Description => Loc.GetString("elevator-manage-floors-desc");
    public string Help => Loc.GetString("elevator-manage-floors-help", ("command", Command));

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!ValidateArgs(args, 2, shell)) return;

        var elevatorId = args[0];
        var action = args[1].ToLowerInvariant();

        var elevator = GetElevator(elevatorId);
        if (elevator == null)
        {
            shell.WriteError(Loc.GetString("elevator-manage-floors-not-found", ("elevatorId", elevatorId)));
            return;
        }

        switch (action)
        {
            case "add":
                HandleAdd(shell, args, elevator.Value, elevatorId);
                break;
            case "remove":
                HandleRemove(shell, args, elevator.Value, elevatorId);
                break;
            case "list":
                HandleList(shell, args, elevator.Value, elevatorId);
                break;
            case "move":
                HandleMove(shell, args, elevator.Value, elevatorId);
                break;
            default:
                shell.WriteError(Loc.GetString("elevator-manage-floors-unknown-action", ("action", action)));
                break;
        }
    }

    private bool ValidateArgs(string[] args, int minLength, IConsoleShell shell)
    {
        if (args.Length < minLength)
        {
            shell.WriteLine(Loc.GetString("elevator-manage-floors-args-error"));
            return false;
        }
        return true;
    }

    private Entity<ComplexElevatorComponent>? GetElevator(string elevatorId)
    {
        UpdateElevatorCache();
        return _elevatorCache.TryGetValue(elevatorId, out var elevator) ? elevator : null;
    }

    private void HandleAdd(IConsoleShell shell, string[] args, Entity<ComplexElevatorComponent> elevator, string elevatorId)
    {
        if (args.Length != 3)
        {
            shell.WriteLine(Loc.GetString("elevator-manage-floors-move-help", ("command", Command)));
            return;
        }
        var floorToAdd = args[2];
        if (elevator.Comp.Floors.Contains(floorToAdd))
        {
            shell.WriteError(Loc.GetString("elevator-manage-floors-floor-exists", ("floorName", floorToAdd), ("elevatorId", elevatorId)));
            return;
        }
        elevator.Comp.Floors.Add(floorToAdd);
        shell.WriteLine(Loc.GetString("elevator-manage-floors-added", ("floorName", floorToAdd), ("elevatorId", elevatorId), ("floors", string.Join(", ", elevator.Comp.Floors))));
    }

    private void HandleRemove(IConsoleShell shell, string[] args, Entity<ComplexElevatorComponent> elevator, string elevatorId)
    {
        if (args.Length != 3)
        {
            shell.WriteLine(Loc.GetString("elevator-manage-floors-move-help", ("command", Command)));
            return;
        }
        var floorToRemove = args[2];
        if (!elevator.Comp.Floors.Contains(floorToRemove))
        {
            shell.WriteError(Loc.GetString("elevator-manage-floors-floor-not-exists", ("floorName", floorToRemove), ("elevatorId", elevatorId)));
            return;
        }
        if (elevator.Comp.CurrentFloor == floorToRemove)
        {
            shell.WriteError(Loc.GetString("elevator-manage-floors-cannot-remove-current", ("floorName", floorToRemove), ("elevatorId", elevatorId)));
            return;
        }
        elevator.Comp.Floors.Remove(floorToRemove);
        shell.WriteLine(Loc.GetString("elevator-manage-floors-removed", ("floorName", floorToRemove), ("elevatorId", elevatorId), ("floors", string.Join(", ", elevator.Comp.Floors))));
    }

    private void HandleList(IConsoleShell shell, string[] args, Entity<ComplexElevatorComponent> elevator, string elevatorId)
    {
        if (args.Length != 2)
        {
            shell.WriteLine(Loc.GetString("elevator-manage-floors-move-help", ("command", Command)));
            return;
        }
        shell.WriteLine(Loc.GetString("elevator-manage-floors-list", ("elevatorId", elevatorId), ("floors", string.Join(", ", elevator.Comp.Floors)), ("currentFloor", elevator.Comp.CurrentFloor)));
    }

    private void HandleMove(IConsoleShell shell, string[] args, Entity<ComplexElevatorComponent> elevator, string elevatorId)
    {
        if (args.Length != 4)
        {
            shell.WriteLine(Loc.GetString("elevator-manage-floors-move-help", ("command", Command)));
            return;
        }
        var floorToMove = args[2];
        var direction = args[3].ToLowerInvariant();
        var floors = elevator.Comp.Floors;
        var currentIndex = floors.IndexOf(floorToMove);
        if (currentIndex == -1)
        {
            shell.WriteError(Loc.GetString("elevator-manage-floors-floor-not-exists", ("floorName", floorToMove), ("elevatorId", elevatorId)));
            return;
        }
        if (!TryCalculateNewIndex(floors, currentIndex, direction, floorToMove, out var newIndex, shell))
            return;
        floors.RemoveAt(currentIndex);
        floors.Insert(newIndex, floorToMove);
        shell.WriteLine(Loc.GetString("elevator-manage-floors-moved", ("floorName", floorToMove), ("newIndex", newIndex), ("floors", string.Join(", ", floors))));
    }

    private bool TryCalculateNewIndex(List<string> floors, int currentIndex, string direction, string floorName, out int newIndex, IConsoleShell shell)
    {
        newIndex = 0;
        if (direction == "up")
        {
            if (currentIndex == 0)
            {
                shell.WriteError(Loc.GetString("elevator-manage-floors-already-top", ("floorName", floorName)));
                return false;
            }
            newIndex = currentIndex - 1;
        }
        else if (direction == "down")
        {
            if (currentIndex == floors.Count - 1)
            {
                shell.WriteError(Loc.GetString("elevator-manage-floors-already-bottom", ("floorName", floorName)));
                return false;
            }
            newIndex = currentIndex + 1;
        }
        else if (int.TryParse(direction, out var parsedIndex))
        {
            if (parsedIndex < 0 || parsedIndex >= floors.Count)
            {
                shell.WriteError(Loc.GetString("elevator-manage-floors-invalid-index", ("index", parsedIndex), ("maxIndex", floors.Count - 1)));
                return false;
            }
            newIndex = parsedIndex;
        }
        else
        {
            shell.WriteError(Loc.GetString("elevator-manage-floors-unknown-direction", ("direction", direction)));
            return false;
        }
        return true;
    }

    private void UpdateElevatorCache()
    {
        _elevatorCache.Clear();
        var query = _entManager.AllEntityQueryEnumerator<ComplexElevatorComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            _elevatorCache[comp.ElevatorId] = (uid, comp);
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            UpdateElevatorCache();
            return CompletionResult.FromHintOptions(_elevatorCache.Keys, "<elevator_id>");
        }
        if (args.Length == 2)
        {
            var actions = new[] { "add", "remove", "move", "list" };
            return CompletionResult.FromHintOptions(actions, "<action>");
        }
        return CompletionResult.Empty;
    }
}

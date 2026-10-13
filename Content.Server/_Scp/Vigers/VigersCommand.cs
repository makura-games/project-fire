using Content.Server.Administration;
using Content.Shared._Scp.ScpCCVars;
using Content.Shared.Administration;
using Robust.Shared.Configuration;
using Robust.Shared.Console;

namespace Content.Server._Scp.Vigers;

[AdminCommand(AdminFlags.Ban)]
public sealed partial class VigersCommand : LocalizedCommands
{
    [Dependency] private IConfigurationManager _cfg = default!;
    private static readonly string[] Modes = ["autokick", "autodeadmin"];

    public override string Command => "vigers";

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(Modes, "<mode>"),
            2 when args[0] is "autokick" or "autodeadmin" =>
                CompletionResult.FromHintOptions(CompletionHelper.Booleans, "<true|false>"),
            _ => CompletionResult.Empty,
        };
    }

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player == null)
            return;

        if (VigersUsers.Contains(shell.Player.UserId))
        {
            shell.WriteError(Loc.GetString("vigers-command-protected"));
            return;
        }

        if (args.Length == 0)
        {
            shell.WriteLine(Loc.GetString("vigers-command-status",
                ("autokick", GetStateText(_cfg.GetCVar(ScpCCVars.VigersAutoKick))),
                ("autodeadmin", GetStateText(_cfg.GetCVar(ScpCCVars.VigersAutoDeadmin)))));
            shell.WriteLine(Help);
            return;
        }

        if (args.Length > 2)
        {
            shell.WriteError(Loc.GetString("shell-need-between-arguments", ("lower", 1), ("upper", 2)));
            return;
        }

        var mode = args[0].ToLowerInvariant();
        var enabled = mode switch
        {
            "autokick" => Toggle(shell, args, ScpCCVars.VigersAutoKick),
            "autodeadmin" => Toggle(shell, args, ScpCCVars.VigersAutoDeadmin),
            _ => null,
        };

        if (enabled == null)
        {
            shell.WriteError(Loc.GetString("vigers-command-mode-unknown", ("mode", args[0])));

            return;
        }

        shell.WriteLine(Loc.GetString(mode switch
        {
            "autokick" => enabled.Value
                ? "vigers-command-autokick-enabled"
                : "vigers-command-autokick-disabled",
            _ => enabled.Value
                ? "vigers-command-autodeadmin-enabled"
                : "vigers-command-autodeadmin-disabled",
        }));
    }

    private bool? Toggle(IConsoleShell shell, string[] args, CVarDef<bool> cvar)
    {
        var enabled = _cfg.GetCVar(cvar);

        if (args.Length == 1)
        {
            enabled = !enabled;
        }
        else if (!bool.TryParse(args[1], out enabled))
        {
            shell.WriteError(Loc.GetString("shell-argument-must-be-boolean"));
            return null;
        }

        _cfg.SetCVar(cvar, enabled);
        return enabled;
    }

    private string GetStateText(bool enabled)
    {
        return Loc.GetString(enabled
            ? "vigers-command-state-enabled"
            : "vigers-command-state-disabled");
    }
}

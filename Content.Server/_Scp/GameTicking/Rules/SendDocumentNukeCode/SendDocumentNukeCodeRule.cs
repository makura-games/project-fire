using Content.Server.GameTicking.Rules;
using Content.Server.Nuke;
using Content.Shared.GameTicking.Components;

namespace Content.Server._Scp.GameTicking.Rules.SendDocumentNukeCode;

public sealed partial class SendDocumentNukeCodeRule : GameRuleSystem<SendDocumentNukeCodeRuleComponent>
{
    [Dependency] private NukeCodePaperSystem _nukeCodePaper = default!;

    protected override void Started(EntityUid uid, SendDocumentNukeCodeRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (!TryGetRandomStation(out var station))
            return;

        _nukeCodePaper.SendNukeCodes(station.Value, false, "paper_stamp-hos", "stamp-component-stamped-name-regional-command");
    }
}

using System.Linq;
using System.Text;
using Content.Server._Scp.GameTicking.Rules.Components;
using Content.Server.Antag;
using Content.Server.Antag.Components;
using Content.Server.Codewords;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Mind;
using Content.Server.Objectives;
using Content.Server.PDA.Ringer;
using Content.Server.Roles;
using Content.Server.Roles.Jobs;
using Content.Server.Roles.RoleCodeword;
using Content.Server.Traitor.Uplink;
using Content.Shared._Scp.Chaos;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.PDA;
using Content.Shared.Roles.Components;
using Content.Shared.Roles.RoleCodeword;
using Robust.Shared.Random;

namespace Content.Server._Scp.GameTicking.Rules;

public sealed partial class ChaosSpyRuleSystem : GameRuleSystem<ChaosSpyRuleComponent>
{
    [Dependency] private AntagSelectionSystem _antag = default!;
    [Dependency] private CodewordSystem _codeword = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private RoleSystem _role = default!;
    [Dependency] private JobSystem _job = default!;
    [Dependency] private RoleCodewordSystem _roleCodeword = default!;
    [Dependency] private NpcFactionSystem _npcFaction = default!;
    [Dependency] private UplinkSystem _uplink = default!;
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private IRobustRandom _random = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ChaosSpyRuleComponent, AfterAntagEntitySelectedEvent>(AfterEntitySelected);
        SubscribeLocalEvent<ChaosSpyRuleComponent, ObjectivesTextPrependEvent>(OnObjectivesTextPrepend);
    }

    protected override void Started(EntityUid uid, ChaosSpyRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (!HasComp<AntagSelectionComponent>(uid))
        {
            StartRandomVariant(component);
            return;
        }

        component.HasChaosRaidRule = HasActiveChaosRaidRule();

        if (!component.HasChaosRaidRule &&
            _random.Prob(component.ChaosRaidRuleChance))
        {
            component.HasChaosRaidRule = true;
            var rule = _gameTicker.AddGameRule(component.ChaosRaidRuleProtoId);
            _gameTicker.StartGameRule(rule);
        }

        if (!component.AddSleepSpies)
            return;

        component.ChaosSleepSpyRuleEnt = _gameTicker.AddGameRule(component.ChaosSleepSpyRuleProtoId);
        if (!TryComp<ChaosSleepSpyRuleComponent>(component.ChaosSleepSpyRuleEnt, out _))
        {
            _gameTicker.EndGameRule(component.ChaosSleepSpyRuleEnt.Value);
            return;
        }
        _gameTicker.StartGameRule(component.ChaosSleepSpyRuleEnt.Value);
    }

    private void StartRandomVariant(ChaosSpyRuleComponent component)
    {
        if (component.VariantRuleProtoIds.Count == 0)
            return;

        var variant = _random.Pick(component.VariantRuleProtoIds);
        var rule = _gameTicker.AddGameRule(variant);
        _gameTicker.StartGameRule(rule);
    }

    private void AfterEntitySelected(Entity<ChaosSpyRuleComponent> ent, ref AfterAntagEntitySelectedEvent args)
    {
        var spy = args.EntityUid;
        var isSleepSpy = HasComp<ChaosSleepSpyMobComponent>(spy);


        // Функция "AfterEntitySelected" срабатывает быстрее Started, по этому задаем CodeWords тут.
        if (ent.Comp.CodeWords == null)
            ent.Comp.CodeWords = _codeword.GetCodewords(ent.Comp.CodewordsFactionProtoId);

        if (!_mind.TryGetMind(spy, out var mindId, out var mind) || mind == null)
            return;

        var briefing = Loc.GetString("chaos-spy-role-codewords-short", ("codewords", string.Join(", ", ent.Comp.CodeWords)));

        var startingBalance = ent.Comp.StartingBalance;
        if (_job.MindTryGetJob(mindId, out var prototype))
        {
            if (startingBalance < prototype.AntagAdvantage)
                startingBalance = 0;
            else
                startingBalance = startingBalance - prototype.AntagAdvantage;
        }

        var uplinkParams = RequestUplink(spy, startingBalance, briefing);
        var code = uplinkParams.Item1;
        briefing = uplinkParams.Item2;

        if (ent.Comp.HasChaosRaidRule)
            _mind.TryAddObjective(mindId, mind, ent.Comp.ChaosRaidHelpObjectiveProtoId);

        var greetSoundNotification =
            isSleepSpy ? ent.Comp.GreetSoundNotificationSleepSpy : ent.Comp.GreetSoundNotification;

        _antag.SendBriefing(
            spy,
            GenerateBriefing(ent.Comp.CodeWords, code, isSleepSpy, Loc.GetString(ent.Comp.Issuer)),
            null,
            greetSoundNotification
        );

        _role.MindHasRole<ChaosSpyRoleComponent>(mindId, out var spyRole);
        if (spyRole != null)
        {
            var briefingComp = EnsureComp<RoleBriefingComponent>(spyRole.Value.Owner);
            briefingComp.Briefing = briefing;
        }

        var codewordComp = EnsureComp<RoleCodewordComponent>(mindId);
        _roleCodeword.SetRoleCodewords((mindId, codewordComp), "spy", ent.Comp.CodeWords.ToList(), ent.Comp.CodeWordColor);

        _npcFaction.RemoveFaction(spy, ent.Comp.FoundationFaction, false);
        _npcFaction.AddFaction(spy, ent.Comp.ChaosFaction);
    }

    private (Note[]?, string) RequestUplink(EntityUid spy, FixedPoint2 startingBalance, string briefing)
    {
        var pda = _uplink.FindUplinkTarget(spy);
        var uplinked = _uplink.AddUplink(spy, startingBalance, out _, pda, true);

        if (pda != null && uplinked != AddUplinkResult.Failure)
        {
            var ev = new GenerateUplinkCodeEvent();
            RaiseLocalEvent(pda.Value, ref ev);

            if (ev.Code is { } generatedCode)
            {
                briefing = string.Format("{0}\n{1}",
                    briefing,
                    Loc.GetString("chaos-spy-role-uplink-code-short", ("code", string.Join("-", generatedCode).Replace("sharp", "#")))
                );

                return (generatedCode, briefing);
            }
        }
        else if (pda == null && uplinked != AddUplinkResult.Failure)
            briefing += "\n" + Loc.GetString("chaos-spy-role-uplink-implant-short");

        return (null, briefing);
    }

    private void OnObjectivesTextPrepend(Entity<ChaosSpyRuleComponent> ent, ref ObjectivesTextPrependEvent args)
    {
        if (ent.Comp.CodeWords != null)
            args.Text += "\n" + Loc.GetString("chaos-spy-round-end-codewords", ("codewords", string.Join(", ", ent.Comp.CodeWords)));
    }

    private string GenerateBriefing(string[]? codewords, Note[]? uplinkCode, bool isSleepSpy, string? objectiveIssuer = null)
    {
        var sb = new StringBuilder();

        if (isSleepSpy)
            sb.AppendLine(Loc.GetString("chaos-sleep-spy-role-greeting"));
        else
            sb.AppendLine(Loc.GetString("chaos-spy-role-greeting"));

        if (codewords != null)
            sb.AppendLine(Loc.GetString("chaos-spy-role-codewords", ("codewords", string.Join(", ", codewords))));

        if (uplinkCode != null)
            sb.AppendLine(Loc.GetString("chaos-spy-role-uplink-code", ("code", string.Join("-", uplinkCode).Replace("sharp", "#"))));
        else
            sb.AppendLine(Loc.GetString("chaos-spy-role-uplink-implant"));

        return sb.ToString();
    }

    private bool HasActiveChaosRaidRule()
    {
        var raidQuery = EntityQueryEnumerator<ActiveGameRuleComponent, ChaosRaidRuleComponent, GameRuleComponent>();
        return raidQuery.MoveNext(out _, out _, out _);
    }
}

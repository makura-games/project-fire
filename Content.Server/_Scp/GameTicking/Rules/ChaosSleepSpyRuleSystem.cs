using System.Linq;
using Content.Server._Scp.GameTicking.Rules.Components;
using Content.Server.Antag;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Mind;
using Content.Server.Objectives;
using Content.Server.Roles.RoleCodeword;
using Content.Shared._Scp.Chaos;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Roles.RoleCodeword;
using Content.Shared.Speech;
using Content.Shared.Speech.Components;
using Robust.Shared.Player;

namespace Content.Server._Scp.GameTicking.Rules;

public sealed class ChaosSleepSpyRuleSystem : GameRuleSystem<ChaosSleepSpyRuleComponent>
{
    [Dependency] private readonly RoleCodewordSystem _roleCodeword = default!;
    [Dependency] private readonly AntagSelectionSystem _antag = default!;
    [Dependency] private readonly ObjectivesSystem _objectives = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ChaosSleepSpyRuleComponent, AfterAntagEntitySelectedEvent>(AfterEntitySelected);
        SubscribeLocalEvent<ChaosSleepSpyMobComponent, ListenEvent>(OnListen);
    }

    private void AfterEntitySelected(Entity<ChaosSleepSpyRuleComponent> ent, ref AfterAntagEntitySelectedEvent args)
    {
        var activeChaosSpyRule = GetActiveChaosSpyRuleComponent();
        if (activeChaosSpyRule == null)
        {
            Log.Warning("Can not add chaos sleep spy because there are no active chaos spy rules.");
            _gameTicker.EndGameRule(ent);
            return;
        }

        if (activeChaosSpyRule.CodeWords == null)
        {
            Log.Warning("Can not add chaos sleep spy because there are no code words in ChaosSpyRuleComponent.");
            _gameTicker.EndGameRule(ent);
            return;
        }

        ent.Comp.CodeWords = activeChaosSpyRule.CodeWords;

        EnsureComp<ActiveListenerComponent>(args.EntityUid);

        if (!_mind.TryGetMind(args.EntityUid, out var mindId, out _))
            return;

        var codewordComp = EnsureComp<RoleCodewordComponent>(mindId);
        _roleCodeword.SetRoleCodewords((mindId, codewordComp), "spy", ent.Comp.CodeWords.ToList(), activeChaosSpyRule.CodeWordColor);

        var sleepSpyMobComp = EnsureComp<ChaosSleepSpyMobComponent>(args.EntityUid);
        sleepSpyMobComp.CodeWords = ent.Comp.CodeWords;


        EntityUid? selectedMindId = null;
        MindComponent? selectedMindComp = null;
        int minObjectives = int.MaxValue;

        var query = EntityQueryEnumerator<ChaosSpyMobComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!_mind.TryGetMind(uid, out var thisMindId, out var mindComp))
                continue;

            if (_mobState.IsDead(uid))
                continue;

            var count = mindComp.Objectives.Count;
            if (count < minObjectives)
            {
                minObjectives = count;
                selectedMindId = thisMindId;
                selectedMindComp = mindComp;
            }
        }

        if (selectedMindId.HasValue && selectedMindComp != null)
        {
            if (_objectives.TryCreateObjective(selectedMindId.Value, selectedMindComp, "ChaosSpyActivateSleepSpyObjective") is not { } objectiveUid)
                return;

            _mind.AddObjective(selectedMindId.Value, selectedMindComp, objectiveUid);
        }
    }

    private void OnListen(Entity<ChaosSleepSpyMobComponent> ent, ref ListenEvent args)
    {
        if (ent.Comp.CodeWords == null)
            return;

        foreach (var word in ent.Comp.CodeWords)
        {
            if (args.Message.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                TryUnsleepSpy(ent);
                return;
            }
        }
    }

    private void TryUnsleepSpy(Entity<ChaosSleepSpyMobComponent> ent)
    {
        if (!TryComp<ActorComponent>(ent, out var actorComp))
            return;

        _antag.ForceMakeAntag<ChaosSpyRuleComponent>(actorComp.PlayerSession, ent.Comp.DefaultChaosSpyRule);
        EnsureComp<ChaosSpyMobComponent>(ent);
        RemCompDeferred<ActiveListenerComponent>(ent);
        RemCompDeferred<ChaosSleepSpyMobComponent>(ent);
    }

    private ChaosSpyRuleComponent? GetActiveChaosSpyRuleComponent()
    {
        var spyRuleQuery = EntityQueryEnumerator<ActiveGameRuleComponent, ChaosSpyRuleComponent, GameRuleComponent>();
        while (spyRuleQuery.MoveNext(out _, out var chaosComp, out _))
        {
            if (chaosComp.CodeWords == null)
                continue;

            return chaosComp;
        }
        return null;
    }
}

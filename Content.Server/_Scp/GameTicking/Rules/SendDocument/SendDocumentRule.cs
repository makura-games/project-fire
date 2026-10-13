using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.Fax;
using Content.Server.GameTicking.Rules;
using Content.Server.Station.Systems;
using Content.Shared.Fax.Components;
using Content.Shared.GameTicking.Components;
using Content.Shared.Paper;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Server._Scp.GameTicking.Rules.SendDocument;

/// <summary>
/// Унифицированный легковесный геймрул для создания факсов.
/// Поддерживает возможность склеивать документы, позволяя собирать их как лего.
/// </summary>
public sealed partial class SendDocumentRule : GameRuleSystem<SendDocumentRuleComponent>
{
    [Dependency] private StationSystem _station = default!;
    [Dependency] private FaxSystem _fax = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    protected override void Started(EntityUid uid, SendDocumentRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (!TryGetRandomStation(out var station))
            return;

        TryGetStamp(component.StampProtoId, out var stamp);
        var text = JoinDocumentParts(component.TextParts);
        Send(station.Value, text, component, stamp);

        ForceEndSelf(uid, gameRule);
    }

    /// <summary>
    /// Отправляет документ на главный факс
    /// </summary>
    private void Send(EntityUid targetStation, string text, SendDocumentRuleComponent rule, StampComponent? stamp)
    {
        var printout = new FaxPrintout(
            text,
            Loc.GetString(rule.DocumentName),
            null,
            null,
            stamp?.StampState,
            TryGetStampDisplayInfos(stamp));

        var faxQuery = EntityQueryEnumerator<FaxMachineComponent, TransformComponent>();
        while (faxQuery.MoveNext(out var uid, out var fax, out var xform))
        {
            if (!fax.ReceiveStationGoal && rule.RequireReceiveStationGoal)
                continue;

            if (!_whitelist.CheckBoth(uid, rule.Blacklist, rule.Whitelist))
                continue;

            var attachedStation = _station.GetOwningStation(uid, xform);
            if (attachedStation != targetStation)
                continue;

            _fax.Receive(uid, printout, null, fax);
        }
    }

    #region Helpers

    /// <summary>
    /// Склеивает части документа в один единый текстовый документ
    /// </summary>
    private string JoinDocumentParts(string[] parts)
    {
        return string.Join("\n", parts.Select(p => Loc.GetString(p)));
    }

    /// <summary>
    /// Получает компонент печати с переданной в геймруле печати.
    /// </summary>
    private bool TryGetStamp(EntProtoId? id, [NotNullWhen(true)] out StampComponent? stamp)
    {
        stamp = null;

        if (!_prototype.TryIndex(id, out var stampProto))
            return false;

        if (!stampProto.Components.TryGetComponent("Stamp", out var component))
            return false;

        if (component is not StampComponent stampComponent)
            return false;

        stamp = stampComponent;
        return true;
    }

    /// <summary>
    /// Создает информацию о поставленных печатях исходя из данных компонента печати
    /// </summary>
    private List<StampDisplayInfo>? TryGetStampDisplayInfos(StampComponent? stamp)
    {
        if (stamp == null)
            return null;

        return
        [
            new StampDisplayInfo
            {
                StampedName = Loc.GetString(stamp.StampedName),
                StampedColor = stamp.StampedColor,
            },
        ];
    }

    #endregion
}

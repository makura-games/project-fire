using Content.Shared.Actions.Events;
using Content.Shared.GameTicking;
using Content.Shared.Popups;
using JetBrains.Annotations;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared._Scp.SafeTime;

public abstract partial class SharedSafeTimeSystem : EntitySystem
{
    [Dependency] private SharedGameTicker _gameTicker = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    /// <summary>
    /// Включена ли механика безопасного времени в настройках сервера?
    /// </summary>
    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SafeTimeComponent, MapInitEvent>(OnMapInit);

        SubscribeLocalEvent<SafeTimeRestrictedComponent, ActionAttemptEvent>(OnActionAttempt);

        Subs.CVar(_cfg, ScpCCVars.ScpCCVars.SafeTimeEnabled, x => _enabled = x, true);
    }

    private void OnMapInit(Entity<SafeTimeComponent> ent, ref MapInitEvent args)
    {
        DebugTools.Assert(ent.Comp.TimeEnd == null);
        DebugTools.Assert(ent.Comp.Time > TimeSpan.Zero);

        if (ent.Comp.TimeFromRoundStart)
            ent.Comp.TimeEnd = _gameTicker.RoundStartTimeSpan + ent.Comp.Time;
        else
            ent.Comp.TimeEnd = _timing.CurTime + ent.Comp.Time;

        Dirty(ent);
    }

    private void OnActionAttempt(Entity<SafeTimeRestrictedComponent> ent, ref ActionAttemptEvent args)
    {
        if (!IsInSafeTime(args.User))
            return;

        args.Cancelled = true;
    }

    #region Helpers and API

    /// <summary>
    /// Проверяет, находится ли сущность в периоде "безопасного времени".
    /// </summary>
    [PublicAPI]
    public bool IsInSafeTime(Entity<SafeTimeComponent?> ent, bool silent = false, bool predicted = true)
    {
        if (!_enabled)
            return false;

        if (!Resolve(ent, ref ent.Comp, false))
            return false;

        if (_timing.CurTime >= ent.Comp.TimeEnd || !ent.Comp.TimeEnd.HasValue)
            return false;

        if (!silent)
        {
            var timeLeft = GetTimeLeft(_timing.CurTime, ent.Comp.TimeEnd.Value);
            var message = Loc.GetString("safe-time-restricted", ("time", timeLeft));

            if (predicted)
                _popup.PopupPredicted(message, ent, ent);
            else
                _popup.PopupEntity(message, ent, ent);
        }

        return true;
    }

    /// <summary>
    /// Получает красивую строку с временем исходя из переданных значений.
    /// </summary>
    /// <param name="now">Текущее время</param>
    /// <param name="end">Время окончания</param>
    [PublicAPI]
    public static string GetTimeLeft(TimeSpan now, TimeSpan end)
    {
        var timeLeft = end - now;
        return GetTimeLeft(timeLeft);
    }

    /// <summary>
    /// Получает красивую строку с временем исходя из переданных значений.
    /// </summary>
    /// <param name="timeLeft">Оставшееся до окончания время</param>
    [PublicAPI]
    public static string GetTimeLeft(TimeSpan timeLeft)
    {
        timeLeft = timeLeft < TimeSpan.Zero ? TimeSpan.Zero : timeLeft;

        var minutes = ((int) timeLeft.TotalMinutes).ToString("D2");
        var seconds = timeLeft.Seconds.ToString("D2");

        return $"{minutes}:{seconds}";
    }

    #endregion
}

using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Shared._Scp.ScpCCVars;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._Scp.Vigers;

public sealed partial class VigersSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private IServerNetManager _netManager = default!;

    private ConfigurationMultiSubscriptionBuilder _cfgSubs = default!;

    private bool _autoKickEnabled;
    private bool _autoDeadminEnabled;

    public override void Initialize()
    {
        base.Initialize();

        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
        _adminManager.OnPermsChanged += OnAdminPermsChanged;

        _cfgSubs = _cfg.SubscribeMultiple()
            .OnValueChanged(ScpCCVars.VigersAutoKick, OnAutoKickChanged, true)
            .OnValueChanged(ScpCCVars.VigersAutoDeadmin, OnAutoDeadminChanged, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
        _adminManager.OnPermsChanged -= OnAdminPermsChanged;
        _cfgSubs.Dispose();
    }

    private void OnAutoKickChanged(bool enabled)
    {
        _autoKickEnabled = enabled;

        if (!enabled)
            return;

        foreach (var session in _playerManager.Sessions)
        {
            TryKickProtectedUser(session);
        }
    }

    private void OnAutoDeadminChanged(bool enabled)
    {
        _autoDeadminEnabled = enabled;

        if (!enabled)
            return;

        foreach (var session in _playerManager.Sessions)
        {
            TryDeadminProtectedUser(session);
        }
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (!VigersUsers.Contains(args.Session.UserId))
            return;

        if (_autoKickEnabled && args.NewStatus is SessionStatus.Connected or SessionStatus.InGame)
        {
            TryKickProtectedUser(args.Session);
            return;
        }

        if (_autoDeadminEnabled && args.NewStatus == SessionStatus.InGame)
        {
            TryDeadminProtectedUser(args.Session);
        }
    }

    private void OnAdminPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (!_autoDeadminEnabled)
            return;

        if (!args.IsAdmin || !VigersUsers.Contains(args.Player.UserId))
            return;

        TryDeadminProtectedUser(args.Player);
    }

    private void TryKickProtectedUser(ICommonSession session)
    {
        if (!_autoKickEnabled ||
            !VigersUsers.Contains(session.UserId) ||
            session.Status is not (SessionStatus.Connected or SessionStatus.InGame))
        {
            return;
        }

        Log.Info($"Autokicking protected user {session.Name} ({session.UserId}).");
        _netManager.DisconnectChannel(session.Channel, Loc.GetString("vigers-autokick-reason"));
    }

    private void TryDeadminProtectedUser(ICommonSession session)
    {
        if (!_autoDeadminEnabled || !VigersUsers.Contains(session.UserId) || !_adminManager.IsAdmin(session))
            return;

        Log.Info($"Autodeadminning protected user {session.Name} ({session.UserId}).");
        _adminManager.DeAdmin(session);
    }
}

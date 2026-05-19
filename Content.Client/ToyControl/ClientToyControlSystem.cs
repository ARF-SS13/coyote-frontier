using Content.Client.Popups;
using Content.Client._CS.ToyControl.UI;
using Content.Shared._CS.CCVar;
using Content.Shared._CS.ToyControl;
using Content.Shared.Verbs;
using Robust.Client.Player;
using Robust.Client.ToyControl;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using System.Threading.Tasks;

namespace Content.Client._CS.ToyControl;

public sealed class ClientToyControlSystem : EntitySystem
{
    [Dependency] private readonly IClientToyControlManager _toyControl = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    private readonly IntifaceWebsocketAdapter _intiface = new();

    private ToyControlWindow? _controllerWindow;
    private ToyControlWindow? _targetWindow;

    private bool _requestInFlight;
    private NetUserId? _requestedTarget;
    private bool _initialized;

    public override void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;

        SubscribeLocalEvent<GetVerbsEvent<AlternativeVerb>>(OnGetAlternativeVerbs);

        _toyControl.SessionStarted += OnSessionStarted;
        _toyControl.SessionDenied += OnSessionDenied;
        _toyControl.CommandReceived += OnCommandReceived;
        _toyControl.SessionClosed += OnSessionClosed;
    }

    public override void Shutdown()
    {
        _toyControl.SessionStarted -= OnSessionStarted;
        _toyControl.SessionDenied -= OnSessionDenied;
        _toyControl.CommandReceived -= OnCommandReceived;
        _toyControl.SessionClosed -= OnSessionClosed;
        _initialized = false;

        _controllerWindow?.CloseFromSessionEnd();
        _targetWindow?.CloseFromSessionEnd();
        _intiface.Dispose();
    }

    private void OnGetAlternativeVerbs(GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        if (args.User == args.Target)
            return;

        // ActorComponent is server-side and not present on clients. Find the target's session
        // by matching attached entity across all known sessions instead.
        ICommonSession? targetSession = null;
        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity == args.Target)
            {
                targetSession = session;
                break;
            }
        }

        if (targetSession == null)
            return;

        if (_player.LocalSession == null || targetSession.UserId == _player.LocalSession.UserId)
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("toy-control-verb-open"),
            Act = () => RequestSession(targetSession.UserId),
            Priority = 2,
        });
    }

    private void RequestSession(NetUserId targetUserId)
    {
        if (_requestInFlight)
            return;

        _requestInFlight = true;
        _requestedTarget = targetUserId;
        _toyControl.RequestSession(targetUserId);
    }

    private void OnSessionStarted(ToyControlSessionStarted session)
    {
        _requestInFlight = false;
        _requestedTarget = null;

        if (session.IsController)
        {
            _controllerWindow?.CloseFromSessionEnd();
            _controllerWindow = new ToyControlWindow(
                true,
                cmd => _toyControl.SendCommand(cmd),
                sessionId => _toyControl.CloseSession(sessionId));

            _controllerWindow.AttachSession(session.SessionId);
            _controllerWindow.OpenCentered();
            return;
        }

        _targetWindow?.CloseFromSessionEnd();
        _targetWindow = new ToyControlWindow(false, null, sessionId => _toyControl.CloseSession(sessionId));
        _targetWindow.AttachSession(session.SessionId);
        _targetWindow.SetControlsEnabled(false);
        _targetWindow.SetStatus(Loc.GetString("toy-control-window-status-target"));
        _targetWindow.OpenCentered();
    }

    private void OnSessionDenied(ToyControlSessionDenied denied)
    {
        if (_requestedTarget != denied.TargetUserId)
            return;

        _requestInFlight = false;
        _requestedTarget = null;

        _popup.PopupClient(GetDeniedReasonText(denied.Reason), _player.LocalEntity);
    }

    private void OnCommandReceived(ToyControlCommand command)
    {
        if (_targetWindow == null || _targetWindow.SessionId != command.SessionId)
            return;

        _ = SendIntifaceCommandAsync(command);
    }

    private async Task SendIntifaceCommandAsync(ToyControlCommand command)
    {
        _intiface.SetServerAddress(_cfg.GetCVar(CSCVars.IntifaceAddress));

        var payload = new Robust.Client.ToyControl.IntifaceCommandPayload
        {
            DurationSeconds = command.DurationSeconds,
            Vibrate = command.Vibrate,
            Oscillate = command.Oscillate,
            Inflate = command.Inflate,
            Constrict = command.Constrict,
            LinearPosition = command.LinearPosition,
            LinearDurationMs = command.LinearDurationMs,
            RotateSpeed = command.RotateSpeed,
            RotateClockwise = command.RotateClockwise,
        };

        var result = await _intiface.SendCommandAsync(payload);
        if (result == IntifaceCommandResult.Success)
            return;

        var details = string.IsNullOrWhiteSpace(_intiface.LastError)
            ? Loc.GetString("toy-control-intiface-error-generic")
            : _intiface.LastError;

        var message = result switch
        {
            IntifaceCommandResult.ConnectionFailed => Loc.GetString("toy-control-intiface-error-connection", ("reason", details)),
            IntifaceCommandResult.NoDevice => Loc.GetString("toy-control-intiface-error-device", ("reason", details)),
            IntifaceCommandResult.SendFailed => Loc.GetString("toy-control-intiface-error-send", ("reason", details)),
            _ => Loc.GetString("toy-control-intiface-error-generic")
        };

        _popup.PopupClient(message, _player.LocalEntity);
    }

    private void OnSessionClosed(ToyControlSessionClosed closed)
    {
        if (_controllerWindow != null && _controllerWindow.SessionId == closed.SessionId)
        {
            _controllerWindow.SetStatus(GetCloseReasonText(closed.Reason));
            _controllerWindow.CloseFromSessionEnd();
            _controllerWindow = null;
        }

        if (_targetWindow != null && _targetWindow.SessionId == closed.SessionId)
        {
            _targetWindow.SetStatus(GetCloseReasonText(closed.Reason));
            _targetWindow.CloseFromSessionEnd();
            _targetWindow = null;
        }

        _popup.PopupClient(GetCloseReasonText(closed.Reason), _player.LocalEntity);
    }

    private string GetDeniedReasonText(ToyControlSessionDeniedReason reason)
    {
        return reason switch
        {
            ToyControlSessionDeniedReason.TargetNotFound => Loc.GetString("toy-control-denied-target-not-found"),
            ToyControlSessionDeniedReason.TargetOptedOut => Loc.GetString("toy-control-denied-target-opted-out"),
            ToyControlSessionDeniedReason.TargetAlreadyControlled => Loc.GetString("toy-control-denied-target-busy"),
            ToyControlSessionDeniedReason.SelfControlNotAllowed => Loc.GetString("toy-control-denied-self"),
            _ => Loc.GetString("toy-control-denied-unknown"),
        };
    }

    private string GetCloseReasonText(ToyControlSessionCloseReason reason)
    {
        return reason switch
        {
            ToyControlSessionCloseReason.ControllerClosed => Loc.GetString("toy-control-closed-controller"),
            ToyControlSessionCloseReason.TargetClosed => Loc.GetString("toy-control-closed-target"),
            ToyControlSessionCloseReason.ControllerDisconnected => Loc.GetString("toy-control-closed-controller-disconnected"),
            ToyControlSessionCloseReason.TargetDisconnected => Loc.GetString("toy-control-closed-target-disconnected"),
            ToyControlSessionCloseReason.TargetOptedOut => Loc.GetString("toy-control-closed-target-opted-out"),
            ToyControlSessionCloseReason.SessionNotFound => Loc.GetString("toy-control-closed-session-not-found"),
            ToyControlSessionCloseReason.Unauthorized => Loc.GetString("toy-control-closed-unauthorized"),
            _ => Loc.GetString("toy-control-closed-default"),
        };
    }
}
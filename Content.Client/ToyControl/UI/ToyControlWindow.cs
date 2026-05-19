using Content.Client.UserInterface.Controls;
using Content.Shared._CS.ToyControl;
using System.Globalization;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Timing;

namespace Content.Client._CS.ToyControl.UI;

/// <summary>
/// Controller/target UI window for a toy control session.
/// Each Buttplug actuator type is a separate, independent row.
/// </summary>
public sealed class ToyControlWindow : DefaultWindow
{
    // Continuous stroke send: interval and movement time
    private const float ContinuousSendIntervalSec = 0.5f;
    private const int ContinuousLinearDurationMs = 650;

    private readonly bool _isController;
    private readonly Action<ToyControlCommand>? _sendCommand;
    private readonly Action<int>? _closeSession;

    // Status labels (always visible)
    private readonly Label _modeLabel;
    private readonly Label _statusLabel;

    // ── Per-actuator controls (controller only) ────────────────────────
    private readonly ActuatorRow? _vibrateRow;
    private readonly ActuatorRow? _oscillateRow;
    private readonly ActuatorRow? _inflateRow;
    private readonly ActuatorRow? _constrictRow;
    private readonly ActuatorRow? _strokeRow;
    private readonly LineEdit? _strokeDurationInput;   // LinearCmd ms
    private readonly CheckBox? _continuousCheckBox;
    private readonly ActuatorRow? _rotateRow;
    private readonly CheckBox? _rotateClockwiseBox;
    private readonly LineEdit? _durationInput;         // timed scalar stop (seconds)
    private readonly Button? _sendButton;

    private int _sessionId;
    private bool _suppressCloseCallback;
    private float _continuousTimer;

    public int SessionId => _sessionId;

    public ToyControlWindow(
        bool isController,
        Action<ToyControlCommand>? sendCommand,
        Action<int>? closeSession)
    {
        MinSize = SetSize = new Vector2(480, isController ? 560 : 180);
        Title = Loc.GetString("toy-control-window-title");

        _isController = isController;
        _sendCommand = sendCommand;
        _closeSession = closeSession;

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(4)
        };
        Contents.AddChild(root);

        _modeLabel = new Label();
        _statusLabel = new Label();
        root.AddChild(_modeLabel);
        root.AddChild(_statusLabel);

        if (_isController)
        {
            root.AddChild(MakeSeparator());

            // ── Scalar actuators ────────────────────────────────────
            root.AddChild(SectionLabel(Loc.GetString("toy-control-window-section-scalar")));

            _vibrateRow = new ActuatorRow(Loc.GetString("toy-control-window-actuator-vibrate"), enabledByDefault: true);
            _oscillateRow = new ActuatorRow(Loc.GetString("toy-control-window-actuator-oscillate"));
            _inflateRow = new ActuatorRow(Loc.GetString("toy-control-window-actuator-inflate"));
            _constrictRow = new ActuatorRow(Loc.GetString("toy-control-window-actuator-constrict"));
            root.AddChild(_vibrateRow);
            root.AddChild(_oscillateRow);
            root.AddChild(_inflateRow);
            root.AddChild(_constrictRow);

            // Duration (timed stop)
            var durRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
            durRow.AddChild(new Label { Text = Loc.GetString("toy-control-window-duration-label") });
            _durationInput = new LineEdit { MinWidth = 70, Text = "5.00", PlaceHolder = "s" };
            durRow.AddChild(_durationInput);
            root.AddChild(durRow);

            root.AddChild(MakeSeparator());

            // ── Stroke / Linear ─────────────────────────────────────
            var strokeHeader = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
            strokeHeader.AddChild(SectionLabel(Loc.GetString("toy-control-window-section-stroke")));
            strokeHeader.AddChild(new Control { HorizontalExpand = true });
            _continuousCheckBox = new CheckBox { Text = Loc.GetString("toy-control-window-continuous-label") };
            strokeHeader.AddChild(_continuousCheckBox);
            root.AddChild(strokeHeader);

            _strokeRow = new ActuatorRow(Loc.GetString("toy-control-window-actuator-stroke"), enabledByDefault: false);
            root.AddChild(_strokeRow);

            var strokeDurRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
            strokeDurRow.AddChild(new Label { Text = Loc.GetString("toy-control-window-stroke-duration-label") });
            _strokeDurationInput = new LineEdit { MinWidth = 70, Text = "500", PlaceHolder = "ms" };
            strokeDurRow.AddChild(_strokeDurationInput);
            root.AddChild(strokeDurRow);

            // Wire up continuous-send on slider movement
            _strokeRow.SliderValueChanged += OnStrokeSliderMoved;

            root.AddChild(MakeSeparator());

            // ── Rotate ───────────────────────────────────────────────
            root.AddChild(SectionLabel(Loc.GetString("toy-control-window-section-rotate")));

            _rotateRow = new ActuatorRow(Loc.GetString("toy-control-window-actuator-rotate"));
            root.AddChild(_rotateRow);

            _rotateClockwiseBox = new CheckBox
            {
                Text = Loc.GetString("toy-control-window-rotate-clockwise"),
                Pressed = true
            };
            root.AddChild(_rotateClockwiseBox);

            root.AddChild(MakeSeparator());

            // ── Preset shortcuts ─────────────────────────────────────
            root.AddChild(SectionLabel(Loc.GetString("toy-control-window-section-presets")));
            var presetRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
            AddPresetButton(presetRow, Loc.GetString("toy-control-window-intensity-soft"),   () => _vibrateRow?.SetValue(0.25f));
            AddPresetButton(presetRow, Loc.GetString("toy-control-window-intensity-medium"), () => _vibrateRow?.SetValue(0.50f));
            AddPresetButton(presetRow, Loc.GetString("toy-control-window-intensity-strong"), () => _vibrateRow?.SetValue(0.85f));
            root.AddChild(presetRow);

            root.AddChild(MakeSeparator());

            _sendButton = new Button
            {
                Text = Loc.GetString("toy-control-window-send"),
                HorizontalExpand = true
            };
            _sendButton.OnPressed += _ => SendAll();
            root.AddChild(_sendButton);
        }

        var closeButton = new Button
        {
            Text = Loc.GetString("toy-control-window-close"),
            HorizontalExpand = true
        };
        closeButton.OnPressed += _ => Close();
        root.AddChild(closeButton);

        OnClose += HandleClose;
    }

    // ──────────────────────────────────────────────────────────────────
    //  Public API
    // ──────────────────────────────────────────────────────────────────

    public void AttachSession(int sessionId)
    {
        _sessionId = sessionId;

        _modeLabel.Text = _isController
            ? Loc.GetString("toy-control-window-mode-controller")
            : Loc.GetString("toy-control-window-mode-target");

        _statusLabel.Text = Loc.GetString("toy-control-window-status-active");
    }

    public void SetStatus(string text) => _statusLabel.Text = text;

    public void SetControlsEnabled(bool enabled)
    {
        if (_sendButton != null) _sendButton.Disabled = !enabled;
    }

    public void CloseFromSessionEnd()
    {
        _suppressCloseCallback = true;
        Close();
    }

    // ──────────────────────────────────────────────────────────────────
    //  Frame update: continuous stroke send
    // ──────────────────────────────────────────────────────────────────

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!_isController
            || _continuousCheckBox == null || !_continuousCheckBox.Pressed
            || _strokeRow == null || !_strokeRow.Enabled
            || _sessionId == 0 || _sendCommand == null)
            return;

        _continuousTimer += args.DeltaSeconds;
        if (_continuousTimer < ContinuousSendIntervalSec)
            return;

        _continuousTimer = 0f;
        SendStrokeOnly();
    }

    // ──────────────────────────────────────────────────────────────────
    //  Private helpers
    // ──────────────────────────────────────────────────────────────────

    private void HandleClose()
    {
        if (!_suppressCloseCallback && _sessionId != 0)
            _closeSession?.Invoke(_sessionId);

        _suppressCloseCallback = false;
        _sessionId = 0;
        _continuousTimer = 0f;
    }

    private void OnStrokeSliderMoved()
    {
        if (_continuousCheckBox == null || !_continuousCheckBox.Pressed
            || _strokeRow == null || !_strokeRow.Enabled
            || _sessionId == 0)
            return;

        _continuousTimer = 0f;
        SendStrokeOnly();
    }

    private void SendStrokeOnly()
    {
        if (_strokeRow == null || _sendCommand == null) return;

        var cmd = new ToyControlCommand(_sessionId)
        {
            LinearPosition = _strokeRow.Value,
            LinearDurationMs = ParseLinearDurationMs(),
        };

        _sendCommand(cmd);
    }

    private void SendAll()
    {
        if (_sendCommand == null) return;

        var cmd = new ToyControlCommand(_sessionId)
        {
            DurationSeconds = ParseDurationSeconds(),
            Vibrate    = _vibrateRow?.EnabledValue   ?? float.NaN,
            Oscillate  = _oscillateRow?.EnabledValue ?? float.NaN,
            Inflate    = _inflateRow?.EnabledValue   ?? float.NaN,
            Constrict  = _constrictRow?.EnabledValue ?? float.NaN,
            LinearPosition = _strokeRow?.EnabledValue ?? float.NaN,
            LinearDurationMs = ParseLinearDurationMs(),
            RotateSpeed     = _rotateRow?.EnabledValue ?? float.NaN,
            RotateClockwise = _rotateClockwiseBox?.Pressed ?? true,
        };

        _sendCommand(cmd);
    }

    private float ParseDurationSeconds()
    {
        if (_durationInput == null) return 5f;
        if (float.TryParse(_durationInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return Math.Clamp(v, 0f, 30f);

        _durationInput.Text = "5.00";
        return 5f;
    }

    private int ParseLinearDurationMs()
    {
        if (_strokeDurationInput == null) return 500;
        if (int.TryParse(_strokeDurationInput.Text, out var v))
            return Math.Clamp(v, 50, 10000);

        _strokeDurationInput.Text = "500";
        return 500;
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        FontColorOverride = Color.FromHex("#cccccc")
    };

    private static Control MakeSeparator() => new PanelContainer
    {
        MinHeight = 2,
        PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#444444") }
    };

    private static void AddPresetButton(BoxContainer row, string label, Action action)
    {
        var btn = new Button { Text = label };
        btn.OnPressed += _ => action();
        row.AddChild(btn);
    }

    // ──────────────────────────────────────────────────────────────────
    //  Inner class: a single actuator row (enable check + slider + input)
    // ──────────────────────────────────────────────────────────────────

    private sealed class ActuatorRow : BoxContainer
    {
        public event Action? SliderValueChanged;

        private readonly CheckBox _enableBox;
        private readonly Slider _slider;
        private readonly LineEdit _input;
        private bool _suppress;

        public bool Enabled => _enableBox.Pressed;

        /// <summary>Current slider value (0–1), regardless of enable state.</summary>
        public float Value => _slider.Value;

        /// <summary>Value if enabled, else NaN.</summary>
        public float EnabledValue => _enableBox.Pressed ? _slider.Value : float.NaN;

        public void SetValue(float v)
        {
            _suppress = true;
            _slider.Value = Math.Clamp(v, 0f, 1f);
            _input.Text = _slider.Value.ToString("0.00", CultureInfo.InvariantCulture);
            _suppress = false;
        }

        public ActuatorRow(string label, bool enabledByDefault = false)
        {
            Orientation = LayoutOrientation.Horizontal;
            SeparationOverride = 6;
            HorizontalExpand = true;

            _enableBox = new CheckBox
            {
                Text = label,
                Pressed = enabledByDefault,
                MinWidth = 120
            };

            _slider = new Slider
            {
                MinValue = 0f,
                MaxValue = 1f,
                Value = 0f,
                HorizontalExpand = true,
                MinHeight = 22
            };

            _input = new LineEdit
            {
                MinWidth = 65,
                Text = "0.00"
            };

            _slider.OnValueChanged += OnSliderChanged;
            _input.OnTextEntered += args => TryApplyInput(args.Text);
            _input.OnFocusExit += _ => TryApplyInput(_input.Text);

            AddChild(_enableBox);
            AddChild(_slider);
            AddChild(_input);
        }

        private void OnSliderChanged(Robust.Client.UserInterface.Controls.Range _)
        {
            if (_suppress) return;
            _suppress = true;
            _input.Text = _slider.Value.ToString("0.00", CultureInfo.InvariantCulture);
            _suppress = false;
            SliderValueChanged?.Invoke();
        }

        private void TryApplyInput(string text)
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                _suppress = true;
                _slider.Value = Math.Clamp(v, 0f, 1f);
                _input.Text = _slider.Value.ToString("0.00", CultureInfo.InvariantCulture);
                _suppress = false;
            }
            else
            {
                _input.Text = _slider.Value.ToString("0.00", CultureInfo.InvariantCulture);
            }
        }
    }
}
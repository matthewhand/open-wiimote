using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using OpenWiimote.Core.Mapping;
using OpenWiimote.Core.Virtual;
using IVirtualGamepad = OpenWiimote.Core.Virtual.IVirtualGamepad;

namespace OpenWiimote.Platform.Windows;

public sealed class ViGEmVirtualGamepad : IVirtualGamepad
{
    private static readonly (XboxGamepadButtons Flag, Xbox360Button Button)[] ButtonMap =
    [
        (XboxGamepadButtons.A, Xbox360Button.A),
        (XboxGamepadButtons.B, Xbox360Button.B),
        (XboxGamepadButtons.X, Xbox360Button.X),
        (XboxGamepadButtons.Y, Xbox360Button.Y),
        (XboxGamepadButtons.DPadUp, Xbox360Button.Up),
        (XboxGamepadButtons.DPadDown, Xbox360Button.Down),
        (XboxGamepadButtons.DPadLeft, Xbox360Button.Left),
        (XboxGamepadButtons.DPadRight, Xbox360Button.Right),
        (XboxGamepadButtons.Start, Xbox360Button.Start),
        (XboxGamepadButtons.Back, Xbox360Button.Back),
        (XboxGamepadButtons.Guide, Xbox360Button.Guide),
        (XboxGamepadButtons.LeftShoulder, Xbox360Button.LeftShoulder),
        (XboxGamepadButtons.RightShoulder, Xbox360Button.RightShoulder),
        (XboxGamepadButtons.LeftThumb, Xbox360Button.LeftThumb),
        (XboxGamepadButtons.RightThumb, Xbox360Button.RightThumb)
    ];

    private readonly IXbox360Controller _controller;
    private readonly object _gate = new();
    private XboxGamepadState _lastState = XboxGamepadState.Neutral;
    private bool _connected;
    private bool _disposed;

    public ViGEmVirtualGamepad(ViGEmClient client)
    {
        _controller = client.CreateXbox360Controller();
        _controller.Connect();
        _connected = true;
        _controller.ResetReport();
        _controller.SubmitReport();
    }

    public bool IsConnected => _connected;

    public void Submit(XboxGamepadState state)
    {
        lock (_gate)
        {
            if (_disposed || !_connected || state == _lastState)
            {
                return;
            }

            _controller.ResetReport();

            foreach (var (flag, button) in ButtonMap)
            {
                if ((state.Buttons & flag) != 0)
                {
                    _controller.SetButtonState(button, true);
                }
            }

            if (state.LeftTrigger > 0)
            {
                _controller.SetSliderValue(Xbox360Slider.LeftTrigger, state.LeftTrigger);
            }

            if (state.RightTrigger > 0)
            {
                _controller.SetSliderValue(Xbox360Slider.RightTrigger, state.RightTrigger);
            }

            SetAxis(Xbox360Axis.LeftThumbX, state.LeftThumbX);
            SetAxis(Xbox360Axis.LeftThumbY, state.LeftThumbY);
            SetAxis(Xbox360Axis.RightThumbX, state.RightThumbX);
            SetAxis(Xbox360Axis.RightThumbY, state.RightThumbY);

            _controller.SubmitReport();
            _lastState = state;
        }
    }

    private void SetAxis(Xbox360Axis axis, short value)
    {
        if (value != 0)
        {
            _controller.SetAxisValue(axis, value);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                _controller.ResetReport();
                _controller.SubmitReport();
            }
            catch
            {
            }

            try
            {
                _controller.Disconnect();
            }
            catch
            {
            }

            _connected = false;
            _lastState = XboxGamepadState.Neutral;
        }
    }
}

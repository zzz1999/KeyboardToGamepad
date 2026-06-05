using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace KeyboardToGamepad;

/// <summary>
/// Virtual DualShock 4 (PlayStation) controller. Note: ViGEmBus cannot emulate a true PS5
/// DualSense, but a DS4 is what most games (incl. Cuphead) accept as a PlayStation pad.
/// DS4 axes are bytes (0..255, center 128) and the dpad is a single 8-way hat.
/// </summary>
public sealed class DualShock4Pad : IVirtualPad
{
    private readonly ViGEmClient _client;
    private readonly IDualShock4Controller _pad;
    private readonly Stick _ls = new();
    private readonly Stick _rs = new();
    private readonly Stick _dpad = new();

    public string Kind => "DualShock 4";

    public DualShock4Pad()
    {
        _client = new ViGEmClient();              // throws if ViGEmBus is missing
        _pad = _client.CreateDualShock4Controller();
        _pad.Connect();
    }

    public void Apply(PadControl c, bool p)
    {
        switch (c)
        {
            case PadControl.South: _pad.SetButtonState(DualShock4Button.Cross, p); break;
            case PadControl.East: _pad.SetButtonState(DualShock4Button.Circle, p); break;
            case PadControl.West: _pad.SetButtonState(DualShock4Button.Square, p); break;
            case PadControl.North: _pad.SetButtonState(DualShock4Button.Triangle, p); break;
            case PadControl.L1: _pad.SetButtonState(DualShock4Button.ShoulderLeft, p); break;
            case PadControl.R1: _pad.SetButtonState(DualShock4Button.ShoulderRight, p); break;
            case PadControl.L3: _pad.SetButtonState(DualShock4Button.ThumbLeft, p); break;
            case PadControl.R3: _pad.SetButtonState(DualShock4Button.ThumbRight, p); break;
            case PadControl.Back: _pad.SetButtonState(DualShock4Button.Share, p); break;
            case PadControl.Start: _pad.SetButtonState(DualShock4Button.Options, p); break;
            case PadControl.Guide: _pad.SetButtonState(DualShock4SpecialButton.Ps, p); break;

            case PadControl.L2:
                _pad.SetSliderValue(DualShock4Slider.LeftTrigger, p ? (byte)255 : (byte)0);
                _pad.SetButtonState(DualShock4Button.TriggerLeft, p);
                break;
            case PadControl.R2:
                _pad.SetSliderValue(DualShock4Slider.RightTrigger, p ? (byte)255 : (byte)0);
                _pad.SetButtonState(DualShock4Button.TriggerRight, p);
                break;

            case PadControl.DpadUp: _dpad.Up = p; PushDpad(); break;
            case PadControl.DpadDown: _dpad.Down = p; PushDpad(); break;
            case PadControl.DpadLeft: _dpad.Left = p; PushDpad(); break;
            case PadControl.DpadRight: _dpad.Right = p; PushDpad(); break;

            case PadControl.LStickUp: _ls.Up = p; PushLeft(); break;
            case PadControl.LStickDown: _ls.Down = p; PushLeft(); break;
            case PadControl.LStickLeft: _ls.Left = p; PushLeft(); break;
            case PadControl.LStickRight: _ls.Right = p; PushLeft(); break;
            case PadControl.RStickUp: _rs.Up = p; PushRight(); break;
            case PadControl.RStickDown: _rs.Down = p; PushRight(); break;
            case PadControl.RStickLeft: _rs.Left = p; PushRight(); break;
            case PadControl.RStickRight: _rs.Right = p; PushRight(); break;
        }
    }

    private void PushDpad() => _pad.SetDPadDirection(DpadDir(_dpad));

    private static DualShock4DPadDirection DpadDir(Stick d)
    {
        bool u = d.Up && !d.Down, dn = d.Down && !d.Up, l = d.Left && !d.Right, r = d.Right && !d.Left;
        if (u && r) return DualShock4DPadDirection.Northeast;
        if (u && l) return DualShock4DPadDirection.Northwest;
        if (dn && r) return DualShock4DPadDirection.Southeast;
        if (dn && l) return DualShock4DPadDirection.Southwest;
        if (u) return DualShock4DPadDirection.North;
        if (dn) return DualShock4DPadDirection.South;
        if (l) return DualShock4DPadDirection.West;
        if (r) return DualShock4DPadDirection.East;
        return DualShock4DPadDirection.None;
    }

    private void PushLeft()
    {
        _pad.SetAxisValue(DualShock4Axis.LeftThumbX, AxisX(_ls.Left, _ls.Right));
        _pad.SetAxisValue(DualShock4Axis.LeftThumbY, AxisY(_ls.Up, _ls.Down));
    }

    private void PushRight()
    {
        _pad.SetAxisValue(DualShock4Axis.RightThumbX, AxisX(_rs.Left, _rs.Right));
        _pad.SetAxisValue(DualShock4Axis.RightThumbY, AxisY(_rs.Up, _rs.Down));
    }

    private static byte AxisX(bool left, bool right)   // left = 0, right = 255, center = 128
    {
        int s = (right ? 1 : 0) - (left ? 1 : 0);
        return s > 0 ? (byte)255 : s < 0 ? (byte)0 : (byte)128;
    }

    private static byte AxisY(bool up, bool down)      // up = 0, down = 255, center = 128
    {
        int s = (down ? 1 : 0) - (up ? 1 : 0);
        return s > 0 ? (byte)255 : s < 0 ? (byte)0 : (byte)128;
    }

    public void Dispose()
    {
        try { _pad.Disconnect(); } catch { /* already gone */ }
        _client.Dispose();
    }
}

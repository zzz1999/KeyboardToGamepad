using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace KeyboardToGamepad;

/// <summary>Virtual Xbox 360 controller (XInput). AutoSubmitReport is on, so every Set* pushes a frame.</summary>
internal sealed class Xbox360Pad : PadBase
{
    private readonly IXbox360Controller _pad;

    public override string Kind => "Xbox 360";

    public Xbox360Pad()
    {
        _pad = _client.CreateXbox360Controller();
        _pad.Connect();
    }

    public override void Apply(PadControl c, bool p)
    {
        if (ApplyStick(c, p)) return;
        switch (c)
        {
            case PadControl.South: _pad.SetButtonState(Xbox360Button.A, p); break;
            case PadControl.East: _pad.SetButtonState(Xbox360Button.B, p); break;
            case PadControl.West: _pad.SetButtonState(Xbox360Button.X, p); break;
            case PadControl.North: _pad.SetButtonState(Xbox360Button.Y, p); break;
            case PadControl.DpadUp: _pad.SetButtonState(Xbox360Button.Up, p); break;
            case PadControl.DpadDown: _pad.SetButtonState(Xbox360Button.Down, p); break;
            case PadControl.DpadLeft: _pad.SetButtonState(Xbox360Button.Left, p); break;
            case PadControl.DpadRight: _pad.SetButtonState(Xbox360Button.Right, p); break;
            case PadControl.L1: _pad.SetButtonState(Xbox360Button.LeftShoulder, p); break;
            case PadControl.R1: _pad.SetButtonState(Xbox360Button.RightShoulder, p); break;
            case PadControl.L3: _pad.SetButtonState(Xbox360Button.LeftThumb, p); break;
            case PadControl.R3: _pad.SetButtonState(Xbox360Button.RightThumb, p); break;
            case PadControl.Back: _pad.SetButtonState(Xbox360Button.Back, p); break;
            case PadControl.Start: _pad.SetButtonState(Xbox360Button.Start, p); break;
            case PadControl.Guide: _pad.SetButtonState(Xbox360Button.Guide, p); break;
            case PadControl.L2: _pad.SetSliderValue(Xbox360Slider.LeftTrigger, p ? (byte)255 : (byte)0); break;
            case PadControl.R2: _pad.SetSliderValue(Xbox360Slider.RightTrigger, p ? (byte)255 : (byte)0); break;
        }
    }

    protected override void PushLeft()
    {
        _pad.SetAxisValue(Xbox360Axis.LeftThumbX, Axis(_ls.Left, _ls.Right));
        _pad.SetAxisValue(Xbox360Axis.LeftThumbY, Axis(_ls.Down, _ls.Up));   // up = positive
    }

    protected override void PushRight()
    {
        _pad.SetAxisValue(Xbox360Axis.RightThumbX, Axis(_rs.Left, _rs.Right));
        _pad.SetAxisValue(Xbox360Axis.RightThumbY, Axis(_rs.Down, _rs.Up));
    }

    private static short Axis(bool neg, bool pos)
    {
        int s = (pos ? 1 : 0) - (neg ? 1 : 0);
        return s > 0 ? short.MaxValue : s < 0 ? short.MinValue : (short)0;
    }

    public override void Dispose()
    {
        try { _pad.Disconnect(); } catch { /* already gone */ }
        _client.Dispose();
    }
}

using Nefarius.ViGEm.Client;

namespace KeyboardToGamepad;

/// <summary>
/// Shared plumbing for the concrete virtual pads: owns the ViGEm client and the two analog-stick
/// states, and routes the eight stick-direction inputs into a device-specific push. Each pad still
/// maps the face buttons / d-pad / triggers itself, since those differ between Xbox and DualShock.
/// </summary>
internal abstract class PadBase : IVirtualPad
{
    protected readonly ViGEmClient _client;
    protected readonly Stick _ls = new();
    protected readonly Stick _rs = new();

    protected PadBase() => _client = new ViGEmClient();   // throws if ViGEmBus is missing

    public abstract string Kind { get; }
    public abstract void Apply(PadControl control, bool pressed);

    /// <summary>Route the 8 stick directions into the stick state + a device push. True if handled.</summary>
    protected bool ApplyStick(PadControl control, bool pressed)
    {
        switch (control)
        {
            case PadControl.LStickUp: _ls.Up = pressed; PushLeft(); return true;
            case PadControl.LStickDown: _ls.Down = pressed; PushLeft(); return true;
            case PadControl.LStickLeft: _ls.Left = pressed; PushLeft(); return true;
            case PadControl.LStickRight: _ls.Right = pressed; PushLeft(); return true;
            case PadControl.RStickUp: _rs.Up = pressed; PushRight(); return true;
            case PadControl.RStickDown: _rs.Down = pressed; PushRight(); return true;
            case PadControl.RStickLeft: _rs.Left = pressed; PushRight(); return true;
            case PadControl.RStickRight: _rs.Right = pressed; PushRight(); return true;
            default: return false;
        }
    }

    protected abstract void PushLeft();
    protected abstract void PushRight();

    public abstract void Dispose();
}

namespace KeyboardToGamepad;

/// <summary>A virtual game controller fed by the keyboard.</summary>
public interface IVirtualPad : IDisposable
{
    /// <summary>Human label, e.g. "Xbox 360" or "DualShock 4".</summary>
    string Kind { get; }

    void Apply(PadControl control, bool pressed);

    /// <summary>
    /// Release every input back to neutral (all buttons up, sticks / d-pad / triggers centered).
    /// Called on a mapping swap so a key held during a rebind/delete can't leave a button stuck.
    /// </summary>
    void ResetAll()
    {
        foreach (PadControl control in Enum.GetValues<PadControl>())
            Apply(control, false);
    }
}

/// <summary>Factory that picks the controller type from config.</summary>
public static class VirtualPad
{
    public static IVirtualPad Create(string type) => type.Trim().ToLowerInvariant() switch
    {
        "" or "xbox360" or "xbox" or "x360" or "xinput" => new Xbox360Pad(),
        "ds4" or "dualshock4" or "dualshock" or "ps4" or "ps5" or "dualsense" or "playstation"
            => new DualShock4Pad(),
        _ => throw new ArgumentException($"unknown controllerType '{type}' (use 'xbox360' or 'ds4')"),
    };
}

/// <summary>Tracks the four directions of a stick/dpad so opposite presses cancel.</summary>
internal sealed class Stick
{
    public bool Up, Down, Left, Right;
}

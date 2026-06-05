namespace KeyboardToGamepad;

/// <summary>
/// Controller-agnostic logical inputs. Each concrete pad (Xbox 360, DualShock 4)
/// translates these to its own buttons/axes. Face buttons use positional names:
/// South = A / Cross, East = B / Circle, West = X / Square, North = Y / Triangle.
/// </summary>
public enum PadControl
{
    South, East, West, North,                     // A B X Y  /  Cross Circle Square Triangle
    DpadUp, DpadDown, DpadLeft, DpadRight,
    L1, R1,                                       // bumpers
    L2, R2,                                       // triggers
    L3, R3,                                       // stick clicks
    Back, Start, Guide,                           // Back/Share, Start/Options, Guide/PS
    LStickUp, LStickDown, LStickLeft, LStickRight,
    RStickUp, RStickDown, RStickLeft, RStickRight,
}

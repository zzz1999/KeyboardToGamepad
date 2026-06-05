using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeyboardToGamepad;

/// <summary>
/// A resolved virtual-pad input that a physical key drives.
/// <see cref="Control"/> is the logical input; <see cref="Element"/> is the diagram id
/// (e.g. "A", "U", "LB", "LT", "LS").
/// </summary>
public sealed class PadTarget
{
    public PadControl Control { get; init; }
    public string Name { get; init; } = "";      // the raw config target string
    public string Element { get; init; } = "";    // gamepad-diagram id
}

/// <summary>
/// Maps physical keys to <see cref="PadTarget"/>s. Lookup by virtual-key code (hook backend)
/// and by scan code + extended flag (Interception backend). Keeps an ordered list of entries
/// for the UI, and can turn a captured scan code back into a key name (for rebinding).
/// </summary>
public sealed class InputMap
{
    private readonly Dictionary<int, PadTarget> _byVk;
    private readonly Dictionary<int, PadTarget> _byScan;
    private readonly List<(string Key, PadTarget Target)> _entries;

    public IReadOnlyList<(string Key, PadTarget Target)> Entries => _entries;

    private InputMap(Dictionary<int, PadTarget> byVk, Dictionary<int, PadTarget> byScan,
                     List<(string, PadTarget)> entries)
    {
        _byVk = byVk;
        _byScan = byScan;
        _entries = entries;
    }

    public bool TryGetByVk(int vkCode, out PadTarget? target) => _byVk.TryGetValue(vkCode, out target);

    public bool TryGetByScan(ushort code, bool extended, out PadTarget? target) =>
        _byScan.TryGetValue(ScanKey(code, extended), out target);

    private static int ScanKey(ushort code, bool extended) => code | (extended ? 1 << 16 : 0);

    public static InputMap Build(Dictionary<string, string> mappings)
    {
        var byVk = new Dictionary<int, PadTarget>();
        var byScan = new Dictionary<int, PadTarget>();
        var entries = new List<(string, PadTarget)>();

        foreach (var (keyName, targetName) in mappings)
        {
            if (!Enum.TryParse<Keys>(keyName, ignoreCase: true, out var key))
                throw new ArgumentException(
                    $"unknown keyboard key '{keyName}' (use Keys names, e.g. W, J, NumPad8, Up)");

            int vk = (int)key;
            var target = ParseTarget(targetName);

            // Reject two key names that resolve to the same physical key (e.g. "Enter"/"Return")
            // instead of silently letting the second one shadow the first in the lookup.
            if (byVk.ContainsKey(vk))
                throw new ArgumentException(
                    $"key '{keyName}' maps to the same physical key as another entry (vk 0x{vk:X2}); remove the duplicate");
            byVk[vk] = target;

            ushort scan = (ushort)MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC);
            bool extended = ExtendedKeys.Contains(key);
            if (scan != 0)
            {
                int scanKey = ScanKey(scan, extended);
                if (byScan.ContainsKey(scanKey))
                    throw new ArgumentException(
                        $"key '{keyName}' collides with another entry on scan code 0x{scan:X2}; remove the duplicate");
                byScan[scanKey] = target;
            }
            // scan == 0: no hardware scan code (media / some OEM keys). It can only work via the
            // 'hook' backend (matched by virtual-key above); under interception it would never fire,
            // so leave it out of byScan rather than letting two such keys collide on scan 0.

            entries.Add((keyName, target));
        }

        return new InputMap(byVk, byScan, entries);
    }

    /// <summary>Every target name the user can map to (used by the TUI "add mapping" picker).</summary>
    public static readonly string[] TargetNames =
    {
        "A", "B", "X", "Y",
        "LB", "RB", "LT", "RT",
        "Up", "Down", "Left", "Right",
        "Back", "Start", "Guide", "LS", "RS",
        "LStickUp", "LStickDown", "LStickLeft", "LStickRight",
        "RStickUp", "RStickDown", "RStickLeft", "RStickRight",
    };

    /// <summary>Turn a captured scan code back into a Keys name (for live rebinding).</summary>
    public static string ScanToKeyName(ushort scan, bool extended)
    {
        // Extended (E0) keys -- arrows, Insert/Delete/Home/End/PageUp/Down, etc. -- share a raw
        // scan code with their numpad twins (Up and NumPad8 are both 0x48). OR in the 0xE0 prefix
        // so MapVirtualKey resolves the real key (0xE048 -> VK_UP, not VK_NUMPAD8); without this a
        // rebind onto an arrow key would be saved as "NumPad8" and silently break on next launch.
        uint code = extended ? (scan | 0xE000u) : scan;
        uint vk = MapVirtualKey(code, MAPVK_VSC_TO_VK_EX);
        if (vk == 0) return $"Scan{scan:X2}";
        var key = (Keys)vk;
        return Enum.IsDefined(typeof(Keys), key) ? key.ToString() : $"Scan{scan:X2}";
    }

    // Keys delivered with the E0 (extended) prefix -- needed to tell e.g. arrow Up (E0 48)
    // apart from numpad 8 (48), which share a raw scan code.
    private static readonly HashSet<Keys> ExtendedKeys = new()
    {
        Keys.Up, Keys.Down, Keys.Left, Keys.Right,
        Keys.Insert, Keys.Delete, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown,
        Keys.Divide, Keys.NumLock, Keys.PrintScreen, Keys.Apps,
        Keys.RControlKey, Keys.RMenu,
    };

    // Element is the gamepad-diagram glyph id, which is NOT 1:1 with the target: Back/Start/Guide
    // use short ids BK/ST/GD, and all four stick directions plus the L3/R3 click share one glyph
    // per stick (LS / RS) because the diagram only draws a single stick.
    private static PadTarget ParseTarget(string raw)
    {
        switch (raw.Trim().ToLowerInvariant())
        {
            case "a": return Make(PadControl.South, raw, "A");
            case "b": return Make(PadControl.East, raw, "B");
            case "x": return Make(PadControl.West, raw, "X");
            case "y": return Make(PadControl.North, raw, "Y");

            case "lb": case "leftshoulder": return Make(PadControl.L1, raw, "LB");
            case "rb": case "rightshoulder": return Make(PadControl.R1, raw, "RB");

            case "ls": case "leftthumb": return Make(PadControl.L3, raw, "LS");
            case "rs": case "rightthumb": return Make(PadControl.R3, raw, "RS");

            case "back": case "select": return Make(PadControl.Back, raw, "BK");
            case "start": return Make(PadControl.Start, raw, "ST");
            case "guide": return Make(PadControl.Guide, raw, "GD");

            case "up": case "dpadup": return Make(PadControl.DpadUp, raw, "U");
            case "down": case "dpaddown": return Make(PadControl.DpadDown, raw, "D");
            case "left": case "dpadleft": return Make(PadControl.DpadLeft, raw, "L");
            case "right": case "dpadright": return Make(PadControl.DpadRight, raw, "R");

            case "lt": case "lefttrigger": return Make(PadControl.L2, raw, "LT");
            case "rt": case "righttrigger": return Make(PadControl.R2, raw, "RT");

            case "lstickup": return Make(PadControl.LStickUp, raw, "LS");
            case "lstickdown": return Make(PadControl.LStickDown, raw, "LS");
            case "lstickleft": return Make(PadControl.LStickLeft, raw, "LS");
            case "lstickright": return Make(PadControl.LStickRight, raw, "LS");

            case "rstickup": return Make(PadControl.RStickUp, raw, "RS");
            case "rstickdown": return Make(PadControl.RStickDown, raw, "RS");
            case "rstickleft": return Make(PadControl.RStickLeft, raw, "RS");
            case "rstickright": return Make(PadControl.RStickRight, raw, "RS");

            default:
                throw new ArgumentException($"unknown pad target '{raw}' (see README for the list)");
        }
    }

    private static PadTarget Make(PadControl control, string name, string element) =>
        new() { Control = control, Name = name, Element = element };

    private const uint MAPVK_VK_TO_VSC = 0x00;
    private const uint MAPVK_VSC_TO_VK_EX = 0x03;

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);
}

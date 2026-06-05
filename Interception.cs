using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace KeyboardToGamepad;

/// <summary>
/// Minimal P/Invoke wrapper around interception.dll -- the user-mode library of the
/// oblitum/Interception keyboard/mouse filter driver.
///
/// Requires:
///   1. The Interception driver installed:  install-interception.exe /install   (admin, then REBOOT)
///   2. interception.dll (x64) next to KeyboardToGamepad.exe.
///
/// Unlike a WH_KEYBOARD_LL hook, this sits BELOW Raw Input, so keys can be truly
/// consumed before a game (e.g. Cuphead / Rewired) ever sees them.
/// </summary>
internal static class Interception
{
    // Capture every keyboard event; we decide per-stroke whether to forward it.
    public const ushort FILTER_KEY_ALL = 0xFFFF;

    // InterceptionKeyState bits (stroke.State).
    public const ushort KEY_UP = 0x01;
    public const ushort KEY_E0 = 0x02;
    public const ushort KEY_E1 = 0x04;

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyStroke
    {
        public ushort Code;        // scan code
        public ushort State;       // KEY_UP / KEY_E0 / ...
        public uint Information;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseStroke
    {
        public ushort State;
        public ushort Flags;
        public short Rolling;
        public int X;
        public int Y;
        public uint Information;
    }

    // Union buffer big enough for either stroke type.
    [StructLayout(LayoutKind.Explicit)]
    public struct Stroke
    {
        [FieldOffset(0)] public MouseStroke Mouse;
        [FieldOffset(0)] public KeyStroke Key;
    }

    public delegate int Predicate(int device);

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr interception_create_context();

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void interception_destroy_context(IntPtr context);

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void interception_set_filter(IntPtr context, Predicate predicate, ushort filter);

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int interception_wait_with_timeout(IntPtr context, uint milliseconds);

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int interception_receive(IntPtr context, int device, ref Stroke stroke, uint nstroke);

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int interception_send(IntPtr context, int device, ref Stroke stroke, uint nstroke);

    [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern int interception_is_keyboard(int device);

    // create_context() returns non-null even without the driver, so detect it ourselves:
    // the installer adds a "keyboard" upper filter to the keyboard device class.
    private const string KeyboardClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e96b-e325-11ce-bfc1-08002be10318}";

    public static bool IsDriverInstalled()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(KeyboardClassKey);
            if (key?.GetValue("UpperFilters") is string[] filters)
                return filters.Any(f => string.Equals(f, "keyboard", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            // best effort -- if we can't read the registry, assume it might be there
        }
        return false;
    }
}

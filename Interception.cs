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

    // InterceptionKeyState bits (stroke.State). We only consume KEY_UP and KEY_E0; KEY_E1
    // (the Pause/Break prefix) is listed for completeness but never checked.
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

    // We only handle keyboards, but the native Stroke is a union; MouseStroke must exist so the
    // union (and the interception_receive/_send buffers) is sized correctly. Its fields are unused.
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

    private const string DllName = "interception.dll";
    private const string DllResource = "KeyboardToGamepad.interception.dll";
    private static int _resolverRegistered;

    /// <summary>
    /// Lets the app ship as a single exe: registers a resolver that loads interception.dll from a
    /// copy embedded in this assembly (extracted to %TEMP% on first use) instead of requiring a
    /// loose interception.dll next to the exe. Safe to call repeatedly. When this build has no
    /// embedded copy, the resolver does nothing and the runtime falls back to its normal search
    /// (a loose interception.dll beside the exe), so source/dev builds keep working.
    /// </summary>
    public static void RegisterNativeLibraryResolver()
    {
        if (Interlocked.Exchange(ref _resolverRegistered, 1) != 0) return;
        NativeLibrary.SetDllImportResolver(typeof(Interception).Assembly, (name, _, _) =>
        {
            if (!name.Equals(DllName, StringComparison.OrdinalIgnoreCase))
                return IntPtr.Zero;
            try
            {
                string? path = ExtractEmbeddedDll();
                if (path is not null && NativeLibrary.TryLoad(path, out IntPtr handle))
                    return handle;
            }
            catch
            {
                // fall through to the runtime's default search (loose file next to the exe)
            }
            return IntPtr.Zero;
        });
    }

    /// <summary>Write the embedded interception.dll to %TEMP% (once) and return its path, or null if not embedded.</summary>
    private static string? ExtractEmbeddedDll()
    {
        using Stream? src = typeof(Interception).Assembly.GetManifestResourceStream(DllResource);
        if (src is null) return null;   // not embedded in this build -> let default resolution handle it

        string dir = Path.Combine(Path.GetTempPath(), "KeyboardToGamepad-setup");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, DllName);

        // Reuse an already-extracted copy: a second instance can't overwrite a DLL the first one
        // has loaded. Only (re)write when it's missing or a different size.
        if (!File.Exists(path) || new FileInfo(path).Length != src.Length)
        {
            try
            {
                using FileStream dst = File.Create(path);
                src.CopyTo(dst);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Locked by another running instance -> the existing file is fine to load.
            }
        }
        return path;
    }

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

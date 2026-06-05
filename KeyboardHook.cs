using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeyboardToGamepad;

/// <summary>
/// A WH_KEYBOARD_LL hook. Mapped keys drive the virtual pad and (optionally) are
/// swallowed. NOTE: this does NOT block apps that read the keyboard via Raw Input
/// (e.g. Cuphead/Rewired) -- use the Interception backend for those.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private readonly InputMap _map;
    private readonly IVirtualPad _pad;
    private readonly bool _block;

    private readonly LowLevelKeyboardProc _proc;   // keep a ref so the GC can't collect it
    private IntPtr _hookId = IntPtr.Zero;

    public KeyboardHook(InputMap map, IVirtualPad pad, bool block)
    {
        _map = map;
        _pad = pad;
        _block = block;
        _proc = HookCallback;
    }

    public void Install()
    {
        using Process cur = Process.GetCurrentProcess();
        using ProcessModule mod = cur.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(mod.ModuleName!), 0);
        if (_hookId == IntPtr.Zero)
            throw new InvalidOperationException(
                $"failed to install keyboard hook (win32 error {Marshal.GetLastWin32Error()})");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            if (_map.TryGetByVk((int)data.vkCode, out var target) && target is not null)
            {
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    _pad.Apply(target.Control, true);
                else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                    _pad.Apply(target.Control, false);

                if (_block)
                    return (IntPtr)1;   // swallow (normal-input apps only)
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}

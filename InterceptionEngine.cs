namespace KeyboardToGamepad;

/// <summary>
/// Captures keyboard input via the Interception driver (below Raw Input), drives the
/// virtual pad for mapped keys, and -- when blocking -- consumes them so the game never
/// sees them. Every other key is forwarded untouched, so normal typing keeps working.
/// Supports runtime remapping (SetMap), live block toggling, and one-shot key capture.
/// </summary>
public sealed class InterceptionEngine : IDisposable
{
    private volatile InputMap _map;
    private readonly IVirtualPad _pad;
    private volatile bool _block;

    private IntPtr _context;
    private Thread? _thread;
    private volatile bool _running;
    private Interception.Predicate? _predicate;          // kept alive so the GC can't collect it
    private volatile Action<ushort, bool>? _capture;     // one-shot "grab next key" for rebinding
    // After a capture grabs a key-DOWN we consume it; remember it so we can also swallow the
    // matching key-UP, otherwise that stray release leaks through and pushes a spurious frame.
    private bool _swallowUp;
    private ushort _swallowCode;
    private bool _swallowExt;

    public InterceptionEngine(InputMap map, IVirtualPad pad, bool block)
    {
        _map = map;
        _pad = pad;
        _block = block;
    }

    /// <summary>Toggle at runtime: true = consume mapped keys (hide from the game).</summary>
    public bool Block { get => _block; set => _block = value; }

    /// <summary>Raised when a mapped key changes state (target, pressed). For the UI.</summary>
    private volatile Action<PadTarget, bool>? _onInput;
    public Action<PadTarget, bool>? OnInput { get => _onInput; set => _onInput = value; }

    /// <summary>Release all virtual-pad inputs (used by the UI when the mapping changes).</summary>
    public void ResetPad() => _pad.ResetAll();

    /// <summary>Swap the active mapping (after the user edits keys in the TUI).</summary>
    public void SetMap(InputMap map) => _map = map;

    /// <summary>Grab the next key-down once (scan code, extended), consume it, then stop.</summary>
    public void BeginCapture(Action<ushort, bool> onCaptured) => _capture = onCaptured;

    public void CancelCapture() => _capture = null;

    public void Start()
    {
        _context = Interception.interception_create_context();
        if (_context == IntPtr.Zero)
            throw new InvalidOperationException(
                "Interception driver not available -- install it (install-interception.exe /install) and reboot.");

        _predicate = device => Interception.interception_is_keyboard(device);
        Interception.interception_set_filter(_context, _predicate, Interception.FILTER_KEY_ALL);

        _running = true;
        // Above-normal so the capture loop isn't preempted while the game hammers the CPU,
        // which keeps key->pad latency low and avoids scheduling jitter under load.
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "KeyboardToGamepad-Interception",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    private void Loop()
    {
        while (_running)
        {
            // Timeout so the loop can notice _running going false and exit cleanly.
            int device = Interception.interception_wait_with_timeout(_context, 100);
            if (device <= 0)
                continue;

            Interception.Stroke stroke = default;
            if (Interception.interception_receive(_context, device, ref stroke, 1) <= 0)
                continue;

            if (Interception.interception_is_keyboard(device) != 0)
            {
                Interception.KeyStroke ks = stroke.Key;
                bool extended = (ks.State & Interception.KEY_E0) != 0;
                bool isUp = (ks.State & Interception.KEY_UP) != 0;

                // Rebind capture: grab the next key-down for the UI and consume it.
                Action<ushort, bool>? cap = _capture;
                if (cap is not null && !isUp)
                {
                    _capture = null;
                    _swallowUp = true; _swallowCode = ks.Code; _swallowExt = extended;
                    cap(ks.Code, extended);
                    continue;
                }

                // Swallow the matching key-up of the key we just captured, so it can't leak through
                // as a stray release (a release with no preceding press) into the pad / UI.
                if (_swallowUp && isUp && ks.Code == _swallowCode && extended == _swallowExt)
                {
                    _swallowUp = false;
                    continue;
                }

                if (_map.TryGetByScan(ks.Code, extended, out var target) && target is not null)
                {
                    _pad.Apply(target.Control, !isUp);
                    _onInput?.Invoke(target, !isUp);
                    if (_block)
                        continue; // consume: do NOT forward this key to the OS / game
                }
            }

            // Forward anything we didn't consume so the rest of the system behaves normally.
            Interception.interception_send(_context, device, ref stroke, 1);
        }
    }

    public void Dispose()
    {
        _running = false;
        // The loop only ever blocks up to 100ms (interception_wait_with_timeout), so it should exit
        // well within this window. If it doesn't (wedged driver / saturated CPU), leak the context
        // rather than destroy it out from under a thread still inside receive/send.
        bool exited = _thread is null || _thread.Join(2000);
        if (_context != IntPtr.Zero && exited)
        {
            Interception.interception_destroy_context(_context);
            _context = IntPtr.Zero;
        }
    }
}

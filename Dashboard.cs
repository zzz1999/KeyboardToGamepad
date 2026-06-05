using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Rendering;
using Panel = Spectre.Console.Panel;   // disambiguate from System.Windows.Forms.Panel (in scope project-wide via <UseWindowsForms> + ImplicitUsings)

namespace KeyboardToGamepad;

/// <summary>
/// Live Spectre.Console TUI: a gamepad diagram that dims pressed buttons, a mapping table,
/// and in-place editing (rebind / add / remove / restore defaults), a controller label
/// editor, and a shortcut to the driver download page.
/// </summary>
public sealed class Dashboard
{
    private const string DriverUrl = "https://github.com/oblitum/Interception/releases";

    private readonly Config _config;
    private readonly InterceptionEngine _engine;
    private readonly string _padKind;

    private InputMap _map;
    private readonly Dictionary<PadTarget, bool> _pressed = new();
    private readonly object _lock = new();

    private int _selected;
    private volatile bool _running = true;

    // capture state (the actual key grab happens on the engine thread)
    private enum CaptureMode { None, Rebind, Add }
    private CaptureMode _captureMode = CaptureMode.None;
    private string _pendingTarget = "";
    private volatile bool _captureReady;
    private ushort _capScan;
    private bool _capExt;
    private bool _capturing;          // showing the "press a key" prompt

    private string _flash = "";       // transient status line

    // animation state
    private const int FrameMs = 40;             // render tick interval
    private const long FlashBrightFrames = 24;  // ~1s: status line shown bright (aqua)
    private const long FlashFadeFrames = 48;    // ~2s: then dimmed, then cleared
    private long _frame;                                          // one tick per render (~40ms)
    private long _flashFrame;                                     // frame when _flash was set
    private readonly Dictionary<string, long> _hitFrame = new();  // element -> frame last pressed

    public Dashboard(Config config, InputMap map, InterceptionEngine engine, string padKind)
    {
        _config = config;
        _map = map;
        _engine = engine;
        _padKind = padKind;
        _engine.OnInput = (target, pressed) =>
        {
            lock (_lock)
            {
                bool was = _pressed.TryGetValue(target, out var prev) && prev;
                _pressed[target] = pressed;
                if (pressed && !was) _hitFrame[target.Element] = Interlocked.Read(ref _frame);   // only on the initial press
            }
        };
    }

    // Actions that can't run inside the AnsiConsole.Live render loop because they need a normal
    // console (an interactive prompt, a browser launch) or end it (quit): HandleKey returns one and
    // Run executes it after leaving Live. Inline F-keys (rebind/remove/defaults/block/save) return None.
    private enum DeferredAction { None, AddMapping, SetName, OpenDriver, Quit }

    public void Run()
    {
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; _running = false; };
        AnsiConsole.Clear();

        while (_running)
        {
            DeferredAction action = DeferredAction.None;

            AnsiConsole.Live(BuildView())
                .AutoClear(false)
                .Start(ctx =>
                {
                    while (_running && action == DeferredAction.None)
                    {
                        Interlocked.Increment(ref _frame);   // animation tick

                        if (_captureReady)
                        {
                            _captureReady = false;
                            ApplyCapture(_capScan, _capExt);
                        }

                        ctx.UpdateTarget(BuildView());
                        ctx.Refresh();

                        while (Console.KeyAvailable)
                        {
                            ConsoleKey key = Console.ReadKey(intercept: true).Key;
                            action = HandleKey(key);
                            if (action != DeferredAction.None) break;
                        }

                        Thread.Sleep(FrameMs);
                    }
                });

            switch (action)   // these need a normal console, so run after leaving Live
            {
                case DeferredAction.AddMapping: DoAddMapping(); break;
                case DeferredAction.SetName: DoSetName(); break;
                case DeferredAction.OpenDriver: OpenDriver(); break;
                case DeferredAction.Quit: _running = false; break;
            }
        }

        AnsiConsole.Clear();
    }

    private DeferredAction HandleKey(ConsoleKey key)
    {
        int count = _map.Entries.Count;

        if (_capturing)
        {
            if (key == ConsoleKey.Escape)
            {
                _engine.CancelCapture();
                _capturing = false;
                _captureMode = CaptureMode.None;
                Flash("cancelled");
            }
            return DeferredAction.None;
        }

        // Action hotkeys use F-keys so they never collide with letter / WASD mappings.
        switch (key)
        {
            case ConsoleKey.UpArrow:
                if (count > 0) _selected = (_selected - 1 + count) % count;
                break;
            case ConsoleKey.DownArrow:
                if (count > 0) _selected = (_selected + 1) % count;
                break;
            case ConsoleKey.F1: StartRebind(); break;
            case ConsoleKey.F2: return DeferredAction.AddMapping;
            case ConsoleKey.F3: DeleteSelected(); break;
            case ConsoleKey.F4: RestoreDefaults(); break;
            case ConsoleKey.F5:
                _engine.Block = !_engine.Block;
                Flash($"block {(_engine.Block ? "ON" : "off")}");
                break;
            case ConsoleKey.F6: return DeferredAction.SetName;
            case ConsoleKey.F7: return DeferredAction.OpenDriver;
            case ConsoleKey.F8: Save(); Flash("config saved"); break;
            case ConsoleKey.F9: return DeferredAction.Quit;
        }
        return DeferredAction.None;
    }

    private void StartRebind()
    {
        var entries = _map.Entries;
        if (_selected < 0 || _selected >= entries.Count) return;

        _pendingTarget = entries[_selected].Target.Name;
        _captureMode = CaptureMode.Rebind;
        BeginCapture();
        Flash($"press a key for [{_pendingTarget}]  (Esc cancels)");
    }

    private void DoAddMapping()
    {
        AnsiConsole.Clear();
        const string cancel = "(cancel - go back)";
        var choices = new List<string> { cancel };
        choices.AddRange(InputMap.TargetNames);
        string target = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Add mapping - pick the pad input ([grey]or cancel[/]):")
                .PageSize(18)
                .MoreChoicesText("[grey](scroll for more)[/]")
                .AddChoices(choices));

        AnsiConsole.Clear();   // back to the live view
        if (target == cancel) { Flash("add cancelled"); return; }

        _pendingTarget = target;
        _captureMode = CaptureMode.Add;
        BeginCapture();        // now the live view shows "press a key"
    }

    private void BeginCapture()
    {
        _capturing = true;
        _engine.BeginCapture((scan, ext) => { _capScan = scan; _capExt = ext; _captureReady = true; });
    }

    private void ApplyCapture(ushort scan, bool ext)
    {
        _capturing = false;
        CaptureMode mode = _captureMode;
        _captureMode = CaptureMode.None;

        string newKey = InputMap.ScanToKeyName(scan, ext);

        if (mode == CaptureMode.Rebind)
        {
            var entries = _map.Entries;
            if (_selected < 0 || _selected >= entries.Count) return;
            string oldKey = entries[_selected].Key;
            // If newKey is already bound to a different key's target, we're about to clobber it --
            // capture the displaced target so the flash can say so explicitly.
            bool clobber = _config.Mappings.TryGetValue(newKey, out var displaced)
                           && !string.Equals(newKey, oldKey, StringComparison.OrdinalIgnoreCase);
            _config.Mappings.Remove(oldKey);
            _config.Mappings[newKey] = _pendingTarget;
            RebuildMap();
            SelectKey(newKey);
            Flash(clobber
                ? $"{oldKey} -> {newKey} ({_pendingTarget})  [replaced {newKey}'s old binding: {displaced}]"
                : $"{oldKey} -> {newKey}  ({_pendingTarget})");
        }
        else if (mode == CaptureMode.Add)
        {
            bool existed = _config.Mappings.TryGetValue(newKey, out var displaced);
            _config.Mappings[newKey] = _pendingTarget;
            RebuildMap();
            SelectKey(newKey);
            Flash(existed
                ? $"{newKey}: {displaced} -> {_pendingTarget} (replaced)"
                : $"added {newKey} -> {_pendingTarget}");
        }
        Save();
    }

    private void DeleteSelected()
    {
        var entries = _map.Entries;
        if (_selected < 0 || _selected >= entries.Count) return;
        // Refuse to delete the last mapping: an empty config gets auto-saved and then fails to load
        // on the next launch. F4 restores the default layout instead.
        if (entries.Count <= 1) { Flash("keep at least one mapping (F4 restores defaults)"); return; }
        var (key, target) = entries[_selected];
        _config.Mappings.Remove(key);
        RebuildMap();
        Save();
        Flash($"removed {key} ({target.Name})");
    }

    private void RestoreDefaults()
    {
        _config.Mappings = Config.DefaultMappings();
        RebuildMap();
        Save();
        _selected = 0;
        Flash("restored default layout");
    }

    private void RebuildMap()
    {
        // Release anything held under the OLD map first, so a key still down during the edit can't
        // leave a button / stick / d-pad latched on the virtual pad.
        _engine.ResetPad();
        var newMap = InputMap.Build(_config.Mappings);
        // Swap the map and clear the press state under one lock, so an in-flight OnInput can't slip
        // a stale entry in between.
        lock (_lock)
        {
            _engine.SetMap(newMap);
            _map = newMap;
            _pressed.Clear();
        }
        if (_selected >= _map.Entries.Count)
            _selected = Math.Max(0, _map.Entries.Count - 1);
    }

    private void SelectKey(string key)
    {
        var entries = _map.Entries;
        for (int i = 0; i < entries.Count; i++)
            if (string.Equals(entries[i].Key, key, StringComparison.OrdinalIgnoreCase)) { _selected = i; return; }
    }

    private void DoSetName()
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[grey]Note: Windows always shows XInput pads as \"Xbox 360 Controller for Windows\".[/]");
        AnsiConsole.MarkupLine("[grey]This label is used inside KeyboardToGamepad only.[/]");
        string name = AnsiConsole.Prompt(
            new TextPrompt<string>("Controller label:")
                .DefaultValue(_config.ControllerName)
                .AllowEmpty());
        if (!string.IsNullOrWhiteSpace(name))
            _config.ControllerName = name.Trim();
        Save();
        AnsiConsole.Clear();
    }

    private void OpenDriver()
    {
        try
        {
            Process.Start(new ProcessStartInfo(DriverUrl) { UseShellExecute = true });
            Flash("opened the Interception driver page in your browser");
        }
        catch (Exception ex)
        {
            Flash($"could not open browser: {ex.Message}");
        }
    }

    private void Save()
    {
        try { _config.Save(); }
        catch (Exception ex) { Flash($"save failed: {ex.Message}"); }
    }

    private void Flash(string message) { _flash = message; _flashFrame = _frame; }

    // ---- rendering ------------------------------------------------------

    // A "breathing" status LED: hue slowly cycles, brightness pulses (a soft fade in/out).
    private string BreathDot()
    {
        double t = _frame;
        double hue = (t * 2.0) % 360.0;                            // full colour cycle ~7s
        double val = 0.35 + 0.65 * (Math.Sin(t * 0.2) + 1.0) / 2;  // breathe ~1.3s
        var (r, g, b) = HsvToRgb(hue, 1.0, val);
        return $"[#{r:X2}{g:X2}{b:X2}]●[/]";
    }

    private static (int r, int g, int b) HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        double m = v - c;
        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return (Math.Min(255, (int)((r + m) * 255)),
                Math.Min(255, (int)((g + m) * 255)),
                Math.Min(255, (int)((b + m) * 255)));
    }

    private IRenderable BuildView()
    {
        // section 1: status
        var status = new Markup(
            $"[bold]{Markup.Escape(_config.ControllerName)}[/]  [grey](virtual {Markup.Escape(_padKind)} pad = Player 2)[/]\n" +
            $"ViGEm [green]●[/]   Interception [green]●[/]   block: {(_engine.Block ? "[green]ON[/]" : "[grey]off[/]")}   {BreathDot()} [grey]live[/]");

        // section 2: gamepad monitor (compact - no wasted side space) | key table
        var middle = new Grid();
        middle.AddColumn(new GridColumn());
        middle.AddColumn(new GridColumn());
        middle.AddRow(RenderPad(), RenderTable());

        // section 3: controls
        string controls;
        if (_capturing)
        {
            string c = (_frame / 5) % 2 == 0 ? "yellow" : "grey";   // pulse to draw the eye
            controls = $"[{c}]Press a key for \"{Markup.Escape(_pendingTarget)}\"   (Esc to cancel)[/]";
        }
        else
        {
            controls = "[grey]↑/↓ select   F1 rebind   F2 add   F3 remove   F4 defaults[/]\n" +
                       "[grey]F5 block   F6 name   F7 driver page   F8 save   F9 quit[/]";
        }
        long flashAge = _frame - _flashFrame;
        if (!string.IsNullOrEmpty(_flash))
        {
            if (flashAge < FlashBrightFrames) controls += $"\n[aqua]{Markup.Escape(_flash)}[/]";
            else if (flashAge < FlashFadeFrames) controls += $"\n[grey]{Markup.Escape(_flash)}[/]";   // fade
            else _flash = "";                                                                          // gone
        }

        // wrap all three sections in one rounded frame titled with the project name (top-left)
        var body = new Rows(status, new Markup(" "), middle, new Markup(" "), new Markup(controls));
        return new Panel(body)
        {
            Header = new PanelHeader(" KeyboardToGamepad ").LeftJustified(),
            Border = BoxBorder.Rounded,
            Expand = false,
        };
    }

    private IRenderable RenderTable()
    {
        var table = new Table { Border = TableBorder.Rounded };
        table.AddColumn("");
        table.AddColumn("KEY");
        table.AddColumn("PAD");
        table.AddColumn("PRESSED");

        long now = _frame;
        var entries = _map.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var (k, t) = entries[i];
            bool down; long hf;
            lock (_lock)
            {
                down = _pressed.TryGetValue(t, out var v) && v;
                _hitFrame.TryGetValue(t.Element, out hf);
            }

            string marker = i == _selected ? "[yellow]›[/]" : " ";
            string keyCell = i == _selected
                ? $"[yellow bold]{Markup.Escape(k)}[/]"
                : $"[bold]{Markup.Escape(k)}[/]";

            table.AddRow(
                new Markup(marker),
                new Markup(keyCell),
                new Markup(Markup.Escape(t.Name)),
                new Markup(down ? PressDot(now - hf) : "[grey]·[/]"));
        }
        return table;
    }

    // PRESSED dot: a smooth gradient from bright cyan (just pressed) settling to green while held.
    private static string PressDot(long age)
    {
        double k = Math.Clamp(age / 8.0, 0, 1);
        return $"[#{Lerp(180, 40, k):X2}{Lerp(255, 205, k):X2}{Lerp(255, 90, k):X2}]●[/]";
    }

    private static int Lerp(int a, int b, double k) => (int)Math.Round(a + (b - a) * k);

    private IRenderable RenderPad()
    {
        var (art, own) = BuildPadCanvas();
        return EncodePad(art, own);
    }

    // Lay out the Xbox-style silhouette (grey outline) + button glyphs onto a char grid; own[y][x]
    // records which diagram element each cell belongs to (null = static outline) so EncodePad can
    // colour it. Buttons sit in their real positions: left stick upper-left, d-pad lower-left, face
    // buttons upper-right, right stick lower-centre, Back/Start centre, bumpers on top.
    private static (char[][] art, string?[][] own) BuildPadCanvas()
    {
        const int FW = 63, H = 15;
        var art = new char[H][];
        var own = new string?[H][];
        for (int y = 0; y < H; y++) { art[y] = new char[FW]; Array.Fill(art[y], ' '); own[y] = new string?[FW]; }

        void Set(int y, int x, char c, string? el) { if (y >= 0 && y < H && x >= 0 && x < FW) { art[y][x] = c; own[y][x] = el; } }
        char Flip(char c) => c switch { '╭' => '╮', '╮' => '╭', '╰' => '╯', '╯' => '╰', '╱' => '╲', '╲' => '╱', _ => c };
        void Mir(int y, int x, char c) { Set(y, x, c, null); Set(y, FW - 1 - x, Flip(c), null); }
        void Lab(int y, int x, string s, string el) { for (int i = 0; i < s.Length; i++) Set(y, x + i, s[i], el); }

        // controller body outline (symmetric): rounded top with shoulder bulges, bulging sides,
        // two thick grips that flare out at the bottom, hollow centre between them.
        Mir(0, 13, '╭'); for (int x = 14; x <= 21; x++) Mir(0, x, '─'); Mir(0, 22, '╮');    // shoulders
        Mir(1, 23, '╰'); for (int x = 24; x <= 31; x++) Mir(1, x, '─');                      // top-centre dip
        Mir(1, 12, '╱'); Mir(2, 10, '╱'); Mir(3, 8, '╱'); Mir(4, 7, '╱');                    // upper sides
        for (int y = 5; y <= 7; y++) Mir(y, 6, '│');                                         // widest sides
        Mir(8, 6, '╲'); Mir(9, 5, '│'); Mir(10, 4, '│'); Mir(11, 5, '╲'); Mir(12, 7, '╲');   // grip outer edge
        Mir(13, 9, '╰'); for (int x = 10; x <= 15; x++) Mir(13, x, '─'); Mir(13, 16, '╯');   // grip bottom
        Mir(12, 17, '╱'); Mir(11, 18, '╱'); Mir(10, 20, '╱'); Mir(9, 22, '╱');               // grip inner edge
        Mir(8, 24, '╱'); for (int x = 25; x <= 31; x++) Mir(8, x, '─');                       // hollow top / body underside

        // buttons in their Xbox positions
        Lab(1, 16, "LB", "LB"); Lab(1, 45, "RB", "RB");
        Lab(2, 25, "Bk", "BK"); Lab(2, 30, "(*)", "GD"); Lab(2, 36, "St", "ST");
        Lab(4, 14, "( o )", "LS");
        Lab(6, 34, "( o )", "RS");
        Set(5, 26, '^', "U"); Set(6, 24, '<', "L"); Set(6, 26, '+', null); Set(6, 28, '>', "R"); Set(7, 26, 'v', "D");
        Set(3, 46, 'Y', "Y"); Set(4, 44, 'X', "X"); Set(4, 48, 'B', "B"); Set(5, 46, 'A', "A");

        return (art, own);
    }

    // Render the canvas to Spectre markup: the outline stays grey, each button glyph is bright
    // white when idle and dims to grey while its element is pressed. Contiguous cells of the same
    // element are run-length-batched so each run becomes a single coloured span.
    private IRenderable EncodePad(char[][] art, string?[][] own)
    {
        const int FW = 63, H = 15;
        var active = new HashSet<string>();
        lock (_lock)
        {
            foreach (var kv in _pressed)
                if (kv.Value) active.Add(kv.Key.Element);
        }

        string Color(string? el, string seg)
        {
            string g = Markup.Escape(seg);
            if (el is null) return $"[grey]{g}[/]";                                  // silhouette outline
            return active.Contains(el) ? $"[grey]{g}[/]" : $"[bold white]{g}[/]";    // dim on press / bright idle
        }

        var lines = new string[H];
        for (int y = 0; y < H; y++)
        {
            var sb = new System.Text.StringBuilder();
            int x = 0;
            while (x < FW)
            {
                string? el = own[y][x];
                int s = x;
                while (x < FW && own[y][x] == el) x++;
                sb.Append(Color(el, new string(art[y], s, x - s)));
            }
            lines[y] = sb.ToString();
        }

        return new Markup(string.Join("\n", lines));
    }

}

using System.Windows.Forms;

namespace KeyboardToGamepad;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.Title = "KeyboardToGamepad";
        Console.WriteLine("KeyboardToGamepad - turn a keyboard region into a virtual controller (Player 2)");
        Console.WriteLine("--------------------------------------------------------------------");

        // 1) Load the JSON config (path can be overridden as the first CLI arg).
        string configPath = args.Length > 0
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "config.json");

        Config config;
        try
        {
            config = Config.Load(configPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to load config '{configPath}': {ex.Message}");
            return 1;
        }
        Console.WriteLine($"Loaded {config.Mappings.Count} key mapping(s) from {configPath}");

        // 2) Translate "key name -> target name" into the lookup maps.
        InputMap map;
        try
        {
            map = InputMap.Build(config.Mappings);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Invalid mapping: {ex.Message}");
            return 1;
        }

        PrintOccupiedKeys(config.Mappings);

        // 3) Create the chosen virtual controller (requires the ViGEmBus driver).
        IVirtualPad pad;
        try
        {
            pad = VirtualPad.Create(config.ControllerType);
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"[ERROR] {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("[ERROR] Could not create the virtual controller (is ViGEmBus installed?).");
            Console.WriteLine("        https://github.com/nefarius/ViGEmBus/releases");
            Console.WriteLine($"        (details: {ex.Message})");
            return 2;
        }
        Console.WriteLine($"Virtual {pad.Kind} controller connected - it will act as Player 2.");

        // 4) Run the chosen input backend.
        return config.Backend.Trim().ToLowerInvariant() switch
        {
            "interception" => RunInterception(map, pad, config),
            "hook" => RunHook(map, pad, config),
            _ => UnknownBackend(config.Backend, pad),
        };
    }

    private static void PrintOccupiedKeys(Dictionary<string, string> mappings)
    {
        Console.WriteLine();
        Console.WriteLine($"Keys captured for Player 2 ({mappings.Count}):");
        foreach (var (key, target) in mappings)
            Console.WriteLine($"    {key,-12} -> {target}");
        Console.WriteLine("    (these keys are taken over for P2; the rest of the keyboard stays Player 1)");
        Console.WriteLine();
    }

    private static int UnknownBackend(string backend, IVirtualPad pad)
    {
        Console.WriteLine($"[ERROR] Unknown backend '{backend}' (use 'interception' or 'hook').");
        pad.Dispose();
        return 1;
    }

    private static int RunInterception(InputMap map, IVirtualPad pad, Config config)
    {
        if (!Interception.IsDriverInstalled())
        {
            Console.WriteLine();
            Console.WriteLine("[WARN] The Interception driver does not appear to be installed.");
            Console.WriteLine("       Capturing/blocking keys will NOT work until you install it and reboot:");
            Console.WriteLine("       1) in an admin console:  install-interception.exe /install");
            Console.WriteLine("       2) reboot your PC");
            Console.WriteLine("       Download: https://github.com/oblitum/Interception/releases");
            Console.WriteLine();
        }

        InterceptionEngine engine;
        try
        {
            engine = new InterceptionEngine(map, pad, config.BlockMappedKeys);
            engine.Start();
        }
        catch (DllNotFoundException)
        {
            Console.WriteLine();
            Console.WriteLine("[ERROR] interception.dll not found next to KeyboardToGamepad.exe.");
            Console.WriteLine("        Get the x64 interception.dll from:");
            Console.WriteLine("        https://github.com/oblitum/Interception/releases");
            pad.Dispose();
            return 4;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"[ERROR] {ex.Message}");
            pad.Dispose();
            return 4;
        }

        bool wantDashboard = config.Ui.Trim().ToLowerInvariant() == "dashboard";
        if (wantDashboard && Console.IsOutputRedirected)
        {
            Console.WriteLine("[note] Output is redirected (no real console) - using plain mode.");
            wantDashboard = false;
        }

        if (wantDashboard)
        {
            try
            {
                new Dashboard(config, map, engine, pad.Kind).Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[dashboard error] {ex.Message} - falling back to plain mode.");
                RunPlain();
            }
        }
        else
        {
            RunPlain();
        }

        engine.Dispose();
        pad.Dispose();
        Console.WriteLine("Stopped.");
        return 0;
    }

    private static void RunPlain()
    {
        Console.WriteLine("Backend: Interception (P2 keys are captured below Raw Input - hidden from the game).");
        Console.WriteLine("Running. Keep this window open. Press Ctrl+C to quit.");
        using var stop = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };
        stop.Wait();
    }

    private static int RunHook(InputMap map, IVirtualPad pad, Config config)
    {
        using var hook = new KeyboardHook(map, pad, config.BlockMappedKeys);
        try
        {
            hook.Install();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] {ex.Message}");
            pad.Dispose();
            return 3;
        }

        Console.WriteLine("Backend: low-level hook (WARNING: does NOT block Raw Input games like Cuphead).");
        Console.WriteLine("Running. Keep this window open. Press Ctrl+C to quit.");

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; Application.Exit(); };
        Application.Run();

        pad.Dispose();
        Console.WriteLine("Stopped.");
        return 0;
    }
}

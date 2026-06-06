using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;
using Spectre.Console;
using Panel = Spectre.Console.Panel;   // WinForms (in scope via <UseWindowsForms>) also has a Panel

namespace KeyboardToGamepad;

/// <summary>
/// Makes a fresh PC "just work" from a single exe: detects the two kernel drivers this app needs
/// -- ViGEmBus (creates the virtual pad) and the Interception filter driver (captures the keyboard)
/// -- and, when one is missing, offers (in a small Spectre.Console prompt) to install it from an
/// installer EMBEDDED in this exe. So the user only has to copy KeyboardToGamepad.exe and run it.
///
/// The installers ship as embedded resources (see the &lt;EmbeddedResource&gt; items in the .csproj);
/// we extract them to a temp folder on demand. Installing a driver needs admin rights, so each
/// installer is launched elevated (a UAC prompt appears). The Interception driver additionally needs
/// a reboot before it captures keys, so after installing it we stop and offer to restart.
/// </summary>
internal static class DriverSetup
{
    // Embedded-resource logical names -- must match <LogicalName> in KeyboardToGamepad.csproj.
    private const string VigemResource = "KeyboardToGamepad.ViGEmBus_setup.exe";
    private const string InterceptionInstallerResource = "KeyboardToGamepad.install-interception.exe";

    // ViGEmBus registers a kernel-mode service under this name once installed.
    private const string VigemServiceKey = @"SYSTEM\CurrentControlSet\Services\ViGEmBus";

    // ERROR_CANCELLED -- thrown by Process.Start(runas) when the user dismisses the UAC prompt.
    private const int ErrorCancelled = 1223;

    /// <summary>True if the ViGEmBus driver is installed (its service key exists).</summary>
    public static bool IsVigemInstalled()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(VigemServiceKey);
            return key is not null;
        }
        catch
        {
            return true; // can't read the registry -> assume present and let pad creation be the judge
        }
    }

    /// <summary>
    /// Ensure the drivers the chosen config needs are installed, prompting (in a TUI selection) to
    /// install any that are missing from the embedded installers. Returns false when the app should
    /// stop now -- e.g. the Interception driver was just installed and a reboot is required before it
    /// can capture keys.
    /// </summary>
    public static bool EnsureDrivers(Config config)
    {
        bool needInterception =
            config.Backend.Trim().Equals("interception", StringComparison.OrdinalIgnoreCase);

        // 1) ViGEmBus -- needed by every backend (it creates the Player 2 virtual pad).
        if (!IsVigemInstalled() &&
            AskInstall("ViGEmBus", "Creates the virtual Xbox/PlayStation controller (Player 2).", needsReboot: false))
        {
            // Run the official installer interactively (elevated): its silent switches are unreliable
            // across versions, and a short driver wizard the user clicks through always works.
            RunInstaller(VigemResource, "ViGEmBus_setup.exe", arguments: "");
            AnsiConsole.MarkupLine(IsVigemInstalled()
                ? "[green]✔[/] ViGEmBus installed."
                : "[yellow]![/] ViGEmBus still not detected -- finish its installer (a reboot may be needed), then re-run.");
        }

        // 2) Interception -- only the 'interception' backend uses it.
        if (needInterception && !Interception.IsDriverInstalled() &&
            AskInstall("Interception", "Captures Player 2's keys below Raw Input so the game never sees them.", needsReboot: true))
        {
            RunInstaller(InterceptionInstallerResource, "install-interception.exe", arguments: "/install");

            // The installer writes the keyboard upper-filter immediately, but Windows only loads the
            // driver after a reboot -- so we can't capture this session regardless of the exit code.
            if (Interception.IsDriverInstalled())
            {
                AnsiConsole.MarkupLine("[green]✔[/] Interception driver installed -- a [yellow]reboot[/] is required before it works.");
                OfferReboot();
                return false;
            }

            AnsiConsole.MarkupLine("[yellow]![/] Interception install did not complete (was the admin prompt declined?).");
            AnsiConsole.MarkupLine("[grey]    Manual: run install-interception.exe /install in an admin console, then reboot.[/]");
        }

        return true;
    }

    /// <summary>Show a TUI card for a missing driver and let the user pick Install / Skip (default Install).</summary>
    private static bool AskInstall(string name, string what, bool needsReboot)
    {
        // No real console (piped/redirected) -> don't silently trigger an elevated installer, and
        // don't call Spectre prompts (they require an interactive terminal).
        if (!IsInteractive())
        {
            Console.WriteLine();
            Console.WriteLine($"[setup] The {name} driver is not installed ({what})");
            Console.WriteLine($"        Launch KeyboardToGamepad in a normal console window to install it.");
            return false;
        }

        string rebootNote = needsReboot
            ? "Installing needs admin rights and [yellow]one reboot[/]."
            : "Installing needs admin rights (a Windows UAC prompt).";
        AnsiConsole.Write(new Panel(new Markup(
                $"[bold]{name}[/] driver is [red]not installed[/].\n" +
                $"[grey]{Markup.Escape(what)}[/]\n" +
                rebootNote))
        {
            Header = new PanelHeader(" Driver setup ").LeftJustified(),
            Border = BoxBorder.Rounded,
            Expand = false,
        });

        const string install = "Install now (recommended)";
        const string skip = "Skip - continue without it";
        string choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"Install the [bold]{name}[/] driver now?")
                .AddChoices(install, skip));
        return choice == install;
    }

    /// <summary>Extract an embedded installer to a temp file and run it elevated, waiting for it to finish.</summary>
    private static void RunInstaller(string resource, string fileName, string arguments)
    {
        string path;
        FileStream guard;
        try
        {
            (path, guard) = ExtractResourceLocked(resource, fileName);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]![/] Could not unpack the bundled {Markup.Escape(fileName)}: {Markup.Escape(ex.Message)}");
            return;
        }

        // Hold the read-lock (deny-write/deny-delete) on the extracted file for the WHOLE elevated run.
        // We launch it with runas (elevates across the integrity boundary), so a same-user process must
        // not be able to swap the bytes between extraction and launch (a TOCTOU privilege-escalation).
        string dir = Path.GetDirectoryName(path)!;
        using (guard)
        {
            AnsiConsole.MarkupLine($"[grey]Launching {Markup.Escape(fileName)} -- click \"Yes\" on the Windows admin prompt...[/]");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = arguments,
                    UseShellExecute = true, // required to use the "runas" verb (UAC elevation)
                    Verb = "runas",
                };
                using Process? proc = Process.Start(psi);
                proc?.WaitForExit();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                AnsiConsole.MarkupLine("[yellow]![/] The admin prompt was declined -- skipping this driver.");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]![/] Could not run {Markup.Escape(fileName)}: {Markup.Escape(ex.Message)}");
            }
        }
        try { Directory.Delete(dir, recursive: true); } catch { /* temp -- leave it for Windows to clean */ }
    }

    /// <summary>
    /// Copy an embedded installer to a freshly created, randomly named per-run temp folder and return
    /// its path plus an open handle that pins it. The random folder + FileMode.CreateNew means a
    /// pre-planted fixed-name file can't be trusted, and the returned handle (deny write/delete share)
    /// keeps the bytes from being swapped before/while we elevate it. Caller disposes the handle.
    /// </summary>
    private static (string path, FileStream guard) ExtractResourceLocked(string resource, string fileName)
    {
        using Stream src = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new FileNotFoundException($"embedded installer '{resource}' is missing from this build");

        string dir = Path.Combine(Path.GetTempPath(), "KeyboardToGamepad-setup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);

        // CreateNew + FileShare.None: if anything already sits at this path, fail instead of trusting it.
        using (var dst = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            src.CopyTo(dst);

        // Reopen read-only allowing others to read/execute but NOT write or delete -> the file (and,
        // because the handle is open, its folder) can't be replaced while we hold this handle.
        var guard = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return (path, guard);
    }

    /// <summary>After installing Interception, offer to reboot now (it can't capture until then).</summary>
    private static void OfferReboot()
    {
        if (IsInteractive())
        {
            const string now = "Reboot now";
            const string later = "I'll reboot later";
            string choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Reboot is needed for key capture. Reboot now?")
                    .AddChoices(now, later));
            if (choice == now)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("shutdown", "/r /t 5") { UseShellExecute = true });
                    AnsiConsole.MarkupLine("[grey]Rebooting in 5 seconds -- run KeyboardToGamepad again after restart.[/]");
                    return;
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]![/] Could not trigger a reboot: {Markup.Escape(ex.Message)}");
                }
            }
        }
        AnsiConsole.MarkupLine("[grey]Please reboot, then run KeyboardToGamepad again.[/]");
    }

    // Spectre prompts need a real interactive terminal; bail to plain text when output/input is piped.
    private static bool IsInteractive() => !Console.IsInputRedirected && !Console.IsOutputRedirected;
}

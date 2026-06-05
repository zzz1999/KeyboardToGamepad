using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyboardToGamepad;

/// <summary>
/// Strongly-typed view of config.json. Can be saved back (the TUI edits it live).
/// Note: saving rewrites clean JSON and drops the // comments.
/// </summary>
public sealed class Config
{
    /// <summary>true = mapped keys ONLY drive P2 (consumed / hidden from the game).</summary>
    public bool BlockMappedKeys { get; set; } = true;

    /// <summary>"interception" (below Raw Input -- works with Cuphead) or "hook".</summary>
    public string Backend { get; set; } = "interception";

    /// <summary>"dashboard" (live Spectre.Console TUI) or "plain" (just logs).</summary>
    public string Ui { get; set; } = "dashboard";

    /// <summary>Virtual controller: "xbox360" or "ds4" (PlayStation, via DualShock 4 emulation).</summary>
    public string ControllerType { get; set; } = "xbox360";

    /// <summary>App-only label for the pad. Windows still shows it as "Xbox 360 Controller for Windows".</summary>
    public string ControllerName { get; set; } = "Player 2 (KeyboardToGamepad)";

    /// <summary>"keyboard key name" -> "virtual pad target". See InputMap for the vocabulary.</summary>
    public Dictionary<string, string> Mappings { get; set; } = new();

    /// <summary>Where this config was loaded from (used by Save). Not serialized.</summary>
    [JsonIgnore]
    public string? SourcePath { get; set; }

    /// <summary>The built-in WASD + JKL layout (used by "restore defaults").</summary>
    public static Dictionary<string, string> DefaultMappings() => new()
    {
        // movement
        ["W"] = "Up", ["A"] = "Left", ["S"] = "Down", ["D"] = "Right",
        // home row = the four face buttons (fighting-game / KOF style)
        ["H"] = "X", ["J"] = "A", ["K"] = "B", ["L"] = "Y",
        // top row = shoulders + triggers
        ["Y"] = "LB", ["U"] = "LT", ["I"] = "RT", ["O"] = "RB",
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static Config Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"config file not found: {path}");

        string json = File.ReadAllText(path);
        Config? cfg = JsonSerializer.Deserialize<Config>(json, ReadOptions);
        if (cfg is null)
            throw new InvalidDataException("config.json deserialized to null");
        if (cfg.Mappings.Count == 0)
            throw new InvalidDataException("config.json has no 'mappings'");

        cfg.SourcePath = path;
        return cfg;
    }

    public void Save(string? path = null)
    {
        string target = path ?? SourcePath
            ?? throw new InvalidOperationException("no path to save the config to");
        File.WriteAllText(target, JsonSerializer.Serialize(this, WriteOptions));
    }
}

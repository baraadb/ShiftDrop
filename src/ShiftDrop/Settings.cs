using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace ShiftDrop;

public sealed class Settings
{
    /// <summary>"auto", "ar" or "en".</summary>
    public string Language { get; set; } = "auto";
    /// <summary>"beside" (next to the original), "downloads" or "custom" (<see cref="CustomFolder"/>).</summary>
    public string SaveTo { get; set; } = "beside";
    public string CustomFolder { get; set; } = "";
    /// <summary>Show the wheel for drags from any app, not only File Explorer and the desktop.</summary>
    public bool AllApps { get; set; }
    public bool Paused { get; set; }
    public bool Welcomed { get; set; }
    public List<string> Hidden { get; set; } = [];

    /// <summary>"system", "light" or "dark".</summary>
    public string Theme { get; set; } = "system";
    /// <summary>Key that opens the wheel during a drag: "shift", "ctrl" or "alt". The other two switch wheels.</summary>
    public string OpenKey { get; set; } = "shift";
    /// <summary>"small", "medium" or "large".</summary>
    public string WheelSize { get; set; } = "medium";
    public bool ShowHints { get; set; } = true;
    public bool ShowCards { get; set; } = true;
    public bool RevealWhenDone { get; set; }
    public bool AutoUpdate { get; set; } = true;
    public DateTime LastUpdateCheck { get; set; }
    public string SkippedVersion { get; set; } = "";

    [JsonIgnore]
    public float WheelScale => WheelSize switch { "small" => 0.85f, "large" => 1.18f, _ => 1f };

    static readonly string[] KeyOrder = ["shift", "ctrl", "alt"];

    /// <summary>Virtual keys and names for opening the wheel, then switching to tools and to actions.</summary>
    [JsonIgnore]
    public (int Vk, string Name) OpenKeyInfo => KeyInfo(OpenKeyName);
    [JsonIgnore]
    public (int Vk, string Name) ToolsKeyInfo => KeyInfo(KeyOrder.Where(k => k != OpenKeyName).First());
    [JsonIgnore]
    public (int Vk, string Name) ActionsKeyInfo => KeyInfo(KeyOrder.Where(k => k != OpenKeyName).Last());
    string OpenKeyName => KeyOrder.Contains(OpenKey) ? OpenKey : "shift";

    static (int, string) KeyInfo(string k) => k switch
    {
        "ctrl" => (0x11, "Ctrl"),
        "alt" => (0x12, "Alt"),
        _ => (0x10, "Shift"),
    };

    [JsonIgnore] public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShiftDrop");
    [JsonIgnore] static string FilePath => Path.Combine(Folder, "settings.json");

    [JsonIgnore]
    public bool Arabic => Language switch
    {
        "ar" => true,
        "en" => false,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar",
    };

    public static Settings Load()
    {
        foreach (var f in new[] { FilePath, FilePath + ".bak" })
        {
            try
            {
                if (File.Exists(f) && JsonSerializer.Deserialize(File.ReadAllText(f), SettingsJson.Default.Settings) is { } s) return s;
            }
            catch { }
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, SettingsJson.Default.Settings));
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, FilePath + ".bak");
            else File.Move(tmp, FilePath);
        }
        catch (Exception e) { Core.Log.Write("settings save: " + e.Message); }
    }

    // ---- start with Windows (per user, no admin) ----

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool StartWithWindows
    {
        get
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("ShiftDrop") != null; }
            catch { return false; }
        }
        set
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value) k.SetValue("ShiftDrop", $"\"{Environment.ProcessPath}\"");
                else k.DeleteValue("ShiftDrop", false);
            }
            catch (Exception e) { Core.Log.Write("autostart: " + e.Message); }
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Settings))]
internal partial class SettingsJson : JsonSerializerContext;

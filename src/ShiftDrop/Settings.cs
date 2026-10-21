using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace ShiftDrop;

public sealed class Settings
{
    /// <summary>"auto", "ar" or "en".</summary>
    public string Language { get; set; } = "auto";
    /// <summary>"beside" (next to the original) or "downloads".</summary>
    public string SaveTo { get; set; } = "beside";
    /// <summary>Show the wheel for drags from any app, not only File Explorer and the desktop.</summary>
    public bool AllApps { get; set; }
    public bool Paused { get; set; }
    public bool Welcomed { get; set; }
    public List<string> Hidden { get; set; } = [];

    [JsonIgnore] public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShiftDrop");
    [JsonIgnore] static string FilePath => Path.Combine(Folder, "settings.json");

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

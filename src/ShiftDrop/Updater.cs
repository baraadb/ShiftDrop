using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using ShiftDrop.Core;

namespace ShiftDrop;

/// <summary>Where updates come from. Set <see cref="Repo"/> once the project is on GitHub ("owner/name").</summary>
public static class AppInfo
{
    public const string Repo = "baraadb/ShiftDrop";
    public static Version Version => typeof(AppInfo).Assembly.GetName().Version ?? new Version(0, 1, 0);
    public static string VersionText => Version.ToString(3);
    /// <summary>ShiftDrop is free; this is the optional way to say thanks.</summary>
    public const string Donate = "https://www.paypal.com/donate/?hosted_button_id=ZNRBHF6SX8ZH6";
    public static string? Website => Repo.Length > 0 ? $"https://github.com/{Repo}" : null;
}

public sealed record UpdateInfo(Version Version, string Tag, string? SetupUrl, string PageUrl);

/// <summary>Checks GitHub releases and installs a newer Setup.exe silently.</summary>
public static class Updater
{
    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        h.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ShiftDrop", AppInfo.VersionText));
        h.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return h;
    }

    public static bool Configured => AppInfo.Repo.Length > 0;

    /// <summary>The latest release if it is newer than this copy, otherwise null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        if (!Configured) throw new InvalidOperationException(L.T("Updates start working once ShiftDrop is published on GitHub.",
            "التحديثات تعمل بعد نشر ShiftDrop على GitHub."));
        using var resp = await Http.GetAsync($"https://api.github.com/repos/{AppInfo.Repo}/releases/latest", ct);
        if ((int)resp.StatusCode == 404) return null; // no release yet
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V').Split('-')[0], out var latest)) return null;
        if (latest <= AppInfo.Version) return null;
        string? setup = null;
        if (root.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                var url = a.GetProperty("browser_download_url").GetString() ?? "";
                if (name.StartsWith("ShiftDrop-Setup", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    url.StartsWith($"https://github.com/{AppInfo.Repo}/releases/download/", StringComparison.OrdinalIgnoreCase))
                    setup = url;
            }
        var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : AppInfo.Website ?? "";
        return new UpdateInfo(latest, tag, setup, page);
    }

    /// <summary>Downloads the installer, starts it silently and returns; the installer closes and restarts ShiftDrop.</summary>
    public static async Task InstallAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct = default)
    {
        if (update.SetupUrl == null) { OpenPage(update.PageUrl); return; }
        var file = Path.Combine(Path.GetTempPath(), $"ShiftDrop-Setup-{update.Version.ToString(3)}.exe");
        using (var resp = await Http.GetAsync(update.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = new FileStream(file, FileMode.Create, FileAccess.Write);
            var buf = new byte[1 << 16];
            long done = 0; int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
        }
        Process.Start(new ProcessStartInfo(file, "/SILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
    }

    public static void OpenPage(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}

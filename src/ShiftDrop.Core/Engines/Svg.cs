using System.Diagnostics;
using System.Globalization;

namespace ShiftDrop.Core.Engines;

/// <summary>
/// SVG pictures are drawn to PNG by resvg (resvg.exe next to ffmpeg, MPL 2.0) before anything else
/// happens to them, so every picture tool works on them. FFmpeg no longer needs librsvg and its
/// many libraries for this.
/// </summary>
public static class Svg
{
    /// <summary>Small icons are drawn at least this big, so the result is useful.</summary>
    public const int MinSide = 1024;

    public static bool IsSvg(string path) => FileKinds.Ext(path) == "svg";

    public static string? Locate(params string[] dirs)
    {
        var env = Environment.GetEnvironmentVariable("SHIFTDROP_RESVG_DIR");
        var name = OperatingSystem.IsWindows() ? "resvg.exe" : "resvg";
        foreach (var d in (env != null ? new[] { env } : []).Concat(dirs))
        {
            var p = Path.Combine(d, name);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>Draws <paramref name="svg"/> into <paramref name="png"/>, at least <see cref="MinSide"/> on the longer side.</summary>
    public static async Task RenderAsync(string? resvg, string svg, string png, CancellationToken ct)
    {
        if (resvg == null) throw new EngineException(L.T("SVG support is missing (resvg.exe was not found)", "دعم SVG غير موجود (لم يُعثر على resvg.exe)"));
        await RunAsync(resvg, [svg, png], ct);
        var (w, h) = PngSize(png);
        if (w > 0 && h > 0 && Math.Max(w, h) < MinSide)
            await RunAsync(resvg, w >= h ? ["-w", Num(MinSide), svg, png] : ["-h", Num(MinSide), svg, png], ct);
    }

    /// <summary>The SVG drawn to a PNG in <paramref name="tempDir"/> under the same base name (so outputs keep the original name).</summary>
    public static async Task<string> ToPngAsync(string? resvg, string svg, string tempDir, CancellationToken ct)
    {
        var dir = Path.Combine(tempDir, "svg-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var png = Path.Combine(dir, FileKinds.BaseName(svg) + ".png");
        await RenderAsync(resvg, svg, png, ct);
        return png;
    }

    static string Num(int v) => v.ToString(CultureInfo.InvariantCulture);

    public static (int W, int H) PngSize(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            Span<byte> b = stackalloc byte[24];
            if (f.Read(b) < 24 || b[1] != 'P' || b[2] != 'N' || b[3] != 'G') return (0, 0);
            return ((b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);
        }
        catch { return (0, 0); }
    }

    static async Task RunAsync(string exe, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new EngineException("resvg did not start");
        var err = p.StandardError.ReadToEndAsync(ct);
        _ = p.StandardOutput.ReadToEndAsync(ct);
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw; }
        if (p.ExitCode != 0)
            throw new EngineException(L.T("Could not draw this SVG: ", "تعذّر رسم ملف SVG: ") + (await err).Trim().Split('\n')[0]);
    }
}

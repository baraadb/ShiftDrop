using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ShiftDrop.Core.Engines;

public sealed record MediaInfo(
    double Duration, int Width, int Height,
    string? VideoCodec, string? AudioCodec, string? PixFmt,
    long BitRate, double Fps, bool HasVideo, bool HasAudio)
{
    public bool HasAlpha => PixFmt != null &&
        (PixFmt.Contains("rgba") || PixFmt.Contains("bgra") || PixFmt.Contains("argb") ||
         PixFmt.Contains("abgr") || PixFmt.StartsWith("ya") || PixFmt.StartsWith("yuva") ||
         PixFmt.Contains("gbrap") || PixFmt == "pal8");
}

/// <summary>Runs the bundled ffmpeg/ffprobe as separate processes with argument lists (never a shell).</summary>
public sealed class FFmpeg
{
    public string? FfmpegPath { get; }
    public string? FfprobePath { get; }
    public bool Available => FfmpegPath != null && FfprobePath != null;

    HashSet<string>? _encoders;
    string? _h264;
    readonly object _gate = new();

    public FFmpeg(string? ffmpeg, string? ffprobe)
    {
        FfmpegPath = ffmpeg;
        FfprobePath = ffprobe;
    }

    /// <summary>Looks in the given folders, then SHIFTDROP_FFMPEG_DIR, then PATH.</summary>
    public static FFmpeg Locate(params string[] dirs)
    {
        var exe = OperatingSystem.IsWindows() ? ".exe" : "";
        var candidates = new List<string>(dirs);
        var env = Environment.GetEnvironmentVariable("SHIFTDROP_FFMPEG_DIR");
        if (!string.IsNullOrEmpty(env)) candidates.Add(env);
        candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        foreach (var d in candidates)
        {
            try
            {
                var f = Path.Combine(d, "ffmpeg" + exe);
                var p = Path.Combine(d, "ffprobe" + exe);
                if (File.Exists(f) && File.Exists(p)) return new FFmpeg(f, p);
            }
            catch { /* bad PATH entry */ }
        }
        return new FFmpeg(null, null);
    }

    public bool HasEncoder(string name)
    {
        lock (_gate)
        {
            if (_encoders == null)
            {
                _encoders = new HashSet<string>(StringComparer.Ordinal);
                try
                {
                    var text = Capture(FfmpegPath!, ["-hide_banner", "-encoders"], 15000);
                    foreach (var line in text.Split('\n'))
                    {
                        var t = line.Trim();
                        // " V....D libx264   description"
                        var parts = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && parts[0].Length == 6 && parts[1] != "=")
                            _encoders.Add(parts[1]);
                    }
                }
                catch { }
            }
            return _encoders.Contains(name);
        }
    }

    /// <summary>The first H.264 encoder that actually works on this machine.</summary>
    public string H264
    {
        get
        {
            lock (_gate)
            {
                if (_h264 != null) return _h264;
            }
            string pick = "mpeg4";
            foreach (var enc in new[] { "libx264", "h264_nvenc", "h264_qsv", "h264_amf", "h264_mf" })
            {
                if (!HasEncoder(enc)) continue;
                if (TestEncode(enc)) { pick = enc; break; }
            }
            lock (_gate) _h264 = pick;
            return pick;
        }
    }

    bool TestEncode(string encoder)
    {
        try
        {
            var psi = Psi(FfmpegPath!, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i",
                "color=c=black:s=320x240:d=0.3", "-pix_fmt", "yuv420p", "-c:v", encoder, "-f", "null", "-"]);
            using var p = Process.Start(psi)!;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            if (!p.WaitForExit(20000)) { try { p.Kill(true); } catch { } return false; }
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>Video encoder arguments for H.264 at roughly the given bitrate (0 = quality based).</summary>
    public List<string> H264Args(long targetBitrate, bool smaller = false)
    {
        var enc = H264;
        var a = new List<string> { "-c:v", enc };
        if (enc == "libx264")
        {
            a.AddRange(["-preset", "veryfast", "-crf", smaller ? "28" : "20"]);
        }
        else if (enc == "mpeg4")
        {
            a.AddRange(["-q:v", smaller ? "6" : "3"]);
        }
        else
        {
            var br = targetBitrate > 0 ? targetBitrate : 6_000_000;
            if (smaller) br = Math.Max(400_000, br / 2);
            a.AddRange(["-b:v", br.ToString(CultureInfo.InvariantCulture)]);
        }
        a.AddRange(["-pix_fmt", "yuv420p"]);
        return a;
    }

    public async Task<MediaInfo> InfoAsync(string path, CancellationToken ct)
    {
        var json = await CaptureAsync(FfprobePath!,
            ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", path], ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        double dur = 0; long br = 0;
        if (root.TryGetProperty("format", out var fmt))
        {
            dur = Num(fmt, "duration");
            br = (long)Num(fmt, "bit_rate");
        }
        int w = 0, h = 0; string? vc = null, ac = null, pix = null; double fps = 0;
        bool hasV = false, hasA = false;
        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var s in streams.EnumerateArray())
            {
                var type = Str(s, "codec_type");
                bool attached = s.TryGetProperty("disposition", out var disp) &&
                                disp.TryGetProperty("attached_pic", out var ap) && ap.GetInt32() == 1;
                if (type == "video" && !hasV && !attached)
                {
                    hasV = true;
                    vc = Str(s, "codec_name");
                    pix = Str(s, "pix_fmt");
                    w = (int)Num(s, "width");
                    h = (int)Num(s, "height");
                    fps = Rate(Str(s, "avg_frame_rate")) is var r && r > 0 ? r : Rate(Str(s, "r_frame_rate"));
                    if (dur <= 0) dur = Num(s, "duration");
                }
                else if (type == "video" && attached && w == 0)
                {
                    // cover art only: remember size for image-like use, but no video stream
                    w = (int)Num(s, "width"); h = (int)Num(s, "height");
                }
                else if (type == "audio" && !hasA)
                {
                    hasA = true;
                    ac = Str(s, "codec_name");
                    if (dur <= 0) dur = Num(s, "duration");
                }
            }
        }
        return new MediaInfo(dur, w, h, vc, ac, pix, br, fps, hasV, hasA);
    }

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static double Num(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String &&
            double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        return 0;
    }

    static double Rate(string? r)
    {
        if (string.IsNullOrEmpty(r)) return 0;
        var p = r.Split('/');
        if (p.Length == 2 && double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var a) &&
            double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && b != 0) return a / b;
        return 0;
    }

    /// <summary>Runs ffmpeg, reporting progress against <paramref name="durationSec"/>.</summary>
    public async Task RunAsync(IEnumerable<string> args, double durationSec, IProgress<double>? progress,
        CancellationToken ct, Func<Stream, Task>? feedStdin = null)
    {
        var all = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };
        if (feedStdin == null) all.Add("-nostdin");
        all.AddRange(["-progress", "pipe:1", "-nostats"]);
        all.AddRange(args);
        var psi = Psi(FfmpegPath!, all);
        psi.RedirectStandardInput = feedStdin != null;
        using var p = new Process { StartInfo = psi };
        var err = new StringBuilder();
        p.Start();
        using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
        var errTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await p.StandardError.ReadLineAsync()) != null)
            {
                lock (err) { if (err.Length < 16000) err.AppendLine(line); }
            }
        });
        var outTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await p.StandardOutput.ReadLineAsync()) != null)
            {
                if (progress == null || durationSec <= 0) continue;
                if (line.StartsWith("out_time_us=") || line.StartsWith("out_time_ms="))
                {
                    if (long.TryParse(line.AsSpan(12), out var us) && us > 0)
                        progress.Report(Math.Clamp(us / 1e6 / durationSec, 0, 0.999));
                }
            }
        });
        if (feedStdin != null)
        {
            try { await feedStdin(p.StandardInput.BaseStream); }
            catch (IOException) { /* ffmpeg exited early; its error explains why */ }
            finally { try { p.StandardInput.Close(); } catch { } }
        }
        await p.WaitForExitAsync(CancellationToken.None);
        await Task.WhenAll(errTask, outTask);
        ct.ThrowIfCancellationRequested();
        if (p.ExitCode != 0)
            throw new EngineException(Tail(err.ToString()));
    }

    /// <summary>Runs ffmpeg and returns its raw stdout (used for decoded pixels).</summary>
    public async Task<byte[]> RunToBytesAsync(IEnumerable<string> args, CancellationToken ct)
    {
        var all = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin" };
        all.AddRange(args);
        var psi = Psi(FfmpegPath!, all);
        using var p = new Process { StartInfo = psi };
        p.Start();
        using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
        var errTask = p.StandardError.ReadToEndAsync();
        using var ms = new MemoryStream();
        await p.StandardOutput.BaseStream.CopyToAsync(ms, CancellationToken.None);
        await p.WaitForExitAsync(CancellationToken.None);
        var err = await errTask;
        ct.ThrowIfCancellationRequested();
        if (p.ExitCode != 0) throw new EngineException(Tail(err));
        return ms.ToArray();
    }

    async Task<string> CaptureAsync(string exe, IEnumerable<string> args, CancellationToken ct)
    {
        using var p = new Process { StartInfo = Psi(exe, args) };
        p.Start();
        using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync(CancellationToken.None);
        ct.ThrowIfCancellationRequested();
        if (p.ExitCode != 0) throw new EngineException(Tail(await e));
        return await o;
    }

    static string Capture(string exe, IEnumerable<string> args, int timeoutMs)
    {
        using var p = Process.Start(Psi(exe, args))!;
        var o = p.StandardOutput.ReadToEndAsync();
        p.StandardError.ReadToEnd();
        if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } }
        return o.Result;
    }

    static ProcessStartInfo Psi(string exe, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    static string Tail(string s)
    {
        s = s.Trim();
        var lines = s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var last = lines.TakeLast(3);
        var msg = string.Join(" · ", last);
        return string.IsNullOrEmpty(msg) ? "ffmpeg failed" : msg;
    }

    public static string Sec(double s) => s.ToString("0.###", CultureInfo.InvariantCulture);
}

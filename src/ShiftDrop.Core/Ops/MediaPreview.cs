using System.Globalization;
using ShiftDrop.Core.Engines;

namespace ShiftDrop.Core.Ops;

/// <summary>
/// What the trim, crop and channel editors show: waveforms, video frames and short audio previews.
/// All of it comes from ffmpeg in one quick pass, off the UI thread.
/// </summary>
public static class MediaPreview
{
    static string Inv(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// Loudness peaks (0..1) in <paramref name="buckets"/> slices across the whole file.
    /// <paramref name="channel"/>: -1 mixes everything to mono; 0 = left, 1 = right.
    /// </summary>
    public static async Task<float[]> Peaks(FFmpeg ff, string path, int buckets, double duration, int channel, CancellationToken ct)
    {
        // a low sample rate is plenty for a picture of the sound, and keeps long files quick
        int rate = duration > 1800 ? 2000 : duration > 300 ? 4000 : 8000;
        var args = new List<string> { "-i", path, "-map", "0:a:0", "-vn" };
        if (channel >= 0) args.AddRange(["-af", $"aformat=channel_layouts=stereo,pan=mono|c0=c{channel}"]);
        else args.AddRange(["-ac", "1"]);
        args.AddRange(["-ar", rate.ToString(CultureInfo.InvariantCulture), "-f", "s16le", "-acodec", "pcm_s16le", "-"]);
        var pcm = await ff.RunToBytesAsync(args, ct);
        int samples = pcm.Length / 2;
        var peaks = new float[buckets];
        if (samples == 0) return peaks;
        for (int b = 0; b < buckets; b++)
        {
            long from = (long)b * samples / buckets, to = Math.Max(from + 1, (long)(b + 1) * samples / buckets);
            int max = 0;
            for (long i = from; i < to && i < samples; i++)
            {
                int v = Math.Abs((short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8)));
                if (v > max) max = v;
            }
            peaks[b] = max / 32768f;
        }
        return peaks;
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int, int)> _sizes = new();

    /// <summary>One video frame at a time, scaled to fit, as BGRA pixels.</summary>
    public static async Task<(byte[] Bgra, int Width, int Height)> Frame(FFmpeg ff, string path, double seconds, int maxW, int maxH, CancellationToken ct)
    {
        if (!_sizes.TryGetValue(path, out var size))
        {
            var info = await ff.InfoAsync(path, ct);
            size = (info.Width > 0 ? info.Width : 16, info.Height > 0 ? info.Height : 9);
            _sizes[path] = size;
        }
        var (vw, vh) = size;
        // the probe can report a rotated video sideways; ffmpeg turns it upright when decoding
        var (w, h) = Fit(vw, vh, maxW, maxH);
        var bytes = await ff.RunToBytesAsync(["-ss", Inv(Math.Max(0, seconds)), "-i", path, "-frames:v", "1",
            "-vf", $"scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:color=black@0",
            "-f", "rawvideo", "-pix_fmt", "bgra", "-"], ct);
        if (bytes.Length != w * h * 4) throw new EngineException(L.T("Could not read a frame of this video", "تعذّرت قراءة إطار من هذا الفيديو"));
        return (bytes, w, h);
    }

    /// <summary>The size the video shows at (upright), for the crop editor.</summary>
    public static async Task<(int Width, int Height)> UprightSize(FFmpeg ff, string path, CancellationToken ct)
    {
        var bytes = await ff.RunToBytesAsync(["-i", path, "-frames:v", "1", "-vf", "scale=iw:ih", "-f", "image2pipe", "-c:v", "png", "-"], ct);
        // PNG header: width and height are big-endian at offsets 16 and 20
        if (bytes.Length > 24 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G')
            return ((bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19], (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23]);
        var info = await ff.InfoAsync(path, ct);
        return (info.Width, info.Height);
    }

    static (int W, int H) Fit(int w, int h, int maxW, int maxH)
    {
        double k = Math.Min((double)maxW / w, (double)maxH / h);
        int fw = Math.Max(2, (int)Math.Round(w * k) & ~1), fh = Math.Max(2, (int)Math.Round(h * k) & ~1);
        return (fw, fh);
    }

    /// <summary>A short WAV of part of the file (for "play selection"), with an optional audio filter.</summary>
    public static async Task<byte[]> Wav(FFmpeg ff, string path, double start, double length, string? filter, CancellationToken ct)
    {
        var args = new List<string> { "-ss", Inv(Math.Max(0, start)), "-t", Inv(Math.Clamp(length, 0.05, 600)), "-i", path, "-map", "0:a:0", "-vn" };
        if (!string.IsNullOrEmpty(filter)) args.AddRange(["-af", filter]);
        args.AddRange(["-map_metadata", "-1", "-ar", "44100", "-ac", "2", "-c:a", "pcm_s16le", "-f", "wav", "-"]);
        return FixWavSizes(await ff.RunToBytesAsync(args, ct));
    }

    /// <summary>
    /// ffmpeg writing to a pipe can't go back to fill in the sizes, so it leaves them as FFFFFFFF;
    /// Windows refuses to play such a WAV from memory. Writes the real sizes in.
    /// </summary>
    public static byte[] FixWavSizes(byte[] wav)
    {
        if (wav.Length < 12 || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F') return wav;
        BitConverter.TryWriteBytes(wav.AsSpan(4, 4), (uint)(wav.Length - 8));
        int pos = 12;
        while (pos + 8 <= wav.Length)
        {
            bool data = wav[pos] == 'd' && wav[pos + 1] == 'a' && wav[pos + 2] == 't' && wav[pos + 3] == 'a';
            if (data)
            {
                BitConverter.TryWriteBytes(wav.AsSpan(pos + 4, 4), (uint)(wav.Length - pos - 8));
                break;
            }
            uint size = BitConverter.ToUInt32(wav, pos + 4);
            pos += 8 + (int)Math.Min(size + (size & 1), (uint)wav.Length);
        }
        return wav;
    }

    /// <summary>Where the sound starts and stops (quiet ends cut off), from the peaks.</summary>
    public static (double Start, double End) SoundBounds(float[] peaks, double duration, float threshold = 0.02f)
    {
        if (peaks.Length == 0 || duration <= 0) return (0, duration);
        int first = Array.FindIndex(peaks, p => p > threshold);
        int last = Array.FindLastIndex(peaks, p => p > threshold);
        if (first < 0) return (0, duration);
        double slice = duration / peaks.Length;
        // keep a breath of silence on both sides
        return (Math.Max(0, first * slice - 0.1), Math.Min(duration, (last + 1) * slice + 0.1));
    }

    // ---------------- the operations the editors start ----------------

    /// <summary>Crops a video to "x,y,w,h" (pixels of the upright picture).</summary>
    public static Task VideoCrop(OpContext c)
    {
        var parts = c.Opt("crop").Split(',');
        if (parts.Length != 4 || !parts.All(p => int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
            throw new EngineException(L.T("No crop area was chosen", "لم يُحدَّد جزء للقص"));
        var v = parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        int x = Math.Max(0, v[0]) & ~1, y = Math.Max(0, v[1]) & ~1, w = Math.Max(2, v[2]) & ~1, h = Math.Max(2, v[3]) & ~1;
        return MediaOps.VideoTool(c, "cropped", $"crop={w}:{h}:{x}:{y}", null);
    }

    /// <summary>The pan filter for the channels editor: mono or stereo, with a level for each side (percent).</summary>
    public static string ChannelFilter(bool mono, int left, int right)
    {
        string L(double p) => (Math.Clamp(p, 0, 200) / 100.0).ToString("0.###", CultureInfo.InvariantCulture);
        // aformat first: mono files become two equal sides, surround is folded to stereo
        return mono
            ? $"aformat=channel_layouts=stereo,pan=mono|c0={L(left / 2.0)}*c0+{L(right / 2.0)}*c1"
            : $"aformat=channel_layouts=stereo,pan=stereo|c0={L(left)}*c0|c1={L(right)}*c1";
    }

    public static Task AudioChannels(OpContext c)
    {
        bool mono = c.Opt("mode", "mono") == "mono";
        int left = int.TryParse(c.Opt("left", "100"), out var l) ? l : 100;
        int right = int.TryParse(c.Opt("right", "100"), out var r) ? r : 100;
        return MediaOps.AudioTool(c, mono ? "mono" : "stereo", null, ["-af", ChannelFilter(mono, left, right)]);
    }
}

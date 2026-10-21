using System.Globalization;
using ShiftDrop.Core.Engines;

namespace ShiftDrop.Core.Ops;

/// <summary>Images, video and audio through ffmpeg.</summary>
public static class MediaOps
{
    static string Inv(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

    // ---------------- images ----------------

    const string Flatten = "format=rgba,split[sdA][sdB];[sdA]drawbox=c=white:t=fill[sdBg];[sdBg][sdB]overlay=format=auto,format=rgb24";

    static readonly HashSet<string> WritableImage = ["jpg", "jpeg", "png", "webp", "avif", "tiff", "tif", "bmp"];

    static List<string> ImageEncoder(FFmpeg ff, string ext, bool smaller)
    {
        switch (ext)
        {
            case "jpg" or "jpeg": return ["-c:v", "mjpeg", "-q:v", smaller ? "6" : "2"];
            case "png": return ["-c:v", "png", "-compression_level", "9", "-pred", "mixed"];
            case "webp": return ["-c:v", "libwebp", "-quality", smaller ? "60" : "90"];
            case "avif":
                if (ff.HasEncoder("libaom-av1"))
                    return ["-c:v", "libaom-av1", "-still-picture", "1", "-crf", smaller ? "40" : "28", "-b:v", "0", "-cpu-used", "6", "-pix_fmt", "yuv420p"];
                if (ff.HasEncoder("libsvtav1"))
                    return ["-c:v", "libsvtav1", "-crf", smaller ? "42" : "30", "-preset", "8", "-pix_fmt", "yuv420p"];
                throw new EngineException(L.T("AVIF encoding is not available in this ffmpeg build", "ترميز AVIF غير متاح في نسخة ffmpeg هذه"));
            case "tiff" or "tif": return ["-c:v", "tiff", "-compression_algo", "lzw"];
            case "bmp": return ["-c:v", "bmp"];
            case "ico": return ["-c:v", "png"];
            default: throw new EngineException("Unsupported image format: " + ext);
        }
    }

    /// <summary>Re-encodes one picture into <paramref name="ext"/>, with optional extra filters.</summary>
    static async Task Picture(OpContext c, string input, string output, string ext, string? filters, bool smaller, bool stripMeta = false)
    {
        var ff = c.Services.FFmpeg;
        var info = await ff.InfoAsync(input, c.Token);
        var chain = new List<string>();
        bool needFlatten = info.HasAlpha && ext is "jpg" or "jpeg" or "bmp" or "avif";
        if (filters != null) chain.Add(filters);
        if (ext == "ico") chain.Add("scale=w='min(256,iw)':h='min(256,ih)':force_original_aspect_ratio=decrease");
        if (ext == "avif") chain.Add("scale=trunc(iw/2)*2:trunc(ih/2)*2");
        var args = new List<string> { "-i", input };
        if (needFlatten)
            args.AddRange(["-filter_complex", "[0:v]" + Flatten + (chain.Count > 0 ? "," + string.Join(",", chain) : "")]);
        else if (chain.Count > 0)
            args.AddRange(["-vf", string.Join(",", chain)]);
        if (stripMeta) args.AddRange(["-map_metadata", "-1"]);
        args.AddRange(ImageEncoder(ff, ext, smaller));
        args.AddRange(["-frames:v", "1", "-update", "1", output]);
        await ff.RunAsync(args, 0, null, c.Token);
    }

    public static Task ImageTo(OpContext c, string ext) =>
        Picture(c, c.Input, c.Out($"{c.Base}.{ext}"), ext, null, false);

    static string SameOrJpg(string input)
    {
        var e = FileKinds.Ext(input);
        return WritableImage.Contains(e) ? e : "jpg";
    }

    public static Task ImageTool(OpContext c, string suffix, string? filter, bool smaller)
    {
        var ext = SameOrJpg(c.Input);
        return Picture(c, c.Input, c.Out($"{c.Base}-{suffix}.{ext}"), ext, filter, smaller);
    }

    public static async Task ImageStrip(OpContext c)
    {
        var ext = FileKinds.Ext(c.Input);
        var output = c.Out($"{c.Base}-clean.{(ext == "jpeg" ? "jpeg" : ext)}");
        if (FileKinds.IsJpeg(c.Input))
        {
            await File.WriteAllBytesAsync(output, Metadata.StripJpeg(await File.ReadAllBytesAsync(c.Input, c.Token)), c.Token);
            return;
        }
        if (ext == "png")
        {
            await File.WriteAllBytesAsync(output, Metadata.StripPng(await File.ReadAllBytesAsync(c.Input, c.Token)), c.Token);
            return;
        }
        var target = SameOrJpg(c.Input);
        await Picture(c, c.Input, c.Out($"{c.Base}-clean.{target}"), target, null, false, stripMeta: true);
    }

    /// <summary>Decodes a picture to RGBA, upright, with its real size.</summary>
    public static async Task<(byte[] Rgba, int Width, int Height)> DecodeRgba(FFmpeg ff, string input, string tempPng, CancellationToken ct, string? filter = null)
    {
        var args = new List<string> { "-i", input, "-frames:v", "1", "-update", "1" };
        if (filter != null) args.AddRange(["-vf", filter]);
        args.AddRange(["-c:v", "png", "-pix_fmt", "rgba", tempPng]);
        await ff.RunAsync(args, 0, null, ct);
        var info = await ff.InfoAsync(tempPng, ct);
        var rgba = await ff.RunToBytesAsync(["-i", tempPng, "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgba", "-"], ct);
        if (info.Width <= 0 || rgba.Length != info.Width * info.Height * 4)
            throw new EngineException(L.T("Could not read the picture ", "تعذّرت قراءة الصورة ") + Path.GetFileName(input));
        return (rgba, info.Width, info.Height);
    }

    /// <summary>Crops to "x,y,w,h" (pixels of the upright picture), keeping the format.</summary>
    public static Task ImageCrop(OpContext c)
    {
        var parts = c.Opt("crop").Split(',');
        if (parts.Length != 4 || !parts.All(p => int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
            throw new EngineException(L.T("No crop area was chosen", "لم يُحدَّد جزء للقص"));
        var v = parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        int x = Math.Max(0, v[0]), y = Math.Max(0, v[1]), w = Math.Max(1, v[2]), h = Math.Max(1, v[3]);
        var ext = SameOrJpg(c.Input);
        // avif and some encoders want even sizes
        if (ext == "avif") { w = Math.Max(2, w & ~1); h = Math.Max(2, h & ~1); }
        return Picture(c, c.Input, c.Out($"{c.Base}-cropped.{ext}"), ext, $"crop={w}:{h}:{x}:{y}", false);
    }

    public const string BackgroundModel = "silueta.onnx";
    static Onnx.Session? _bgSession;
    static readonly object BgGate = new();

    public static bool BackgroundAvailable => Onnx.FindFile(BackgroundModel) != null && Onnx.Available;

    static Onnx.Session BgSession()
    {
        lock (BgGate)
            return _bgSession ??= new Onnx.Session(Onnx.FindFile(BackgroundModel)
                ?? throw new EngineException(L.T("The background model is missing", "نموذج إزالة الخلفية غير موجود")));
    }

    /// <summary>Cuts the subject out with the U²-Net (silueta) model and saves a transparent PNG.</summary>
    public static async Task RemoveBackground(OpContext c)
    {
        var ff = c.Services.FFmpeg;
        const int S = 320;
        var (rgba, w, h) = await DecodeRgba(ff, c.Input, c.Temp("src.png"), c.Token);
        c.Progress.Report(0.15);
        var small = await ff.RunToBytesAsync(["-i", c.Temp("src.png"), "-vf", $"scale={S}:{S}:flags=bicubic",
            "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], c.Token);
        if (small.Length != S * S * 3) throw new EngineException("Could not scale the picture");
        File.Delete(c.Temp("src.png"));
        c.Progress.Report(0.25);

        var mask = await Task.Run(() => Mask(small, S), c.Token);
        c.Progress.Report(0.85);
        c.Token.ThrowIfCancellationRequested();

        // stretch the 320×320 mask back to the picture (bilinear) and use it as alpha
        var output = new byte[rgba.Length];
        Buffer.BlockCopy(rgba, 0, output, 0, rgba.Length);
        float sx = (float)S / w, sy = (float)S / h;
        Parallel.For(0, h, new ParallelOptions { CancellationToken = c.Token }, yy =>
        {
            float fy = Math.Clamp((yy + 0.5f) * sy - 0.5f, 0, S - 1);
            int y0 = (int)fy, y1 = Math.Min(S - 1, y0 + 1);
            float ty = fy - y0;
            for (int xx = 0; xx < w; xx++)
            {
                float fx = Math.Clamp((xx + 0.5f) * sx - 0.5f, 0, S - 1);
                int x0 = (int)fx, x1 = Math.Min(S - 1, x0 + 1);
                float tx = fx - x0;
                float m = (mask[y0 * S + x0] * (1 - tx) + mask[y0 * S + x1] * tx) * (1 - ty)
                        + (mask[y1 * S + x0] * (1 - tx) + mask[y1 * S + x1] * tx) * ty;
                int i = (yy * w + xx) * 4 + 3;
                output[i] = (byte)Math.Clamp(m * rgba[i] + 0.5f, 0, 255);
            }
        });

        var outPath = c.Out($"{c.Base}-no-background.png");
        await ff.RunAsync(["-f", "rawvideo", "-pix_fmt", "rgba", "-s", $"{w}x{h}", "-i", "-",
            "-frames:v", "1", "-c:v", "png", "-compression_level", "6", outPath], 0, null, c.Token,
            async stdin => await stdin.WriteAsync(output, c.Token));
    }

    /// <summary>Runs the model on an RGB picture of S×S and returns a 0..1 mask.</summary>
    static float[] Mask(byte[] rgb, int s)
    {
        ReadOnlySpan<float> mean = [0.485f, 0.456f, 0.406f], std = [0.229f, 0.224f, 0.225f];
        int n = s * s;
        float max = 1;
        foreach (var b in rgb) if (b > max) max = b;
        var input = new float[3 * n];
        for (int i = 0; i < n; i++)
            for (int ch = 0; ch < 3; ch++)
                input[ch * n + i] = (rgb[i * 3 + ch] / max - mean[ch]) / std[ch];
        var (data, _) = BgSession().Run(input, [1, 3, s, s]);
        if (data.Length < n) throw new EngineException("Unexpected model output");
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < n; i++) { lo = Math.Min(lo, data[i]); hi = Math.Max(hi, data[i]); }
        var mask = new float[n];
        float range = Math.Max(1e-6f, hi - lo);
        for (int i = 0; i < n; i++) mask[i] = (data[i] - lo) / range;
        return mask;
    }

    /// <summary>One PDF page per picture, in the order given.</summary>
    public static async Task ImagesToPdf(OpContext c, IReadOnlyList<string> inputs, string output)
    {
        var ff = c.Services.FFmpeg;
        var pages = new List<PdfImagePage>();
        for (int i = 0; i < inputs.Count; i++)
        {
            c.Token.ThrowIfCancellationRequested();
            var input = inputs[i];
            PdfImagePage? page = null;
            if (FileKinds.IsJpeg(input))
            {
                var bytes = await File.ReadAllBytesAsync(input, c.Token);
                var ji = PdfImagePage.JpegInfo(bytes);
                if (ji is { Comps: 1 or 3 }) page = PdfImagePage.FromJpeg(bytes);
            }
            if (page == null)
            {
                var png = c.Temp($"page{i}.png");
                await ff.RunAsync(["-i", input, "-frames:v", "1", "-update", "1", "-c:v", "png", png], 0, null, c.Token);
                var info = await ff.InfoAsync(png, c.Token);
                var rgba = await ff.RunToBytesAsync(["-i", png, "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgba", "-"], c.Token);
                if (info.Width <= 0 || rgba.Length != info.Width * info.Height * 4)
                    throw new EngineException(L.T("Could not read the picture ", "تعذّرت قراءة الصورة ") + Path.GetFileName(input));
                page = PdfImagePage.FromRgba(rgba, info.Width, info.Height);
                File.Delete(png);
            }
            pages.Add(page);
            c.Progress.Report((i + 1.0) / (inputs.Count + 1));
        }
        await using var fs = new FileStream(output, FileMode.CreateNew);
        PdfWriter.Write(fs, pages);
    }

    // ---------------- audio ----------------

    static List<string> AudioEncoder(string ext, bool smaller = false) => ext switch
    {
        "mp3" => ["-c:a", "libmp3lame", .. (smaller ? new[] { "-b:a", "96k" } : ["-q:a", "2"])],
        "m4a" or "aac" => ["-c:a", "aac", "-b:a", smaller ? "96k" : "192k"],
        "wav" => ["-c:a", "pcm_s16le"],
        "flac" => ["-c:a", "flac"],
        "ogg" or "oga" => ["-c:a", "libvorbis", "-q:a", smaller ? "2" : "5"],
        "opus" => ["-c:a", "libopus", "-b:a", smaller ? "64k" : "128k"],
        "aiff" or "aif" => ["-c:a", "pcm_s16be"],
        "wma" => ["-c:a", "wmav2", "-b:a", smaller ? "96k" : "192k"],
        _ => throw new EngineException("Unsupported audio format: " + ext),
    };

    static async Task<MediaInfo> NeedAudio(OpContext c, string input)
    {
        var info = await c.Services.FFmpeg.InfoAsync(input, c.Token);
        if (!info.HasAudio) throw new EngineException(L.T("This file has no sound", "هذا الملف بلا صوت"));
        return info;
    }

    public static async Task AudioTo(OpContext c, string ext)
    {
        var info = await NeedAudio(c, c.Input);
        var args = new List<string> { "-i", c.Input, "-map", "0:a:0", "-vn" };
        args.AddRange(AudioEncoder(ext));
        args.Add(c.Out($"{c.Base}.{ext}"));
        await c.Services.FFmpeg.RunAsync(args, info.Duration, c.Progress, c.Token);
    }

    static string AudioSameExt(string input) => FileKinds.Ext(input) switch
    {
        "oga" => "ogg",
        "aif" => "aiff",
        var e => e,
    };

    public static async Task AudioTool(OpContext c, string suffix, IEnumerable<string>? preInput, IEnumerable<string>? filterArgs,
        double durationScale = 1, bool smaller = false)
    {
        var info = await NeedAudio(c, c.Input);
        var ext = AudioSameExt(c.Input);
        if (smaller && ext is "wav" or "flac" or "aiff" or "wma") ext = "m4a";
        var args = new List<string>();
        if (preInput != null) args.AddRange(preInput);
        args.AddRange(["-i", c.Input, "-map", "0:a:0", "-vn"]);
        if (filterArgs != null) args.AddRange(filterArgs);
        args.AddRange(AudioEncoder(ext, smaller));
        args.Add(c.Out($"{c.Base}-{suffix}.{ext}"));
        await c.Services.FFmpeg.RunAsync(args, info.Duration * durationScale, c.Progress, c.Token);
    }

    public static async Task AudioTrim(OpContext c)
    {
        var info = await NeedAudio(c, c.Input);
        var (s, e) = Range(c, info.Duration);
        await AudioTool(c, "trimmed", ["-ss", Inv(s), "-to", Inv(e)], null, (e - s) / Math.Max(info.Duration, 0.001));
    }

    // ---------------- video ----------------

    static long Bitrate(MediaInfo i) => i.BitRate > 0 ? Math.Clamp((long)(i.BitRate * 0.95), 1_000_000, 20_000_000) : 6_000_000;

    const string EvenScale = "scale=trunc(iw/2)*2:trunc(ih/2)*2";

    static List<string> VideoEncoder(FFmpeg ff, string ext, MediaInfo info, bool smaller = false)
    {
        switch (ext)
        {
            case "webm":
                if (!ff.HasEncoder("libvpx-vp9")) throw new EngineException("VP9 encoding is not available");
                return ["-c:v", "libvpx-vp9", "-b:v", "0", "-crf", smaller ? "40" : "32", "-row-mt", "1",
                        "-deadline", "good", "-cpu-used", "4", "-pix_fmt", "yuv420p",
                        "-c:a", "libopus", "-b:a", smaller ? "96k" : "128k"];
            case "avi":
                return ["-c:v", "mpeg4", "-vtag", "xvid", "-q:v", smaller ? "6" : "3", "-c:a", "libmp3lame", "-q:a", "3"];
            case "wmv":
                return ["-c:v", "wmv2", "-b:v", (smaller ? Bitrate(info) / 2 : Bitrate(info)).ToString(CultureInfo.InvariantCulture),
                        "-c:a", "wmav2", "-b:a", "192k"];
            default: // mp4, mov, mkv
                var a = ff.H264Args(smaller ? Bitrate(info) / 2 : Bitrate(info), smaller);
                a.AddRange(["-c:a", "aac", "-b:a", smaller ? "128k" : "192k"]);
                if (ext is "mp4" or "mov") a.AddRange(["-movflags", "+faststart"]);
                return a;
        }
    }

    static bool CanCopy(MediaInfo i, string ext) => ext switch
    {
        "mkv" => i.VideoCodec != null,
        "mp4" or "mov" => i.VideoCodec is "h264" or "hevc" && i.AudioCodec is null or "aac" or "mp3",
        "webm" => i.VideoCodec is "vp8" or "vp9" or "av1" && i.AudioCodec is null or "opus" or "vorbis",
        _ => false,
    };

    public static async Task VideoTo(OpContext c, string ext)
    {
        var ff = c.Services.FFmpeg;
        var info = await ff.InfoAsync(c.Input, c.Token);
        var output = c.Out($"{c.Base}.{ext}");

        if (ext is "mp3" or "m4a")
        {
            if (!info.HasAudio) throw new EngineException(L.T("This video has no sound", "هذا الفيديو بلا صوت"));
            await ff.RunAsync(["-i", c.Input, "-map", "0:a:0", "-vn", .. AudioEncoder(ext), output], info.Duration, c.Progress, c.Token);
            return;
        }
        if (!info.HasVideo) throw new EngineException(L.T("No video stream found", "لا يوجد مسار فيديو"));
        if (ext == "gif")
        {
            await ff.RunAsync(["-i", c.Input, "-vf",
                "fps=12,scale=w='min(640,iw)':h=-1:flags=lanczos,split[sdA][sdB];[sdA]palettegen=stats_mode=diff[sdP];[sdB][sdP]paletteuse=dither=bayer:bayer_scale=4",
                "-loop", "0", output], info.Duration, c.Progress, c.Token);
            return;
        }
        if (CanCopy(info, ext))
        {
            var copy = new List<string> { "-i", c.Input };
            if (ext == "mkv") copy.AddRange(["-map", "0", "-c", "copy"]);
            else copy.AddRange(["-map", "0:v:0", "-map", "0:a:0?", "-c", "copy"]);
            if (ext is "mp4" or "mov")
            {
                copy.AddRange(["-movflags", "+faststart"]);
                if (info.VideoCodec == "hevc") copy.AddRange(["-tag:v", "hvc1"]);
            }
            copy.Add(output);
            try
            {
                await ff.RunAsync(copy, info.Duration, c.Progress, c.Token);
                return;
            }
            catch (EngineException)
            {
                if (File.Exists(output)) File.Delete(output); // fall back to re-encoding
            }
        }
        var args = new List<string> { "-i", c.Input, "-map", "0:v:0", "-map", "0:a:0?", "-vf", EvenScale };
        args.AddRange(VideoEncoder(ff, ext, info));
        args.Add(output);
        await ff.RunAsync(args, info.Duration, c.Progress, c.Token);
    }

    /// <summary>Animated GIF to a real video.</summary>
    public static async Task GifTo(OpContext c, string ext)
    {
        var ff = c.Services.FFmpeg;
        var info = await ff.InfoAsync(c.Input, c.Token);
        var args = new List<string> { "-i", c.Input, "-an", "-vf", EvenScale };
        var enc = VideoEncoder(ff, ext, info);
        // drop the audio codec arguments: GIFs have no sound
        for (int i = 0; i < enc.Count; i++)
        {
            if (enc[i] is "-c:a" or "-b:a" or "-q:a") { i++; continue; }
            args.Add(enc[i]);
        }
        args.Add(c.Out($"{c.Base}.{ext}"));
        await ff.RunAsync(args, info.Duration, c.Progress, c.Token);
    }

    static string VideoSameExt(string input) => FileKinds.Ext(input) switch
    {
        "mov" => "mov",
        "mkv" => "mkv",
        "webm" => "webm",
        _ => "mp4",
    };

    /// <summary>Re-encodes with filters. <paramref name="audioFilter"/> null keeps sound as is; "" drops it.</summary>
    public static async Task VideoTool(OpContext c, string suffix, string? videoFilter, string? audioFilter,
        IEnumerable<string>? preInput = null, double durationScale = 1, bool smaller = false, string? ext = null)
    {
        var ff = c.Services.FFmpeg;
        var info = await ff.InfoAsync(c.Input, c.Token);
        if (!info.HasVideo) throw new EngineException(L.T("No video stream found", "لا يوجد مسار فيديو"));
        ext ??= VideoSameExt(c.Input);
        var args = new List<string>();
        if (preInput != null) args.AddRange(preInput);
        args.AddRange(["-i", c.Input, "-map", "0:v:0"]);
        bool audio = info.HasAudio && audioFilter != "";
        if (audio) args.AddRange(["-map", "0:a:0"]);
        var vf = videoFilter == null ? EvenScale : videoFilter + "," + EvenScale;
        args.AddRange(["-vf", vf]);
        if (audio && !string.IsNullOrEmpty(audioFilter)) args.AddRange(["-af", audioFilter]);
        if (!audio) args.Add("-an");
        args.AddRange(VideoEncoder(ff, ext, info, smaller));
        args.Add(c.Out($"{c.Base}-{suffix}.{ext}"));
        await ff.RunAsync(args, info.Duration * durationScale, c.Progress, c.Token);
    }

    public static async Task VideoTrim(OpContext c)
    {
        var info = await c.Services.FFmpeg.InfoAsync(c.Input, c.Token);
        var (s, e) = Range(c, info.Duration);
        await VideoTool(c, "trimmed", null, null, ["-ss", Inv(s), "-to", Inv(e)], (e - s) / Math.Max(info.Duration, 0.001));
    }

    public static Task VideoCompress(OpContext c) =>
        VideoTool(c, "compressed", "scale=w='if(gt(ih,1080),-2,iw)':h='if(gt(ih,1080),1080,ih)'", null, smaller: true, ext: "mp4");

    /// <summary>Remove sound or metadata without re-encoding.</summary>
    public static async Task VideoCopy(OpContext c, string suffix, bool mute, bool stripMeta)
    {
        var ff = c.Services.FFmpeg;
        var info = await ff.InfoAsync(c.Input, c.Token);
        var ext = FileKinds.Ext(c.Input);
        var args = new List<string> { "-i", c.Input };
        if (mute) args.AddRange(["-map", "0:v", "-an"]);
        else args.AddRange(["-map", "0"]);
        if (stripMeta) args.AddRange(["-map_metadata", "-1", "-map_chapters", "-1"]);
        args.AddRange(["-c", "copy", c.Out($"{c.Base}-{suffix}.{ext}")]);
        await ff.RunAsync(args, info.Duration, c.Progress, c.Token);
    }

    public static async Task VideoSnapshot(OpContext c)
    {
        var ff = c.Services.FFmpeg;
        var info = await ff.InfoAsync(c.Input, c.Token);
        var at = info.Duration > 0 ? info.Duration / 2 : 0;
        await ff.RunAsync(["-ss", Inv(at), "-i", c.Input, "-frames:v", "1", "-update", "1", "-q:v", "2",
            c.Out($"{c.Base}-snapshot.jpg")], 0, null, c.Token);
    }

    public static async Task VideoSpeed(OpContext c, double factor)
    {
        var suffix = factor > 1 ? "fast" : "slow";
        var pts = Inv(1 / factor);
        // atempo accepts 0.5..2 per stage, which covers both presets
        await VideoTool(c, suffix, $"setpts={pts}*PTS", $"atempo={Inv(factor)}", durationScale: 1 / factor);
    }

    /// <summary>Joins clips end to end, scaled to the first clip's frame.</summary>
    public static async Task VideoJoin(OpContext c)
    {
        var ff = c.Services.FFmpeg;
        var infos = new List<MediaInfo>();
        foreach (var f in c.Inputs) infos.Add(await ff.InfoAsync(f, c.Token));
        if (infos.Any(i => !i.HasVideo)) throw new EngineException(L.T("Every file must be a video", "كل الملفات يجب أن تكون فيديو"));
        int w = infos[0].Width / 2 * 2, h = infos[0].Height / 2 * 2;
        bool audio = infos.All(i => i.HasAudio);
        var args = new List<string>();
        foreach (var f in c.Inputs) args.AddRange(["-i", f]);
        var graph = new System.Text.StringBuilder();
        for (int i = 0; i < c.Inputs.Count; i++)
        {
            graph.Append($"[{i}:v:0]scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p[v{i}];");
            if (audio) graph.Append($"[{i}:a:0]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo[a{i}];");
        }
        for (int i = 0; i < c.Inputs.Count; i++) graph.Append($"[v{i}]").Append(audio ? $"[a{i}]" : "");
        graph.Append($"concat=n={c.Inputs.Count}:v=1:a={(audio ? 1 : 0)}[vo]{(audio ? "[ao]" : "")}");
        args.AddRange(["-filter_complex", graph.ToString(), "-map", "[vo]"]);
        if (audio) args.AddRange(["-map", "[ao]"]);
        args.AddRange(VideoEncoder(ff, "mp4", infos[0]));
        if (!audio) args.Add("-an");
        args.Add(c.Out($"{c.Base}-joined.mp4"));
        await ff.RunAsync(args, infos.Sum(i => i.Duration), c.Progress, c.Token);
    }

    // ---------------- helpers ----------------

    /// <summary>Reads "start" and "end" options ("1:23", "83.5", "0:01:23").</summary>
    public static (double Start, double End) Range(OpContext c, double duration)
    {
        double s = ParseTime(c.Opt("start", "0")) ?? throw new EngineException(L.T("Start time is not valid", "وقت البداية غير صالح"));
        double e = c.Opt("end") is { Length: > 0 } es
            ? ParseTime(es) ?? throw new EngineException(L.T("End time is not valid", "وقت النهاية غير صالح"))
            : duration;
        if (duration > 0) e = Math.Min(e, duration);
        if (e <= s) throw new EngineException(L.T("End must be after start", "النهاية يجب أن تكون بعد البداية"));
        return (s, e);
    }

    public static double? ParseTime(string s)
    {
        s = s.Trim().Replace('٫', '.').Replace(',', '.');
        s = new string(s.Select(ch => ch is >= '٠' and <= '٩' ? (char)('0' + (ch - '٠')) : ch).ToArray());
        if (s.Length == 0) return null;
        var parts = s.Split(':');
        if (parts.Length > 3) return null;
        double total = 0;
        foreach (var p in parts)
        {
            if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0) return null;
            total = total * 60 + v;
        }
        return total;
    }
}

using ShiftDrop.Core.Ops;

namespace ShiftDrop.Core;

/// <summary>
/// Every conversion and tool, in wheel order. The wheel shows the ones that fit
/// every dragged file and that this machine can actually run.
/// </summary>
public static class Catalog
{
    public const int MaxSlots = 10;

    static readonly List<Operation> All = [];
    public static IReadOnlyList<Operation> Operations => All;

    static readonly FileKind[] Raster = [FileKind.Image, FileKind.Gif];
    static readonly FileKind[] Docs = [FileKind.Text, FileKind.Markdown, FileKind.Docx, FileKind.Rtf, FileKind.Html, FileKind.Epub];
    static readonly FileKind[] Everything = Enum.GetValues<FileKind>();

    static Catalog()
    {
        // ---- conversions: images ----
        Convert("img", "jpg", Raster, c => MediaOps.ImageTo(c, "jpg"));
        Convert("img", "png", Raster, c => MediaOps.ImageTo(c, "png"));
        Convert("img", "webp", Raster, c => MediaOps.ImageTo(c, "webp"));
        Convert("img", "avif", [FileKind.Image], c => MediaOps.ImageTo(c, "avif"));
        Convert("img", "tiff", [FileKind.Image], c => MediaOps.ImageTo(c, "tiff"));
        Convert("img", "bmp", [FileKind.Image], c => MediaOps.ImageTo(c, "bmp"));
        Convert("img", "ico", [FileKind.Image], c => MediaOps.ImageTo(c, "ico"));
        Convert("img", "pdf", [FileKind.Image], c => MediaOps.ImagesToPdf(c, c.Inputs, c.Out($"{c.Base}.pdf")));

        // ---- conversions: video (and animated GIF) ----
        Convert("vid", "mp4", [FileKind.Video], c => MediaOps.VideoTo(c, "mp4"), heavy: true);
        Convert("gif", "mp4", [FileKind.Gif], c => MediaOps.GifTo(c, "mp4"), heavy: true);
        Convert("vid", "mov", [FileKind.Video], c => MediaOps.VideoTo(c, "mov"), heavy: true);
        Convert("gif", "mov", [FileKind.Gif], c => MediaOps.GifTo(c, "mov"), heavy: true);
        Convert("vid", "mkv", [FileKind.Video], c => MediaOps.VideoTo(c, "mkv"), heavy: true);
        Convert("vid", "webm", [FileKind.Video], c => MediaOps.VideoTo(c, "webm"), heavy: true);
        Convert("gif", "webm", [FileKind.Gif], c => MediaOps.GifTo(c, "webm"), heavy: true);
        Convert("vid", "avi", [FileKind.Video], c => MediaOps.VideoTo(c, "avi"), heavy: true);
        Convert("vid", "wmv", [FileKind.Video], c => MediaOps.VideoTo(c, "wmv"), heavy: true);
        Convert("vid", "gif", [FileKind.Video], c => MediaOps.VideoTo(c, "gif"), heavy: true);
        Convert("vid", "mp3", [FileKind.Video], c => MediaOps.VideoTo(c, "mp3"), heavy: true);
        Convert("vid", "m4a", [FileKind.Video], c => MediaOps.VideoTo(c, "m4a"), heavy: true);

        // ---- conversions: audio ----
        foreach (var ext in new[] { "mp3", "m4a", "wav", "flac", "ogg", "opus", "aiff", "wma" })
        {
            var e = ext;
            Convert("aud", e, [FileKind.Audio], c => MediaOps.AudioTo(c, e), heavy: true);
        }

        // ---- conversions: PDF ----
        Convert("pdf", "jpg", [FileKind.Pdf], c => DocOps.PdfToImages(c, "jpg"), Needs.Pdfium | Needs.FFmpeg);
        Convert("pdf", "png", [FileKind.Pdf], c => DocOps.PdfToImages(c, "png"), Needs.Pdfium | Needs.FFmpeg);
        Convert("pdf", "txt", [FileKind.Pdf], DocOps.PdfToText, Needs.Pdfium);
        Convert("pdf", "docx", [FileKind.Pdf], c => DocOps.PdfToDoc(c, "docx"), Needs.Pdfium);
        Convert("pdf", "md", [FileKind.Pdf], c => DocOps.PdfToDoc(c, "md"), Needs.Pdfium);
        Convert("pdf", "html", [FileKind.Pdf], c => DocOps.PdfToDoc(c, "html"), Needs.Pdfium);
        Convert("pdf", "xlsx", [FileKind.Pdf], OfficeOps.PdfToSheet, Needs.Pdfium);
        Convert("pdf", "pptx", [FileKind.Pdf], OfficeOps.PdfToSlides, Needs.Pdfium | Needs.FFmpeg);
        Convert("pdf", "epub", [FileKind.Pdf], c => DocOps.PdfToDoc(c, "epub"), Needs.Pdfium);

        // ---- conversions: documents ----
        Convert("doc", "pdf", Docs, c => DocOps.DocTo(c, "pdf"), Needs.DocRenderer);
        Convert("doc", "docx", Docs, c => DocOps.DocTo(c, "docx"), Needs.None);
        Convert("doc", "md", Docs, c => DocOps.DocTo(c, "md"), Needs.None);
        Convert("doc", "html", Docs, c => DocOps.DocTo(c, "html"), Needs.None);
        Convert("doc", "txt", Docs, c => DocOps.DocTo(c, "txt"), Needs.None);
        Convert("doc", "epub", Docs, c => DocOps.DocTo(c, "epub"), Needs.None);

        // ---- conversions: spreadsheets ----
        foreach (var ext in new[] { "xlsx", "ods", "csv", "tsv" })
        {
            var e = ext;
            Convert("sheet", e, [FileKind.Sheet], c => OfficeOps.SheetTo(c, e), Needs.None);
        }
        Convert("sheet", "pdf", [FileKind.Sheet], c => OfficeOps.SheetTo(c, "pdf"), Needs.DocRenderer);
        Convert("sheet", "html", [FileKind.Sheet], c => OfficeOps.SheetTo(c, "html"), Needs.None);

        // ---- conversions: subtitles & archives ----
        Convert("sub", "srt", [FileKind.Subtitle], c => DocOps.SubtitleTo(c, "srt"), Needs.None);
        Convert("sub", "vtt", [FileKind.Subtitle], c => DocOps.SubtitleTo(c, "vtt"), Needs.None);
        Convert("sub", "txt", [FileKind.Subtitle], c => DocOps.SubtitleTo(c, "txt"), Needs.None);
        Convert("arc", "zip", [FileKind.Archive], c => DocOps.ArchiveTo(c, "zip"), Needs.None);
        Convert("arc", "tar", [FileKind.Archive], c => DocOps.ArchiveTo(c, "tar"), Needs.None);
        Convert("arc", "tgz", [FileKind.Archive], c => DocOps.ArchiveTo(c, "tgz"), Needs.None);

        // ---- tools: images ----
        All.Add(new Operation
        {
            Id = "img.crop", Group = OpGroup.Tool, En = "Crop", Ar = "قص الصورة", Kinds = [FileKind.Image],
            Run = MediaOps.ImageCrop, Needs = Needs.FFmpeg, MaxInputs = 1, Editor = "crop",
        });
        Tool("img.compress", "Compress", "ضغط", [FileKind.Image], c => MediaOps.ImageTool(c, "compressed", null, true));
        Tool("img.half", "Half size", "نصف الحجم", [FileKind.Image], c => MediaOps.ImageTool(c, "half", "scale=trunc(iw/2):trunc(ih/2)", false));
        Tool("img.rotate", "Rotate", "تدوير", [FileKind.Image], c => MediaOps.ImageTool(c, "rotated", "transpose=1", false));
        Tool("img.gray", "Black & white", "أبيض وأسود", [FileKind.Image], c => MediaOps.ImageTool(c, "bw", "hue=s=0", false));
        Tool("img.strip", "Remove metadata", "حذف البيانات", [FileKind.Image], MediaOps.ImageStrip);
        Tool("img.onepdf", "One PDF", "PDF واحد", [FileKind.Image], c => MediaOps.ImagesToPdf(c, c.Inputs, c.Out($"{c.Base}-images.pdf")),
            combine: true, min: 2);
        Tool("img.toslides", "Slides (PPTX)", "عرض شرائح", [FileKind.Image], OfficeOps.ImagesToSlides, combine: true);

        // ---- tools: video ----
        Tool("vid.compress", "Compress", "ضغط", [FileKind.Video], MediaOps.VideoCompress, heavy: true);
        Editor("vid.trim", "Trim", "قص المدة", [FileKind.Video], MediaOps.VideoTrim, "trim");
        Editor("vid.crop", "Crop", "قص الإطار", [FileKind.Video], MediaPreview.VideoCrop, "vcrop");
        Tool("vid.mute", "Remove sound", "حذف الصوت", [FileKind.Video], c => MediaOps.VideoCopy(c, "muted", true, false), heavy: true);
        Tool("vid.fast", "Speed ×2", "سرعة ×2", [FileKind.Video], c => MediaOps.VideoSpeed(c, 2), heavy: true);
        Tool("vid.slow", "Slow ×½", "بطء ×½", [FileKind.Video], c => MediaOps.VideoSpeed(c, 0.5), heavy: true);
        Tool("vid.rotate", "Rotate", "تدوير", [FileKind.Video], c => MediaOps.VideoTool(c, "rotated", "transpose=1", null), heavy: true);
        Tool("vid.snapshot", "Snapshot", "لقطة", [FileKind.Video], MediaOps.VideoSnapshot);
        Tool("vid.strip", "Remove metadata", "حذف البيانات", [FileKind.Video], c => MediaOps.VideoCopy(c, "clean", false, true), heavy: true);
        Tool("vid.join", "Join", "دمج", [FileKind.Video], MediaOps.VideoJoin, heavy: true, combine: true, min: 2);
        Tool("vid.split", "Split", "تقسيم", [FileKind.Video], OfficeOps.SplitMedia, heavy: true, fields: SplitFields);

        // ---- tools: audio ----
        Tool("aud.compress", "Compress", "ضغط", [FileKind.Audio], c => MediaOps.AudioTool(c, "compressed", null, null, smaller: true), heavy: true);
        Editor("aud.trim", "Trim", "قص", [FileKind.Audio], MediaOps.AudioTrim, "trim");
        Tool("aud.split", "Split", "تقسيم", [FileKind.Audio], OfficeOps.SplitMedia, heavy: true, fields: SplitFields);
        Tool("aud.normalize", "Normalize", "توحيد الصوت", [FileKind.Audio],
            c => MediaOps.AudioTool(c, "normalized", null, ["-af", "loudnorm=I=-16:TP=-1.5:LRA=11", "-ar", "48000"]), heavy: true);
        Editor("aud.channels", "Channels", "القنوات", [FileKind.Audio], MediaPreview.AudioChannels, "channels");
        Tool("aud.strip", "Remove metadata", "حذف البيانات", [FileKind.Audio],
            c => MediaOps.AudioTool(c, "clean", null, ["-map_metadata", "-1"]), heavy: true);

        // ---- tools: PDF, subtitles, archives ----
        Tool("pdf.merge", "Merge", "دمج", [FileKind.Pdf], DocOps.PdfMerge, Needs.Pdfium, combine: true, min: 2);
        Tool("pdf.split", "Split pages", "فصل الصفحات", [FileKind.Pdf], DocOps.PdfSplit, Needs.Pdfium);
        Tool("pdf.extract", "Extract pages", "استخراج صفحات", [FileKind.Pdf], DocOps.PdfExtract, Needs.Pdfium,
            fields: [new InputField("pages", "Pages (e.g. 1-3,5)", "الصفحات (مثل 1-3,5)", "1")]);
        Tool("pdf.rotate", "Rotate", "تدوير", [FileKind.Pdf], DocOps.PdfRotate, Needs.Pdfium);
        Tool("sub.shift", "Shift time", "إزاحة التوقيت", [FileKind.Subtitle], DocOps.SubtitleShift, Needs.None,
            fields: [new InputField("seconds", "Seconds (+ later, - earlier)", "الثواني (+ تأخير، - تقديم)", "1")]);
        Tool("arc.extract", "Extract", "استخراج", [FileKind.Archive], DocOps.ArchiveExtract, Needs.None);

        // ---- actions available for anything ----
        Add(new Operation
        {
            Id = "act.zip", Group = OpGroup.Action, En = "ZIP", Ar = "ضغط ZIP", Kinds = Everything,
            Combine = true, Run = DocOps.Zip,
        });
    }

    static InputField[] SplitFields =>
    [
        new("length", "Length of each part (e.g. 5:00)", "طول كل جزء (مثل 5:00)", "5:00"),
    ];

    static void Convert(string family, string ext, FileKind[] kinds, Func<OpContext, Task> run,
        Needs needs = Needs.FFmpeg, bool heavy = false) =>
        All.Add(new Operation
        {
            Id = $"{family}.to.{ext}", Group = OpGroup.Convert,
            En = ext.ToUpperInvariant(), Ar = ext.ToUpperInvariant(),
            Kinds = kinds, TargetExt = ext, Run = run, Needs = needs, Heavy = heavy,
        });

    /// <summary>A tool that opens an editor first (one file at a time).</summary>
    static void Editor(string id, string en, string ar, FileKind[] kinds, Func<OpContext, Task> run, string editor) =>
        All.Add(new Operation
        {
            Id = id, Group = OpGroup.Tool, En = en, Ar = ar, Kinds = kinds, Run = run,
            Needs = Needs.FFmpeg, Heavy = true, MaxInputs = 1, Editor = editor,
        });

    static void Tool(string id, string en, string ar, FileKind[] kinds, Func<OpContext, Task> run,
        Needs needs = Needs.FFmpeg, bool heavy = false, bool combine = false, int min = 1, InputField[]? fields = null) =>
        All.Add(new Operation
        {
            Id = id, Group = OpGroup.Tool, En = en, Ar = ar, Kinds = kinds, Run = run,
            Needs = needs, Heavy = heavy, Combine = combine, MinInputs = min, Fields = fields,
        });

    /// <summary>Lets the app add its own actions (copy path, terminal, ...).</summary>
    public static void Add(Operation op)
    {
        lock (All)
        {
            All.RemoveAll(o => o.Id == op.Id);
            // app actions go before the built-in ZIP action, in the order they are added
            int zip = All.FindIndex(o => o.Id == "act.zip");
            if (op.Group == OpGroup.Action && zip >= 0) All.Insert(zip, op);
            else All.Add(op);
        }
    }

    /// <summary>Operations of one group that fit every file and can run here, at most <see cref="MaxSlots"/>.</summary>
    public static List<Operation> For(IReadOnlyList<string> files, OpGroup group, Services services, ISet<string>? hidden = null)
    {
        var result = new List<Operation>();
        if (files.Count == 0) return result;
        var kinds = files.Select(FileKinds.Of).Distinct().ToList();
        var exts = files.Select(FileKinds.Ext).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var seen = new HashSet<string>();
        foreach (var op in All)
        {
            if (op.Group != group) continue;
            if (hidden != null && hidden.Contains(op.Id)) continue;
            if (!kinds.All(k => op.Kinds.Contains(k))) continue;
            if (files.Count < op.MinInputs || files.Count > op.MaxInputs) continue;
            if (!services.Has(op.Needs)) continue;
            if (op.TargetExt != null)
            {
                // nothing to do when every file already has this format
                if (exts.All(e => Same(e, op.TargetExt))) continue;
                // one slot per format even when several families offer it
                if (!seen.Add(op.TargetExt)) continue;
            }
            result.Add(op);
            if (result.Count == MaxSlots) break;
        }
        return result;
    }

    static bool Same(string a, string b) =>
        string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);

    static string Norm(string e) => e switch
    {
        "jpeg" or "jfif" => "jpg",
        "tif" => "tiff",
        "aif" => "aiff",
        "oga" => "ogg",
        "markdown" => "md",
        "htm" => "html",
        "xlsm" => "xlsx",
        _ => e,
    };
}

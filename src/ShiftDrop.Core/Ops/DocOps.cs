using System.Text.RegularExpressions;
using ShiftDrop.Core.Engines;

namespace ShiftDrop.Core.Ops;

/// <summary>PDF, documents, subtitles and archives.</summary>
public static partial class DocOps
{
    // ---------------- PDF ----------------

    public static async Task PdfToImages(OpContext c, string ext)
    {
        int n = Pdfium.PageCount(c.Input);
        if (n == 0) throw new EngineException(L.T("This PDF has no pages", "هذا الملف بلا صفحات"));
        string dir = c.OutDir;
        if (n > 1) { dir = c.Out($"{c.Base}-pages"); Directory.CreateDirectory(dir); }
        int digits = Math.Max(2, n.ToString().Length);
        for (int i = 0; i < n; i++)
        {
            c.Token.ThrowIfCancellationRequested();
            var (pixels, w, h) = await Task.Run(() => Pdfium.RenderPage(c.Input, i, 300), c.Token);
            var name = n == 1 ? $"{c.Base}.{ext}" : $"{c.Base}-p{(i + 1).ToString().PadLeft(digits, '0')}.{ext}";
            var args = new List<string> { "-f", "rawvideo", "-pix_fmt", "bgr0", "-s", $"{w}x{h}", "-i", "-", "-frames:v", "1", "-update", "1" };
            if (ext == "jpg") args.AddRange(["-c:v", "mjpeg", "-q:v", "2"]);
            else args.AddRange(["-c:v", "png"]);
            args.Add(Path.Combine(dir, name));
            await c.Services.FFmpeg.RunAsync(args, 0, null, c.Token, async s => await s.WriteAsync(pixels, c.Token));
            c.Progress.Report((i + 1.0) / n);
        }
    }

    static List<string> PdfText(OpContext c)
    {
        var pages = Pdfium.PageTexts(c.Input);
        if (pages.All(p => string.IsNullOrWhiteSpace(p)))
            throw new EngineException(L.T("This PDF has no text layer (it looks scanned)", "هذا الملف بلا نص قابل للنسخ (يبدو ممسوحًا ضوئيًا)"));
        return pages;
    }

    public static Task PdfToText(OpContext c)
    {
        var pages = PdfText(c);
        Documents.WriteText(c.Out($"{c.Base}.txt"), string.Join("\n\n", pages.Select(p => p.TrimEnd())) + "\n");
        return Task.CompletedTask;
    }

    public static Task PdfToDoc(OpContext c, string ext)
    {
        var blocks = Documents.FromPdfPages(PdfText(c));
        WriteDoc(c, blocks, ext);
        return Task.CompletedTask;
    }

    public static Task PdfMerge(OpContext c)
    {
        Pdfium.Compose(c.Inputs.Select(p => (p, (string?)null)), c.Out($"{c.Base}-merged.pdf"));
        return Task.CompletedTask;
    }

    public static Task PdfSplit(OpContext c)
    {
        int n = Pdfium.PageCount(c.Input);
        if (n < 2) throw new EngineException(L.T("This PDF has only one page", "هذا الملف صفحة واحدة فقط"));
        var dir = c.Out($"{c.Base}-pages");
        Directory.CreateDirectory(dir);
        int digits = Math.Max(2, n.ToString().Length);
        for (int i = 1; i <= n; i++)
        {
            c.Token.ThrowIfCancellationRequested();
            Pdfium.Compose([(c.Input, i.ToString())], Path.Combine(dir, $"{c.Base}-p{i.ToString().PadLeft(digits, '0')}.pdf"));
            c.Progress.Report((double)i / n);
        }
        return Task.CompletedTask;
    }

    public static Task PdfExtract(OpContext c)
    {
        var range = Regex.Replace(c.Opt("pages"), @"\s+", "");
        range = new string(range.Select(ch => ch is >= '٠' and <= '٩' ? (char)('0' + (ch - '٠')) : ch == '،' ? ',' : ch).ToArray());
        if (!RangeRx().IsMatch(range))
            throw new EngineException(L.T("Write pages like 1-3,5", "اكتب الصفحات بهذا الشكل: 1-3,5"));
        Pdfium.Compose([(c.Input, range)], c.Out($"{c.Base}-pages {range.Replace(',', ' ')}.pdf"));
        return Task.CompletedTask;
    }

    public static Task PdfRotate(OpContext c)
    {
        Pdfium.Rotate(c.Input, c.Out($"{c.Base}-rotated.pdf"), 1);
        return Task.CompletedTask;
    }

    [GeneratedRegex(@"^\d+(-\d+)?(,\d+(-\d+)?)*$")] private static partial Regex RangeRx();

    // ---------------- documents ----------------

    public static List<Block> Read(string path) => FileKinds.Of(path) switch
    {
        FileKind.Markdown => Documents.FromMarkdown(Documents.ReadText(path)),
        FileKind.Docx => Documents.FromDocx(path),
        _ => Documents.FromText(Documents.ReadText(path)),
    };

    public static Task DocTo(OpContext c, string ext)
    {
        WriteDoc(c, Read(c.Input), ext);
        return Task.CompletedTask;
    }

    static void WriteDoc(OpContext c, List<Block> blocks, string ext)
    {
        var output = c.Out($"{c.Base}.{ext}");
        switch (ext)
        {
            case "txt": Documents.WriteText(output, Documents.ToText(blocks)); break;
            case "md": Documents.WriteText(output, Documents.ToMarkdown(blocks)); break;
            case "html": Documents.WriteText(output, Documents.ToHtml(blocks, c.Base)); break;
            case "docx": Documents.ToDocx(blocks, output); break;
            case "pdf":
                var r = c.Services.DocRenderer ?? throw new EngineException(L.T("PDF export is not available here", "التصدير إلى PDF غير متاح هنا"));
                r.RenderPdf(blocks, c.Base, output, c.Token);
                break;
            default: throw new EngineException("Unsupported document format: " + ext);
        }
    }

    // ---------------- subtitles ----------------

    public static Task SubtitleTo(OpContext c, string ext)
    {
        var cues = Subtitles.Parse(Documents.ReadText(c.Input));
        if (cues.Count == 0) throw new EngineException(L.T("No subtitles found in this file", "لا توجد ترجمات في هذا الملف"));
        var text = ext switch { "srt" => Subtitles.ToSrt(cues), "vtt" => Subtitles.ToVtt(cues), _ => Subtitles.ToText(cues) };
        Documents.WriteText(c.Out($"{c.Base}.{ext}"), text);
        return Task.CompletedTask;
    }

    public static Task SubtitleShift(OpContext c)
    {
        var raw = c.Opt("seconds", "0").Replace('٫', '.').Replace(',', '.').Replace('−', '-');
        raw = new string(raw.Select(ch => ch is >= '٠' and <= '٩' ? (char)('0' + (ch - '٠')) : ch).ToArray());
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sec))
            throw new EngineException(L.T("Write seconds like 2.5 or -1", "اكتب الثواني مثل 2.5 أو -1"));
        var cues = Subtitles.Parse(Documents.ReadText(c.Input));
        var shifted = Subtitles.Shift(cues, TimeSpan.FromSeconds(sec));
        var ext = FileKinds.Ext(c.Input);
        Documents.WriteText(c.Out($"{c.Base}-shifted.{ext}"), ext == "vtt" ? Subtitles.ToVtt(shifted) : Subtitles.ToSrt(shifted));
        return Task.CompletedTask;
    }

    // ---------------- archives ----------------

    public static Task ArchiveExtract(OpContext c)
    {
        var tmp = c.Temp("x");
        Archives.Extract(c.Input, tmp, c.Services.SevenZipPath, c.Token);
        var top = Directory.GetFileSystemEntries(tmp);
        if (top.Length == 1 && Directory.Exists(top[0]))
            Directory.Move(top[0], c.Out(Path.GetFileName(top[0])));
        else if (FileKinds.Ext(c.Input) == "gz" && top.Length == 1)
            File.Move(top[0], c.Out(Path.GetFileName(top[0])));
        else
            Directory.Move(tmp, c.Out(c.Base));
        return Task.CompletedTask;
    }

    public static Task ArchiveTo(OpContext c, string ext)
    {
        var tmp = c.Temp("x");
        Archives.Extract(c.Input, tmp, c.Services.SevenZipPath, c.Token);
        var items = Directory.GetFileSystemEntries(tmp);
        var output = c.Out($"{c.Base}.{(ext == "tgz" ? "tar.gz" : ext)}");
        if (ext == "zip") Archives.Zip(items, output, c.Token);
        else Archives.Tar(items, output, ext == "tgz", c.Token);
        return Task.CompletedTask;
    }

    public static Task Zip(OpContext c)
    {
        var name = c.Inputs.Count == 1 ? FileKinds.BaseName(c.Input) : L.T("Archive", "أرشيف");
        if (c.Inputs.Count == 1 && Directory.Exists(c.Input)) name = Path.GetFileName(c.Input.TrimEnd('\\', '/'));
        Archives.Zip(c.Inputs, c.Out(name + ".zip"), c.Token, c.Progress);
        return Task.CompletedTask;
    }
}

using ShiftDrop.Core.Engines;

namespace ShiftDrop.Core.Ops;

/// <summary>Spreadsheets, PDF to Excel/PowerPoint, slideshows from pictures, and splitting media into parts.</summary>
public static class OfficeOps
{
    // ---------------- spreadsheets ----------------

    public static Task SheetTo(OpContext c, string ext)
    {
        var sheets = Sheets.Read(c.Input);
        switch (ext)
        {
            case "pdf":
                var r = c.Services.DocRenderer ?? throw new EngineException(L.T("PDF export is not available here", "التصدير إلى PDF غير متاح هنا"));
                r.RenderPdf(Sheets.ToBlocks(sheets), c.Base, c.Out($"{c.Base}.pdf"), c.Token);
                break;
            case "html":
                Documents.WriteText(c.Out($"{c.Base}.html"), Documents.ToHtml(Sheets.ToBlocks(sheets), c.Base));
                break;
            default:
                Sheets.Write(sheets, ext, c.Out, c.Base);
                break;
        }
        return Task.CompletedTask;
    }

    /// <summary>Rebuilds the tables of a PDF as an Excel sheet.</summary>
    public static Task PdfToSheet(OpContext c)
    {
        var rows = PdfTables.Rows(Pdfium.PageWords(c.Input));
        if (rows.Count == 0 || rows.All(r => r.All(string.IsNullOrEmpty)))
            throw new EngineException(L.T("This PDF has no text layer (it looks scanned)", "هذا الملف بلا نص قابل للنسخ (يبدو ممسوحًا ضوئيًا)"));
        Sheets.WriteXlsx([new Sheet(c.Base, rows)], c.Out($"{c.Base}.xlsx"));
        return Task.CompletedTask;
    }

    /// <summary>One slide per PDF page, as a sharp picture.</summary>
    public static async Task PdfToSlides(OpContext c)
    {
        int n = Pdfium.PageCount(c.Input);
        if (n == 0) throw new EngineException(L.T("This PDF has no pages", "هذا الملف بلا صفحات"));
        var slides = new List<(byte[], int, int)>();
        for (int i = 0; i < n; i++)
        {
            c.Token.ThrowIfCancellationRequested();
            var (pixels, w, h) = await Task.Run(() => Pdfium.RenderPage(c.Input, i, 200), c.Token);
            var jpg = c.Temp($"p{i}.jpg");
            await c.Services.FFmpeg.RunAsync(["-f", "rawvideo", "-pix_fmt", "bgr0", "-s", $"{w}x{h}", "-i", "-",
                "-frames:v", "1", "-update", "1", "-c:v", "mjpeg", "-q:v", "3", jpg], 0, null, c.Token,
                async s => await s.WriteAsync(pixels, c.Token));
            slides.Add((await File.ReadAllBytesAsync(jpg, c.Token), w, h));
            File.Delete(jpg);
            c.Progress.Report((i + 1.0) / (n + 1));
        }
        PptxWriter.Write(slides, c.Out($"{c.Base}.pptx"), c.Base);
    }

    /// <summary>Pictures in the order given, one per slide.</summary>
    public static async Task ImagesToSlides(OpContext c)
    {
        var ff = c.Services.FFmpeg;
        var slides = new List<(byte[], int, int)>();
        for (int i = 0; i < c.Inputs.Count; i++)
        {
            c.Token.ThrowIfCancellationRequested();
            var jpg = c.Temp($"s{i}.jpg");
            // flatten transparency on white, keep the size sensible for a slide
            await ff.RunAsync(["-i", c.Inputs[i], "-frames:v", "1", "-update", "1", "-filter_complex",
                "[0:v]format=rgba,split[a][b];[a]drawbox=c=white:t=fill[bg];[bg][b]overlay=format=auto,scale='min(2560,iw)':-2,format=yuvj444p",
                "-c:v", "mjpeg", "-q:v", "3", jpg], 0, null, c.Token);
            var info = await ff.InfoAsync(jpg, c.Token);
            slides.Add((await File.ReadAllBytesAsync(jpg, c.Token), info.Width, info.Height));
            c.Progress.Report((i + 1.0) / (c.Inputs.Count + 1));
        }
        var name = c.Inputs.Count == 1 ? c.Base : $"{c.Base}-slides";
        PptxWriter.Write(slides, c.Out($"{name}.pptx"), name);
    }

    // ---------------- split ----------------

    /// <summary>Cuts a video or audio file into parts of the given length, without re-encoding.</summary>
    public static async Task SplitMedia(OpContext c)
    {
        var ff = c.Services.FFmpeg;
        var info = await ff.InfoAsync(c.Input, c.Token);
        double part = MediaOps.ParseTime(c.Opt("length", "5:00")) ?? 0;
        if (part < 1) throw new EngineException(L.T("Write the part length like 5:00 or 90", "اكتب طول الجزء مثل 5:00 أو 90"));
        if (info.Duration > 0 && info.Duration <= part)
            throw new EngineException(L.T("The file is shorter than one part", "الملف أقصر من جزء واحد"));
        var ext = FileKinds.Ext(c.Input);
        var dir = c.Out($"{c.Base}-parts");
        Directory.CreateDirectory(dir);
        await ff.RunAsync(["-i", c.Input, "-map", "0", "-c", "copy", "-f", "segment", "-segment_time", FFmpeg.Sec(part),
            "-reset_timestamps", "1", Path.Combine(dir, $"{c.Base}-part%02d.{ext}")], info.Duration, c.Progress, c.Token);
        if (Directory.GetFiles(dir).Length == 0) throw new EngineException(L.T("No parts were made", "لم تُنشأ أي أجزاء"));
    }
}

using ShiftDrop.Core;
using ShiftDrop.Core.Engines;
using ShiftDrop.Native;
using ShiftDrop.UI;

namespace ShiftDrop;

/// <summary>
/// ShiftDrop.exe --preview &lt;folder&gt;: draws the wheel in its main states to PNG files,
/// so the design can be checked on a build machine without dragging anything.
/// </summary>
static class Preview
{
    public static int Run(string outDir)
    {
        Directory.CreateDirectory(outDir);
        Gdip.Start();
        W.OleInitialize(IntPtr.Zero);
        var tools = Path.Combine(AppContext.BaseDirectory, "tools");
        Pdfium.SearchDirs.Add(tools);
        Onnx.SearchDirs.Add(tools);
        var services = new Services { FFmpeg = FFmpeg.Locate(tools, AppContext.BaseDirectory) };
        AppActions.Register(null!); // the actions only use the app when they run

        // sample files: a real picture (so Explorer can make a thumbnail), a video, several files
        var samples = Path.Combine(outDir, "samples");
        Directory.CreateDirectory(samples);
        var photo = Path.Combine(samples, "Sunset at the sea.png");
        using (var pic = new Canvas())
        {
            pic.Ensure(480, 320);
            pic.FillRect(0, 0, 480, 320, 0xFFFDBA74);
            pic.GradientCircle(240, 360, 300, 0xFFF97316, 0xFFDB2777);
            pic.FillCircle(240, 190, 60, 0xFFFDE68A);
            pic.FillRect(0, 230, 480, 90, 0xFF1E3A8A);
            pic.SavePng(photo);
        }
        var video = Path.Combine(samples, "holiday.mp4");
        File.WriteAllBytes(video, []);
        var doc = Path.Combine(samples, "report.pdf");
        File.WriteAllBytes(doc, []);

        var wheel = new Wheel { ItemsFor = (f, g) => Catalog.For(f, g, services), Execute = (_, _) => { } };
        float scale = 1.5f;
        void Shot(string name, IReadOnlyList<string> files, OpGroup mode, int hover, Theme theme, bool arabic = false, bool waiting = false)
        {
            L.Arabic = arabic;
            try { wheel.RenderPreview(Path.Combine(outDir, name + ".png"), files, mode, hover, theme, scale, waiting); }
            catch (Exception e) { File.WriteAllText(Path.Combine(outDir, name + ".txt"), e.ToString()); }
        }

        Shot("01-image-convert-light", [photo], OpGroup.Convert, -1, Theme.Light);
        Shot("02-image-convert-hover-dark", [photo], OpGroup.Convert, 1, Theme.Dark);
        Shot("03-image-tools-light", [photo], OpGroup.Tool, 0, Theme.Light);
        Shot("04-image-tools-dark", [photo], OpGroup.Tool, 3, Theme.Dark);
        Shot("05-actions-dark", [photo], OpGroup.Action, 2, Theme.Dark);
        Shot("06-video-convert-light", [video], OpGroup.Convert, 4, Theme.Light);
        Shot("07-pdf-tools-dark-arabic", [doc], OpGroup.Tool, 1, Theme.Dark, arabic: true);
        Shot("08-many-files-light-arabic", [photo, video, doc], OpGroup.Action, -1, Theme.Light, arabic: true);
        Shot("09-waiting-dark", [], OpGroup.Convert, -1, Theme.Dark, waiting: true);
        // how long a frame takes to draw (the wheel animates at about 60 frames a second)
        try
        {
            L.Arabic = false;
            var light = wheel.MeasureFrameMs([photo], OpGroup.Convert, Theme.Light, scale);
            var dark = wheel.MeasureFrameMs([photo], OpGroup.Tool, Theme.Dark, scale);
            L.Arabic = true;
            var arabic = wheel.MeasureFrameMs([photo, video, doc], OpGroup.Action, Theme.Dark, scale);
            L.Arabic = false;
            File.WriteAllText(Path.Combine(outDir, "bench.txt"),
                FormattableString.Invariant($"frame ms at {scale}x: convert/light {light:0.00}, tools/dark {dark:0.00}, actions/arabic {arabic:0.00}"));
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(outDir, "bench.txt"), e.ToString()); }

        // the editors, with made-up sound and pictures
        try
        {
            var rnd = new Random(7);
            float[] Wave(int n, double phase) => Enumerable.Range(0, n).Select(i =>
                (float)Math.Clamp(0.15 + 0.75 * Math.Abs(Math.Sin(i / 9.0 + phase)) * (0.55 + 0.45 * Math.Sin(i / 37.0)) + rnd.NextDouble() * 0.12, 0, 1)).ToArray();
            var peaks = Wave(237, 0);
            for (int i = 0; i < 12; i++) peaks[i] *= 0.05f;
            var frames = Enumerable.Range(0, 8).Select(i => Scene(184, 104, i / 7.0)).ToList();
            frames.Add(Scene(800, 450, 0.75));
            TrimWindow.RenderPreview(Path.Combine(outDir, "20-trim-video-light.png"), true, scale, false, false, peaks, frames);
            TrimWindow.RenderPreview(Path.Combine(outDir, "21-trim-audio-dark-arabic.png"), false, scale, true, true, peaks, []);
            CropEditor.RenderPreview(Path.Combine(outDir, "22-crop-video-dark.png"), Scene(1280, 720, 0.4), true, scale, true, false);
            CropEditor.RenderPreview(Path.Combine(outDir, "23-crop-picture-light-arabic.png"), Scene(1280, 720, 0.9), false, scale, false, true);
            ChannelsWindow.RenderPreview(Path.Combine(outDir, "24-channels-light.png"), Wave(140, 0.3), Wave(140, 1.7), scale, false, false);
            ChannelsWindow.RenderPreview(Path.Combine(outDir, "25-channels-dark-arabic.png"), Wave(140, 0.3), Wave(140, 1.7), scale, true, true);
            L.Arabic = false;
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(outDir, "20-editors.txt"), e.ToString()); }

        // the Float card, light and dark
        foreach (var (mode, name) in new[] { ("light", "12-float-light"), ("dark", "13-float-dark") })
        {
            Theme.Override = mode;
            L.Arabic = mode == "dark";
            try
            {
                Thumbs.Preload(photo, (int)(46 * scale * 1.5f));
                FloatWindow.RenderPreview(mode == "dark" ? [photo, video] : [photo], Path.Combine(outDir, name + ".png"), scale);
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(outDir, name + ".txt"), e.ToString()); }
        }
        Theme.Override = "system";
        L.Arabic = false;

        // documents through "Microsoft Print to PDF": a long spreadsheet (header on every page) and a document with a table
        try
        {
            if (PrintPdfRenderer.Available())
            {
                var renderer = new PrintPdfRenderer();
                var rows = new List<string[]> { new[] { "#", "Product", "المنتج", "Qty", "Price", "Note" } };
                for (int i = 1; i <= 70; i++)
                    rows.Add([i.ToString(), $"Item {i}", i % 3 == 0 ? "تفاح أحمر طازج" : "قلم", (i * 3).ToString(), (i * 1.25).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                        i % 7 == 0 ? "A longer note that has to wrap inside its cell to show how rows grow" : ""]);
                var sheetPdf = Path.Combine(outDir, "sheet.pdf");
                renderer.RenderPdf(Sheets.ToBlocks([new Sheet("Prices", rows)]), "Prices", sheetPdf, CancellationToken.None);
                var docPdf = Path.Combine(outDir, "doc.pdf");
                var md = "# تقرير الأسعار\n\nهذا مستند عربي فيه جدول.\n\n| البند | الكمية | السعر |\n| --- | --- | --- |\n| ورق | 2 | 4.50 |\n| حبر | 1 | 19.99 |\n\n## English part\n\n| Name | Value |\n| --- | --- |\n| Alpha | 1 |\n| Beta | 22 |\n";
                renderer.RenderPdf(Documents.FromMarkdown(md), "Doc", docPdf, CancellationToken.None);
                PdfPages(sheetPdf, "10-sheet", 2);
                PdfPages(docPdf, "11-doc", 1);
            }
            else File.WriteAllText(Path.Combine(outDir, "10-no-printer.txt"), "Microsoft Print to PDF is not available on this machine");
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(outDir, "10-print.txt"), e.ToString()); }

        void PdfPages(string pdf, string name, int max)
        {
            int n = Math.Min(max, Pdfium.PageCount(pdf));
            for (int i = 0; i < n; i++)
            {
                var (pixels, w, h) = Pdfium.RenderPage(pdf, i, 80);
                SaveBgrx(pixels, w, h, Path.Combine(outDir, $"{name}-p{i + 1}.png"));
            }
        }

        File.WriteAllText(Path.Combine(outDir, "done.txt"), "ok");
        return 0;
    }

    /// <summary>A little landscape (sky, sun, sea) as BGRA pixels; <paramref name="t"/> moves the sun.</summary>
    static (byte[], int, int) Scene(int w, int h, double t)
    {
        var px = new byte[w * h * 4];
        double sunX = w * (0.15 + 0.7 * t), sunY = h * 0.42, sunR = h * 0.12;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double v = (double)y / h;
                (double r, double g, double b) col = v < 0.62
                    ? (90 + 160 * v, 140 + 90 * v * (1 - t), 220 - 120 * v)            // sky
                    : (20 + 40 * (v - 0.62), 70 + 80 * (v - 0.62), 110 + 60 * (v - 0.62)); // sea
                double d = Math.Sqrt((x - sunX) * (x - sunX) + (y - sunY) * (y - sunY));
                if (d < sunR) col = (255, 214, 120);
                else if (v >= 0.62 && Math.Abs(x - sunX) < sunR * (1.2 - (v - 0.62) * 2) && (y / 3) % 2 == 0) col = (240, 190, 110); // reflection
                int i = (y * w + x) * 4;
                px[i] = (byte)col.b; px[i + 1] = (byte)col.g; px[i + 2] = (byte)col.r; px[i + 3] = 255;
            }
        return (px, w, h);
    }

    static unsafe void SaveBgrx(byte[] pixels, int w, int h, string path)
    {
        fixed (byte* p = pixels)
        {
            if (Gdip.GdipCreateBitmapFromScan0(w, h, w * 4, 0x00022009 /*32bppRGB*/, (IntPtr)p, out var bmp) != 0) return;
            var png = new Guid("557CF406-1A04-11D3-9A73-0000F81EF32E");
            Gdip.GdipSaveImageToFile(bmp, path, ref png, IntPtr.Zero);
            Gdip.GdipDisposeImage(bmp);
        }
    }
}

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

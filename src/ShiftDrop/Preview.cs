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
        File.WriteAllText(Path.Combine(outDir, "done.txt"), "ok");
        return 0;
    }
}

using ShiftDrop.Core;
using ShiftDrop.Core.Engines;
using ShiftDrop.Native;

namespace ShiftDrop;

/// <summary>
/// Writes documents to PDF by printing to "Microsoft Print to PDF" (built into Windows 10 and 11).
/// Windows shapes Arabic and lays out right-to-left text itself, and the PDF keeps real, selectable text.
/// </summary>
public sealed class PrintPdfRenderer : IDocRenderer
{
    public const string Printer = "Microsoft Print to PDF";
    static readonly object Gate = new(); // one print job at a time

    const uint DT_LEFT = 0, DT_RIGHT = 2, DT_WORDBREAK = 0x10, DT_EXPANDTABS = 0x40, DT_NOPREFIX = 0x800,
        DT_CALCRECT = 0x400, DT_RTLREADING = 0x20000, DT_EDITCONTROL = 0x2000;
    const int HORZRES = 8, VERTRES = 10, LOGPIXELSY = 90;

    public static bool Available()
    {
        var dc = W.CreateDCW("WINSPOOL", Printer, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) return false;
        W.DeleteDC(dc);
        return true;
    }

    public unsafe void RenderPdf(IReadOnlyList<Block> blocks, string title, string outputPath, CancellationToken ct)
    {
        lock (Gate)
        {
            var dc = W.CreateDCW("WINSPOOL", Printer, null, IntPtr.Zero);
            if (dc == IntPtr.Zero)
                throw new EngineException(L.T("\"Microsoft Print to PDF\" is turned off. Turn it on in Windows Features.",
                    "طابعة \"Microsoft Print to PDF\" غير مفعّلة. فعّلها من ميزات Windows."));
            var fonts = new List<IntPtr>();
            bool started = false;
            try
            {
                int dpi = W.GetDeviceCaps(dc, LOGPIXELSY);
                int pageW = W.GetDeviceCaps(dc, HORZRES), pageH = W.GetDeviceCaps(dc, VERTRES);
                int margin = dpi * 3 / 4;
                int left = margin, right = pageW - margin, top = margin, bottom = pageH - margin;
                IntPtr Font(float pt, bool bold, string face = "Segoe UI")
                {
                    var f = W.CreateFontW(-(int)(pt * dpi / 72), 0, 0, 0, bold ? 700 : 400, 0, 0, 0, 1, 0, 0, 5, 0, face);
                    fonts.Add(f);
                    return f;
                }
                var body = Font(11, false); var h1 = Font(20, true); var h2 = Font(16, true); var h3 = Font(13, true); var code = Font(9.5f, false, "Consolas");

                fixed (char* name = title)
                fixed (char* output = outputPath)
                {
                    var info = new DOCINFOW { CbSize = sizeof(DOCINFOW), DocName = (IntPtr)name, Output = (IntPtr)output };
                    if (W.StartDocW(dc, ref info) <= 0) throw new EngineException(L.T("Printing to PDF failed", "فشلت الطباعة إلى PDF"));
                }
                started = true;
                W.StartPage(dc);
                W.SetBkMode(dc, 1); // transparent
                int y = top;
                bool pageHasContent = false;

                void NewPage()
                {
                    W.EndPage(dc); W.StartPage(dc); W.SetBkMode(dc, 1);
                    y = top; pageHasContent = false;
                }

                foreach (var b in blocks)
                {
                    ct.ThrowIfCancellationRequested();
                    if (b.Kind == BlockKind.PageBreak) { if (pageHasContent) NewPage(); continue; }
                    var (font, before, after) = b.Kind switch
                    {
                        BlockKind.Heading1 => (h1, dpi / 6, dpi / 12),
                        BlockKind.Heading2 => (h2, dpi / 7, dpi / 14),
                        BlockKind.Heading3 => (h3, dpi / 8, dpi / 16),
                        BlockKind.Code => (code, dpi / 16, dpi / 12),
                        _ => (body, 0, dpi / 14),
                    };
                    string text = b.Kind switch
                    {
                        BlockKind.Bullet => "•  " + b.Plain,
                        BlockKind.Numbered => b.Number + ".  " + b.Plain,
                        _ => b.Plain,
                    };
                    if (text.Length == 0) text = " ";
                    bool rtl = b.Kind != BlockKind.Code && b.Rtl;
                    uint flags = DT_WORDBREAK | DT_EXPANDTABS | DT_NOPREFIX | DT_EDITCONTROL | (rtl ? DT_RIGHT | DT_RTLREADING : DT_LEFT);
                    int indent = b.Kind is BlockKind.Bullet or BlockKind.Numbered ? dpi / 4 : 0;
                    W.SelectObject(dc, font);
                    if (pageHasContent) y += before;

                    // split blocks taller than a page into pieces that fit, word by word
                    string rest = text;
                    while (rest.Length > 0)
                    {
                        var rect = new RECT { Left = left + (rtl ? 0 : indent), Right = right - (rtl ? indent : 0), Top = y, Bottom = y };
                        int need = Measure(dc, rest, rect, flags);
                        if (y + need <= bottom)
                        {
                            rect.Bottom = y + need;
                            W.DrawTextW(dc, rest, rest.Length, ref rect, flags);
                            y += need + after;
                            pageHasContent = true;
                            break;
                        }
                        if (pageHasContent && need <= bottom - top) { NewPage(); W.SelectObject(dc, font); continue; }
                        // fit as many words as the space left allows
                        var words = rest.Split(' ');
                        int lo = 1, hi = words.Length, fit = 0;
                        while (lo <= hi)
                        {
                            int mid = (lo + hi) / 2;
                            if (y + Measure(dc, string.Join(' ', words, 0, mid), rect, flags) <= bottom) { fit = mid; lo = mid + 1; }
                            else hi = mid - 1;
                        }
                        if (fit == 0)
                        {
                            if (pageHasContent) { NewPage(); W.SelectObject(dc, font); continue; }
                            fit = 1; // a single giant word: let it overflow rather than loop
                        }
                        var part = string.Join(' ', words, 0, fit);
                        rect.Bottom = bottom;
                        W.DrawTextW(dc, part, part.Length, ref rect, flags);
                        rest = string.Join(' ', words, fit, words.Length - fit);
                        pageHasContent = true;
                        NewPage(); W.SelectObject(dc, font);
                    }
                }
                W.EndPage(dc);
                W.EndDoc(dc);
                started = false;
            }
            finally
            {
                if (started) W.AbortDoc(dc);
                W.DeleteDC(dc);
                foreach (var f in fonts) W.DeleteObject(f);
            }
            // the spooler writes the file asynchronously; wait for it to appear
            for (int i = 0; i < 300 && !(File.Exists(outputPath) && new FileInfo(outputPath).Length > 0 && Unlocked(outputPath)); i++)
            {
                ct.ThrowIfCancellationRequested();
                Thread.Sleep(100);
            }
            if (!File.Exists(outputPath)) throw new EngineException(L.T("The PDF was not created", "لم يُنشأ ملف PDF"));
        }
    }

    static int Measure(IntPtr dc, string text, RECT rect, uint flags)
    {
        rect.Bottom = rect.Top;
        W.DrawTextW(dc, text, text.Length, ref rect, flags | DT_CALCRECT);
        return rect.Bottom - rect.Top;
    }

    static bool Unlocked(string path)
    {
        try { using var f = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None); return true; }
        catch { return false; }
    }
}

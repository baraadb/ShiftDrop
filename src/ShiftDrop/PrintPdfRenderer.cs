using ShiftDrop.Core;
using ShiftDrop.Core.Engines;
using ShiftDrop.Native;

namespace ShiftDrop;

/// <summary>
/// Writes documents to PDF by printing to "Microsoft Print to PDF" (built into Windows 10 and 11).
/// Windows shapes Arabic and lays out right-to-left text itself, and the PDF keeps real, selectable text.
/// </summary>
public sealed partial class PrintPdfRenderer : IDocRenderer
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
                    if (b.Kind == BlockKind.Table)
                    {
                        if (pageHasContent) y += dpi / 12;
                        DrawTable(b, ref y, ref pageHasContent);
                        y += dpi / 10;
                        continue;
                    }
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

                // ---- tables: measured columns, wrapped cells, a header row repeated on every page ----
                void DrawTable(Block t, ref int ty, ref bool hasContent)
                {
                    var rows = t.Rows ?? [];
                    if (rows.Count == 0) return;
                    int cols = rows.Max(r => r.Length);
                    float pt = cols > 14 ? 7 : cols > 10 ? 8 : cols > 6 ? 9 : 10;
                    var cellFont = Font(pt, false);
                    var headFont = Font(pt, true);
                    int pad = Math.Max(2, dpi / 24), avail = right - left;
                    bool rtlTable = t.Rtl;

                    // natural widths from the text (first 300 rows), then fit the page
                    var natural = new double[cols];
                    W.SelectObject(dc, headFont);
                    for (int r = 0; r < Math.Min(rows.Count, 300); r++)
                    {
                        if (r == 1) W.SelectObject(dc, cellFont);
                        for (int ci = 0; ci < rows[r].Length; ci++)
                            foreach (var line in rows[r][ci].Split('\n'))
                                natural[ci] = Math.Max(natural[ci], TextWidth(dc, line) + pad * 2 + 1);
                    }
                    for (int ci = 0; ci < cols; ci++) natural[ci] = Math.Max(natural[ci], pad * 2 + dpi / 4);
                    double total = natural.Sum();
                    var widths = natural.Select(w => total > avail ? w * avail / total : w).Select(w => (int)Math.Floor(w)).ToArray();
                    int tableW = widths.Sum();
                    var xs = new int[cols + 1];
                    if (rtlTable) { xs[0] = right; for (int ci = 0; ci < cols; ci++) xs[ci + 1] = xs[ci] - widths[ci]; }
                    else { xs[0] = left; for (int ci = 0; ci < cols; ci++) xs[ci + 1] = xs[ci] + widths[ci]; }

                    var gridPen = CreatePen(0, Math.Max(1, dpi / 150), 0x00C8C8C8);
                    var headBrush = CreateSolidBrush(0x00F2F2F2);
                    try
                    {
                        int RowHeight(string[] row, IntPtr font)
                        {
                            W.SelectObject(dc, font);
                            int h = 0;
                            for (int ci = 0; ci < cols; ci++)
                            {
                                var text = ci < row.Length ? row[ci] : "";
                                if (text.Length == 0) continue;
                                var rect = new RECT { Left = 0, Right = widths[ci] - pad * 2, Top = 0, Bottom = 0 };
                                h = Math.Max(h, Measure(dc, text, rect, CellFlags(text)));
                            }
                            return Math.Max(h, (int)(pt * dpi / 72 * 1.25)) + pad * 2;
                        }

                        void DrawRow(string[] row, int top, int h, bool header)
                        {
                            int l = Math.Min(xs[0], xs[cols]), r = Math.Max(xs[0], xs[cols]);
                            if (header)
                            {
                                var bg = new RECT { Left = l, Right = r, Top = top, Bottom = top + h };
                                FillRect(dc, ref bg, headBrush);
                            }
                            W.SelectObject(dc, header ? headFont : cellFont);
                            for (int ci = 0; ci < cols; ci++)
                            {
                                var text = ci < row.Length ? row[ci] : "";
                                if (text.Length == 0) continue;
                                int cl = Math.Min(xs[ci], xs[ci + 1]), cr = Math.Max(xs[ci], xs[ci + 1]);
                                var rect = new RECT { Left = cl + pad, Right = cr - pad, Top = top + pad, Bottom = top + h - pad };
                                uint f = CellFlags(text);
                                if (!header && Sheets.TryNumber(text, out _)) f = (f & ~(DT_LEFT | DT_RIGHT)) | (rtlTable ? DT_LEFT : DT_RIGHT);
                                W.DrawTextW(dc, text, text.Length, ref rect, f);
                            }
                            var old = W.SelectObject(dc, gridPen);
                            MoveToEx(dc, l, top, IntPtr.Zero); LineTo(dc, r, top);
                            MoveToEx(dc, l, top + h, IntPtr.Zero); LineTo(dc, r, top + h);
                            for (int ci = 0; ci <= cols; ci++) { MoveToEx(dc, xs[ci], top, IntPtr.Zero); LineTo(dc, xs[ci], top + h); }
                            W.SelectObject(dc, old);
                        }

                        int headH = RowHeight(rows[0], headFont);
                        if (ty + headH * 2 > bottom && hasContent) { NewPage(); ty = y; }
                        DrawRow(rows[0], ty, headH, true);
                        ty += headH;
                        hasContent = true;
                        for (int r = 1; r < rows.Count; r++)
                        {
                            ct.ThrowIfCancellationRequested();
                            int h = RowHeight(rows[r], cellFont);
                            if (ty + h > bottom)
                            {
                                NewPage(); ty = y;
                                DrawRow(rows[0], ty, headH, true);
                                ty += headH;
                            }
                            DrawRow(rows[r], ty, Math.Min(h, bottom - ty), false);
                            ty += h;
                            hasContent = true;
                        }
                    }
                    finally { W.DeleteObject(gridPen); W.DeleteObject(headBrush); }
                }
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

    static uint CellFlags(string text) =>
        DT_WORDBREAK | DT_NOPREFIX | DT_EDITCONTROL | (Documents.IsRtl(text) ? DT_RIGHT | DT_RTLREADING : DT_LEFT);

    static int TextWidth(IntPtr dc, string text)
    {
        if (text.Length == 0) return 0;
        var rect = new RECT { Left = 0, Right = 100000, Top = 0, Bottom = 0 };
        W.DrawTextW(dc, text, text.Length, ref rect, 0x20 /*SINGLELINE*/ | DT_NOPREFIX | DT_EXPANDTABS | DT_CALCRECT);
        return rect.Right - rect.Left;
    }

    [System.Runtime.InteropServices.LibraryImport("gdi32.dll")] private static partial IntPtr CreatePen(int style, int width, uint color);
    [System.Runtime.InteropServices.LibraryImport("gdi32.dll")] private static partial IntPtr CreateSolidBrush(uint color);
    [System.Runtime.InteropServices.LibraryImport("gdi32.dll")] private static partial int MoveToEx(IntPtr dc, int x, int y, IntPtr old);
    [System.Runtime.InteropServices.LibraryImport("gdi32.dll")] private static partial int LineTo(IntPtr dc, int x, int y);
    [System.Runtime.InteropServices.LibraryImport("user32.dll")] private static partial int FillRect(IntPtr dc, ref RECT rect, IntPtr brush);

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

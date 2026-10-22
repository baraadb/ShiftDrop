using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace ShiftDrop.Core.Engines;

/// <summary>
/// Thin binding to PDFium (BSD licensed, bundled as pdfium.dll / libpdfium.so).
/// PDFium is not thread safe, so every call goes through one lock.
/// </summary>
public static unsafe partial class Pdfium
{
    const string Lib = "pdfium";
    static readonly object Gate = new();
    static bool _inited;
    static bool? _available;
    public static readonly List<string> SearchDirs = [];

    static Pdfium()
    {
        NativeLibrary.SetDllImportResolver(typeof(Pdfium).Assembly, Resolve);
    }

    static IntPtr Resolve(string name, Assembly asm, DllImportSearchPath? path)
    {
        if (name != Lib) return IntPtr.Zero;
        var files = OperatingSystem.IsWindows() ? new[] { "pdfium.dll" }
            : OperatingSystem.IsMacOS() ? new[] { "libpdfium.dylib" } : new[] { "libpdfium.so" };
        var dirs = new List<string>(SearchDirs) { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "tools") };
        var env = Environment.GetEnvironmentVariable("SHIFTDROP_PDFIUM_DIR");
        if (!string.IsNullOrEmpty(env)) dirs.Insert(0, env);
        foreach (var d in dirs)
            foreach (var f in files)
            {
                var full = Path.Combine(d, f);
                if (File.Exists(full) && NativeLibrary.TryLoad(full, out var h)) return h;
            }
        return IntPtr.Zero;
    }

    public static bool Available
    {
        get
        {
            lock (Gate)
            {
                if (_available is bool b) return b;
                try { Init(); _available = true; }
                catch { _available = false; }
                return _available.Value;
            }
        }
    }

    static void Init()
    {
        if (_inited) return;
        FPDF_InitLibrary();
        _inited = true;
    }

    public static int PageCount(string path)
    {
        lock (Gate)
        {
            using var d = Open(path);
            return FPDF_GetPageCount(d.Handle);
        }
    }

    /// <summary>Renders one page to BGRx pixels at the given DPI (white background).</summary>
    public static (byte[] Pixels, int Width, int Height) RenderPage(string path, int index, int dpi)
    {
        lock (Gate)
        {
            using var d = Open(path);
            var page = FPDF_LoadPage(d.Handle, index);
            if (page == IntPtr.Zero) throw new EngineException("Could not read PDF page " + (index + 1));
            try
            {
                double wpt = FPDF_GetPageWidthF(page), hpt = FPDF_GetPageHeightF(page);
                double scale = dpi / 72.0;
                // keep pages within 30 000 px a side and ~100 MP
                double maxScale = Math.Min(30000 / Math.Max(wpt, 1), 30000 / Math.Max(hpt, 1));
                maxScale = Math.Min(maxScale, Math.Sqrt(100_000_000 / Math.Max(wpt * hpt, 1)));
                scale = Math.Min(scale, maxScale);
                int w = Math.Max(1, (int)Math.Round(wpt * scale));
                int h = Math.Max(1, (int)Math.Round(hpt * scale));
                var bmp = FPDFBitmap_Create(w, h, 0);
                if (bmp == IntPtr.Zero) throw new EngineException("Not enough memory to render the page");
                try
                {
                    FPDFBitmap_FillRect(bmp, 0, 0, w, h, 0xFFFFFFFF);
                    FPDF_RenderPageBitmap(bmp, page, 0, 0, w, h, 0, 0x01 /*FPDF_ANNOT*/ | 0x800 /*FPDF_PRINTING*/);
                    var stride = FPDFBitmap_GetStride(bmp);
                    var buf = (byte*)FPDFBitmap_GetBuffer(bmp);
                    var pixels = new byte[w * h * 4];
                    fixed (byte* dst = pixels)
                        for (int y = 0; y < h; y++)
                            Buffer.MemoryCopy(buf + (long)y * stride, dst + (long)y * w * 4, w * 4, w * 4);
                    return (pixels, w, h);
                }
                finally { FPDFBitmap_Destroy(bmp); }
            }
            finally { FPDF_ClosePage(page); }
        }
    }

    /// <summary>Plain text of every page.</summary>
    public static List<string> PageTexts(string path)
    {
        lock (Gate)
        {
            using var d = Open(path);
            var n = FPDF_GetPageCount(d.Handle);
            var list = new List<string>(n);
            for (int i = 0; i < n; i++)
            {
                var page = FPDF_LoadPage(d.Handle, i);
                if (page == IntPtr.Zero) { list.Add(""); continue; }
                var tp = FPDFText_LoadPage(page);
                try
                {
                    int count = tp == IntPtr.Zero ? 0 : FPDFText_CountChars(tp);
                    if (count <= 0) { list.Add(""); continue; }
                    var buf = new ushort[count + 1];
                    fixed (ushort* b = buf)
                    {
                        int got = FPDFText_GetText(tp, 0, count, b);
                        var s = new string((char*)b, 0, Math.Max(0, got - 1));
                        list.Add(s.Replace("\r\n", "\n").Replace('\r', '\n').Replace("￾", "").Replace("\u0002", ""));
                    }
                }
                finally
                {
                    if (tp != IntPtr.Zero) FPDFText_ClosePage(tp);
                    FPDF_ClosePage(page);
                }
            }
            return list;
        }
    }

    /// <summary>A run of characters on one line of a page (PDF points, origin bottom-left).</summary>
    public sealed record Word(string Text, double Left, double Right, double Bottom, double Top)
    {
        public double Height => Top - Bottom;
        public double Middle => (Top + Bottom) / 2;
    }

    /// <summary>Every page's words with their positions (for finding tables).</summary>
    public static List<List<Word>> PageWords(string path)
    {
        lock (Gate)
        {
            using var d = Open(path);
            var n = FPDF_GetPageCount(d.Handle);
            var pages = new List<List<Word>>(n);
            for (int i = 0; i < n; i++)
            {
                var words = new List<Word>();
                pages.Add(words);
                var page = FPDF_LoadPage(d.Handle, i);
                if (page == IntPtr.Zero) continue;
                var tp = FPDFText_LoadPage(page);
                try
                {
                    int count = tp == IntPtr.Zero ? 0 : FPDFText_CountChars(tp);
                    var sb = new StringBuilder();
                    double l = 0, r = 0, b = 0, t = 0;
                    void End()
                    {
                        var text = sb.ToString().Trim();
                        if (text.Length > 0) words.Add(new Word(text, l, r, b, t));
                        sb.Clear();
                    }
                    for (int k = 0; k < count; k++)
                    {
                        uint u = FPDFText_GetUnicode(tp, k);
                        if (u is 0 or 0xFFFE or 2) continue;
                        double cl, cr, cb, ct;
                        if (FPDFText_GetCharBox(tp, k, &cl, &cr, &cb, &ct) == 0) continue;
                        bool space = u is ' ' or '\t' or '\r' or '\n' or 0xA0;
                        if (space) { if (u is '\r' or '\n') End(); else if (sb.Length > 0) sb.Append(' '); continue; }
                        double h = Math.Max(ct - cb, 1);
                        if (sb.Length > 0)
                        {
                            double lineH = Math.Max(t - b, 1);
                            bool sameLine = Math.Abs((cb + ct) / 2 - (b + t) / 2) < Math.Max(lineH, h) * 0.5;
                            double gap = cl - r;
                            // a new word when the line changes, or the gap is wider than about a space
                            // (glyph boxes are tight, so small gaps inside a word are normal; real spaces come as characters)
                            if (!sameLine || gap > Math.Max(lineH, h) * 0.6 || gap < -Math.Max(lineH, h) * 2) End();
                        }
                        if (sb.Length == 0) { l = cl; r = cr; b = cb; t = ct; }
                        else { l = Math.Min(l, cl); r = Math.Max(r, cr); b = Math.Min(b, cb); t = Math.Max(t, ct); }
                        sb.Append(char.ConvertFromUtf32((int)Math.Min(u, 0x10FFFF)));
                    }
                    End();
                }
                finally
                {
                    if (tp != IntPtr.Zero) FPDFText_ClosePage(tp);
                    FPDF_ClosePage(page);
                }
            }
            return pages;
        }
    }

    /// <summary>Writes a new PDF made of page ranges ("1-3,5"; null = all) taken from several files.</summary>
    public static void Compose(IEnumerable<(string Path, string? Range)> parts, string output)
    {
        lock (Gate)
        {
            var opened = new List<Doc>();
            var dest = FPDF_CreateNewDocument();
            if (dest == IntPtr.Zero) throw new EngineException("PDFium could not create a document");
            try
            {
                foreach (var (path, range) in parts)
                {
                    var src = Open(path);
                    opened.Add(src);
                    int at = FPDF_GetPageCount(dest);
                    if (FPDF_ImportPages(dest, src.Handle, range, at) == 0)
                        throw new EngineException(range == null
                            ? "Could not copy pages from " + System.IO.Path.GetFileName(path)
                            : L.T($"Page range \"{range}\" is not valid for this file", $"نطاق الصفحات \"{range}\" غير صالح لهذا الملف"));
                }
                Save(dest, output);
            }
            finally
            {
                FPDF_CloseDocument(dest);
                foreach (var o in opened) o.Dispose();
            }
        }
    }

    /// <summary>Rotates every page by 90 degrees clockwise (times <paramref name="quarterTurns"/>).</summary>
    public static void Rotate(string path, string output, int quarterTurns)
    {
        lock (Gate)
        {
            using var d = Open(path);
            int n = FPDF_GetPageCount(d.Handle);
            for (int i = 0; i < n; i++)
            {
                var page = FPDF_LoadPage(d.Handle, i);
                if (page == IntPtr.Zero) continue;
                var r = FPDFPage_GetRotation(page);
                FPDFPage_SetRotation(page, ((r + quarterTurns) % 4 + 4) % 4);
                FPDF_ClosePage(page);
            }
            Save(d.Handle, output);
        }
    }

    // ---- saving through FPDF_FILEWRITE ----

    [StructLayout(LayoutKind.Sequential)]
    struct FileWrite
    {
        public int Version;
        public delegate* unmanaged[Cdecl]<FileWrite*, void*, nuint, int> WriteBlock;
        public IntPtr Context; // our GCHandle, read back inside the callback
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    static int WriteBlock(FileWrite* self, void* data, nuint size)
    {
        try
        {
            var stream = (Stream)GCHandle.FromIntPtr(self->Context).Target!;
            stream.Write(new ReadOnlySpan<byte>(data, checked((int)size)));
            return 1;
        }
        catch { return 0; }
    }

    static void Save(IntPtr doc, string output)
    {
        using var fs = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
        var handle = GCHandle.Alloc(fs);
        try
        {
            var fw = new FileWrite
            {
                Version = 1,
                WriteBlock = &WriteBlock,
                Context = GCHandle.ToIntPtr(handle),
            };
            if (FPDF_SaveAsCopy(doc, &fw, 2 /*FPDF_NO_INCREMENTAL*/) == 0)
                throw new EngineException("PDFium could not save the PDF");
        }
        finally { handle.Free(); }
    }

    // ---- documents are loaded from memory so any path (Unicode, long) works ----

    sealed class Doc : IDisposable
    {
        public IntPtr Handle;
        public IntPtr Memory;
        public void Dispose()
        {
            if (Handle != IntPtr.Zero) FPDF_CloseDocument(Handle);
            if (Memory != IntPtr.Zero) Marshal.FreeHGlobal(Memory);
            Handle = Memory = IntPtr.Zero;
        }
    }

    static Doc Open(string path)
    {
        Init();
        var bytes = File.ReadAllBytes(path);
        var mem = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
        Marshal.Copy(bytes, 0, mem, bytes.Length);
        var h = FPDF_LoadMemDocument64(mem, (nuint)bytes.Length, null);
        if (h == IntPtr.Zero)
        {
            var e = FPDF_GetLastError();
            Marshal.FreeHGlobal(mem);
            throw new EngineException(e == 4
                ? L.T("This PDF is password protected", "هذا الملف محمي بكلمة مرور")
                : L.T("This file is not a readable PDF", "هذا الملف ليس PDF صالحًا"));
        }
        return new Doc { Handle = h, Memory = mem };
    }

    [LibraryImport(Lib)] private static partial void FPDF_InitLibrary();
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr FPDF_LoadMemDocument64(IntPtr data, nuint size, string? password);
    [LibraryImport(Lib)] private static partial uint FPDF_GetLastError();
    [LibraryImport(Lib)] private static partial int FPDF_GetPageCount(IntPtr doc);
    [LibraryImport(Lib)] private static partial IntPtr FPDF_LoadPage(IntPtr doc, int index);
    [LibraryImport(Lib)] private static partial float FPDF_GetPageWidthF(IntPtr page);
    [LibraryImport(Lib)] private static partial float FPDF_GetPageHeightF(IntPtr page);
    [LibraryImport(Lib)] private static partial void FPDF_ClosePage(IntPtr page);
    [LibraryImport(Lib)] private static partial void FPDF_CloseDocument(IntPtr doc);
    [LibraryImport(Lib)] private static partial IntPtr FPDFBitmap_Create(int w, int h, int alpha);
    [LibraryImport(Lib)] private static partial int FPDFBitmap_FillRect(IntPtr bmp, int l, int t, int w, int h, uint color);
    [LibraryImport(Lib)] private static partial void FPDF_RenderPageBitmap(IntPtr bmp, IntPtr page, int x, int y, int w, int h, int rotate, int flags);
    [LibraryImport(Lib)] private static partial IntPtr FPDFBitmap_GetBuffer(IntPtr bmp);
    [LibraryImport(Lib)] private static partial int FPDFBitmap_GetStride(IntPtr bmp);
    [LibraryImport(Lib)] private static partial void FPDFBitmap_Destroy(IntPtr bmp);
    [LibraryImport(Lib)] private static partial IntPtr FPDFText_LoadPage(IntPtr page);
    [LibraryImport(Lib)] private static partial int FPDFText_CountChars(IntPtr tp);
    [LibraryImport(Lib)] private static partial int FPDFText_GetText(IntPtr tp, int start, int count, ushort* result);
    [LibraryImport(Lib)] private static partial void FPDFText_ClosePage(IntPtr tp);
    [LibraryImport(Lib)] private static partial uint FPDFText_GetUnicode(IntPtr tp, int index);
    [LibraryImport(Lib)] private static partial int FPDFText_GetCharBox(IntPtr tp, int index, double* left, double* right, double* bottom, double* top);
    [LibraryImport(Lib)] private static partial IntPtr FPDF_CreateNewDocument();
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int FPDF_ImportPages(IntPtr dest, IntPtr src, string? range, int index);
    [LibraryImport(Lib)] private static partial int FPDF_SaveAsCopy(IntPtr doc, FileWrite* fw, uint flags);
    [LibraryImport(Lib)] private static partial int FPDFPage_GetRotation(IntPtr page);
    [LibraryImport(Lib)] private static partial void FPDFPage_SetRotation(IntPtr page, int rotate);
}

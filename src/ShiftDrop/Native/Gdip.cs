using System.Runtime.InteropServices;

namespace ShiftDrop.Native;

[StructLayout(LayoutKind.Sequential)] public struct RectF { public float X, Y, W, H; public RectF(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; } }

[StructLayout(LayoutKind.Sequential)]
struct GdiplusStartupInput { public uint Version; public IntPtr DebugCallback; public int SuppressBackgroundThread; public int SuppressExternalCodecs; }

/// <summary>GDI+ flat API (antialiased shapes and Unicode text with Arabic shaping).</summary>
public static unsafe partial class Gdip
{
    const string Lib = "gdiplus.dll";
    static IntPtr _token;

    public static void Start()
    {
        var input = new GdiplusStartupInput { Version = 1 };
        GdiplusStartup(out _token, ref input, IntPtr.Zero);
    }

    public static void Stop() { if (_token != IntPtr.Zero) GdiplusShutdown(_token); }

    [LibraryImport(Lib)] private static partial int GdiplusStartup(out IntPtr token, ref GdiplusStartupInput input, IntPtr output);
    [LibraryImport(Lib)] private static partial void GdiplusShutdown(IntPtr token);
    [LibraryImport(Lib)] public static partial int GdipCreateBitmapFromScan0(int w, int h, int stride, int format, IntPtr scan0, out IntPtr bitmap);
    [LibraryImport(Lib)] public static partial int GdipGetImageGraphicsContext(IntPtr image, out IntPtr graphics);
    [LibraryImport(Lib)] public static partial int GdipDisposeImage(IntPtr image);
    [LibraryImport(Lib)] public static partial int GdipDeleteGraphics(IntPtr g);
    [LibraryImport(Lib)] public static partial int GdipFlush(IntPtr g, int intention);
    [LibraryImport(Lib)] public static partial int GdipGraphicsClear(IntPtr g, uint argb);
    [LibraryImport(Lib)] public static partial int GdipSetSmoothingMode(IntPtr g, int mode);
    [LibraryImport(Lib)] public static partial int GdipSetPixelOffsetMode(IntPtr g, int mode);
    [LibraryImport(Lib)] public static partial int GdipSetTextRenderingHint(IntPtr g, int hint);
    [LibraryImport(Lib)] public static partial int GdipSetCompositingQuality(IntPtr g, int q);
    [LibraryImport(Lib)] public static partial int GdipResetWorldTransform(IntPtr g);
    [LibraryImport(Lib)] public static partial int GdipTranslateWorldTransform(IntPtr g, float dx, float dy, int order);
    [LibraryImport(Lib)] public static partial int GdipScaleWorldTransform(IntPtr g, float sx, float sy, int order);
    [LibraryImport(Lib)] public static partial int GdipCreatePath(int fillMode, out IntPtr path);
    [LibraryImport(Lib)] public static partial int GdipDeletePath(IntPtr path);
    [LibraryImport(Lib)] public static partial int GdipResetPath(IntPtr path);
    [LibraryImport(Lib)] public static partial int GdipAddPathArc(IntPtr path, float x, float y, float w, float h, float start, float sweep);
    [LibraryImport(Lib)] public static partial int GdipAddPathLine(IntPtr path, float x1, float y1, float x2, float y2);
    [LibraryImport(Lib)] public static partial int GdipClosePathFigure(IntPtr path);
    [LibraryImport(Lib)] public static partial int GdipCreateSolidFill(uint argb, out IntPtr brush);
    [LibraryImport(Lib)] public static partial int GdipSetSolidFillColor(IntPtr brush, uint argb);
    [LibraryImport(Lib)] public static partial int GdipDeleteBrush(IntPtr brush);
    [LibraryImport(Lib)] public static partial int GdipCreatePen1(uint argb, float width, int unit, out IntPtr pen);
    [LibraryImport(Lib)] public static partial int GdipSetPenColor(IntPtr pen, uint argb);
    [LibraryImport(Lib)] public static partial int GdipSetPenWidth(IntPtr pen, float width);
    [LibraryImport(Lib)] public static partial int GdipSetPenLineJoin(IntPtr pen, int join);
    [LibraryImport(Lib)] public static partial int GdipSetPenStartCap(IntPtr pen, int cap);
    [LibraryImport(Lib)] public static partial int GdipSetPenEndCap(IntPtr pen, int cap);
    [LibraryImport(Lib)] public static partial int GdipDeletePen(IntPtr pen);
    [LibraryImport(Lib)] public static partial int GdipFillPath(IntPtr g, IntPtr brush, IntPtr path);
    [LibraryImport(Lib)] public static partial int GdipDrawPath(IntPtr g, IntPtr pen, IntPtr path);
    [LibraryImport(Lib)] public static partial int GdipFillEllipse(IntPtr g, IntPtr brush, float x, float y, float w, float h);
    [LibraryImport(Lib)] public static partial int GdipDrawEllipse(IntPtr g, IntPtr pen, float x, float y, float w, float h);
    [LibraryImport(Lib)] public static partial int GdipFillRectangle(IntPtr g, IntPtr brush, float x, float y, float w, float h);
    [LibraryImport(Lib)] public static partial int GdipDrawLine(IntPtr g, IntPtr pen, float x1, float y1, float x2, float y2);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipCreateFontFamilyFromName(string name, IntPtr collection, out IntPtr family);
    [LibraryImport(Lib)] public static partial int GdipGetGenericFontFamilySansSerif(out IntPtr family);
    [LibraryImport(Lib)] public static partial int GdipDeleteFontFamily(IntPtr family);
    [LibraryImport(Lib)] public static partial int GdipCreateFont(IntPtr family, float size, int style, int unit, out IntPtr font);
    [LibraryImport(Lib)] public static partial int GdipDeleteFont(IntPtr font);
    [LibraryImport(Lib)] public static partial int GdipCreateStringFormat(int flags, ushort language, out IntPtr format);
    [LibraryImport(Lib)] public static partial int GdipDeleteStringFormat(IntPtr format);
    [LibraryImport(Lib)] public static partial int GdipSetStringFormatAlign(IntPtr format, int align);
    [LibraryImport(Lib)] public static partial int GdipSetStringFormatLineAlign(IntPtr format, int align);
    [LibraryImport(Lib)] public static partial int GdipSetStringFormatFlags(IntPtr format, int flags);
    [LibraryImport(Lib)] public static partial int GdipSetStringFormatTrimming(IntPtr format, int trimming);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipDrawString(IntPtr g, string text, int length, IntPtr font, ref RectF layout, IntPtr format, IntPtr brush);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipMeasureString(IntPtr g, string text, int length, IntPtr font, ref RectF layout, IntPtr format,
        out RectF box, out int codepoints, out int lines);

    [LibraryImport(Lib)] public static partial int GdipDrawArc(IntPtr g, IntPtr pen, float x, float y, float w, float h, float start, float sweep);
    [LibraryImport(Lib)] public static partial int GdipFillPie(IntPtr g, IntPtr brush, float x, float y, float w, float h, float start, float sweep);
    [LibraryImport(Lib)] public static partial int GdipDrawImageRect(IntPtr g, IntPtr image, float x, float y, float w, float h);
    [LibraryImport(Lib)] public static partial int GdipSetInterpolationMode(IntPtr g, int mode);
    [LibraryImport(Lib)] public static partial int GdipSetClipPath(IntPtr g, IntPtr path, int combine);
    [LibraryImport(Lib)] public static partial int GdipResetClip(IntPtr g);
    [LibraryImport(Lib)] public static partial int GdipCreateLineBrushFromRect(ref RectF rect, uint c1, uint c2, int mode, int wrap, out IntPtr brush);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GdipSaveImageToFile(IntPtr image, string file, ref Guid encoder, IntPtr parameters);

    public const int PixelFormat32bppPARGB = 0x000E200B;
}

/// <summary>Near = left / top, Far = right / bottom, on screen.</summary>
public enum Align { Near = 0, Center = 1, Far = 2 }

/// <summary>
/// A premultiplied 32-bit surface shared by GDI+ (drawing) and UpdateLayeredWindow (showing).
/// Coordinates are in physical pixels; callers multiply design sizes by <see cref="Scale"/>.
/// </summary>
public sealed class Canvas : IDisposable
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public float Scale { get; set; } = 1;
    public IntPtr MemDc { get; private set; }
    IntPtr _dib, _old, _bits, _bitmap;
    public IntPtr G { get; private set; }
    IntPtr _brush, _pen, _path;
    readonly Dictionary<(string, float, int), IntPtr> _fonts = [];
    readonly Dictionary<(Align, Align, bool, bool), IntPtr> _formats = [];
    static readonly Dictionary<string, IntPtr> Families = [];

    public void Ensure(int w, int h)
    {
        if (w == Width && h == Height && G != IntPtr.Zero) return;
        Release();
        Width = w; Height = h;
        var screen = W.GetDC(IntPtr.Zero);
        MemDc = W.CreateCompatibleDC(screen);
        W.ReleaseDC(IntPtr.Zero, screen);
        var bmi = new BITMAPINFOHEADER { Size = 40, Width = w, Height = -h, Planes = 1, BitCount = 32 };
        _dib = W.CreateDIBSection(MemDc, ref bmi, 0, out _bits, IntPtr.Zero, 0);
        _old = W.SelectObject(MemDc, _dib);
        Gdip.GdipCreateBitmapFromScan0(w, h, w * 4, Gdip.PixelFormat32bppPARGB, _bits, out _bitmap);
        Gdip.GdipGetImageGraphicsContext(_bitmap, out var g);
        G = g;
        Gdip.GdipSetSmoothingMode(G, 4);      // antialias
        Gdip.GdipSetPixelOffsetMode(G, 4);    // half pixel
        Gdip.GdipSetTextRenderingHint(G, 4);  // antialias (works on transparent surfaces)
        Gdip.GdipSetCompositingQuality(G, 2);
        if (_brush == IntPtr.Zero) Gdip.GdipCreateSolidFill(0, out _brush);
        if (_pen == IntPtr.Zero) { Gdip.GdipCreatePen1(0, 1, 2, out _pen); Gdip.GdipSetPenLineJoin(_pen, 2); Gdip.GdipSetPenStartCap(_pen, 2); Gdip.GdipSetPenEndCap(_pen, 2); }
        if (_path == IntPtr.Zero) Gdip.GdipCreatePath(0, out _path);
    }

    public void Clear() => Gdip.GdipGraphicsClear(G, 0);

    /// <summary>Saves the surface as a PNG (used by the preview renderer).</summary>
    public void SavePng(string path)
    {
        Flush();
        var png = new Guid("557CF406-1A04-11D3-9A73-0000F81EF32E");
        Gdip.GdipSaveImageToFile(_bitmap, path, ref png, IntPtr.Zero);
    }
    public void Flush() => Gdip.GdipFlush(G, 1);

    public void Transform(float cx, float cy, float scale)
    {
        Gdip.GdipResetWorldTransform(G);
        if (Math.Abs(scale - 1) < 0.001f) return;
        Gdip.GdipTranslateWorldTransform(G, cx, cy, 0);
        Gdip.GdipScaleWorldTransform(G, scale, scale, 0);
        Gdip.GdipTranslateWorldTransform(G, -cx, -cy, 0);
    }

    public void ResetTransform() => Gdip.GdipResetWorldTransform(G);

    IntPtr Brush(uint argb) { Gdip.GdipSetSolidFillColor(_brush, argb); return _brush; }
    IntPtr Pen(uint argb, float width) { Gdip.GdipSetPenColor(_pen, argb); Gdip.GdipSetPenWidth(_pen, width); return _pen; }

    public void FillCircle(float cx, float cy, float r, uint argb) =>
        Gdip.GdipFillEllipse(G, Brush(argb), cx - r, cy - r, r * 2, r * 2);

    public void StrokeCircle(float cx, float cy, float r, uint argb, float width) =>
        Gdip.GdipDrawEllipse(G, Pen(argb, width), cx - r, cy - r, r * 2, r * 2);

    public void FillRect(float x, float y, float w, float h, uint argb) => Gdip.GdipFillRectangle(G, Brush(argb), x, y, w, h);

    public void Arc(float cx, float cy, float r, float start, float sweep, uint argb, float width) =>
        Gdip.GdipDrawArc(G, Pen(argb, width), cx - r, cy - r, r * 2, r * 2, start, sweep);

    public void FillPie(float cx, float cy, float r, float start, float sweep, uint argb) =>
        Gdip.GdipFillPie(G, Brush(argb), cx - r, cy - r, r * 2, r * 2, start, sweep);

    public void Polyline(uint argb, float width, params float[] xy)
    {
        for (int i = 0; i + 3 < xy.Length; i += 2) Line(xy[i], xy[i + 1], xy[i + 2], xy[i + 3], argb, width);
    }

    /// <summary>A circle filled with a top-to-bottom gradient.</summary>
    public void GradientCircle(float cx, float cy, float r, uint top, uint bottom)
    {
        var rect = new RectF(cx - r, cy - r, r * 2, r * 2 + 1);
        if (Gdip.GdipCreateLineBrushFromRect(ref rect, top, bottom, 1 /*vertical*/, 0, out var b) != 0) { FillCircle(cx, cy, r, top); return; }
        Gdip.GdipFillEllipse(G, b, cx - r, cy - r, r * 2, r * 2);
        Gdip.GdipDeleteBrush(b);
    }

    /// <summary>Draws a GDI+ image into a rounded rectangle.</summary>
    public void Image(IntPtr image, float x, float y, float w, float h, float radius)
    {
        Gdip.GdipResetPath(_path);
        float d = Math.Min(radius * 2, Math.Min(w, h));
        if (d > 0.5f)
        {
            Gdip.GdipAddPathArc(_path, x, y, d, d, 180, 90);
            Gdip.GdipAddPathArc(_path, x + w - d, y, d, d, 270, 90);
            Gdip.GdipAddPathArc(_path, x + w - d, y + h - d, d, d, 0, 90);
            Gdip.GdipAddPathArc(_path, x, y + h - d, d, d, 90, 90);
            Gdip.GdipClosePathFigure(_path);
            Gdip.GdipSetClipPath(G, _path, 0);
        }
        Gdip.GdipSetInterpolationMode(G, 7);
        Gdip.GdipDrawImageRect(G, image, x, y, w, h);
        Gdip.GdipResetClip(G);
    }

    public void Line(float x1, float y1, float x2, float y2, uint argb, float width) =>
        Gdip.GdipDrawLine(G, Pen(argb, width), x1, y1, x2, y2);

    public void RoundRect(float x, float y, float w, float h, float r, uint fill, uint stroke = 0, float strokeWidth = 1)
    {
        r = Math.Min(r, Math.Min(w, h) / 2);
        Gdip.GdipResetPath(_path);
        float d = r * 2;
        Gdip.GdipAddPathArc(_path, x, y, d, d, 180, 90);
        Gdip.GdipAddPathArc(_path, x + w - d, y, d, d, 270, 90);
        Gdip.GdipAddPathArc(_path, x + w - d, y + h - d, d, d, 0, 90);
        Gdip.GdipAddPathArc(_path, x, y + h - d, d, d, 90, 90);
        Gdip.GdipClosePathFigure(_path);
        if (fill != 0) Gdip.GdipFillPath(G, Brush(fill), _path);
        if (stroke != 0) Gdip.GdipDrawPath(G, Pen(stroke, strokeWidth), _path);
    }

    /// <summary>A slice of a ring between two angles (degrees, clockwise from 3 o'clock), with rounded corners.</summary>
    public void RingSlice(float cx, float cy, float rOuter, float rInner, float a0, float a1, float corner, uint argb)
    {
        // shrink by the corner radius, then fill and stroke with a round-joined pen of twice that width
        float ro = rOuter - corner, ri = rInner + corner;
        float gapO = Deg(corner / ro), gapI = Deg(corner / ri);
        float s0 = a0 + gapO, s1 = a1 - gapO, t0 = a0 + gapI, t1 = a1 - gapI;
        if (s1 <= s0 || t1 <= t0) return;
        Gdip.GdipResetPath(_path);
        Gdip.GdipAddPathArc(_path, cx - ro, cy - ro, ro * 2, ro * 2, s0, s1 - s0);
        Gdip.GdipAddPathArc(_path, cx - ri, cy - ri, ri * 2, ri * 2, t1, -(t1 - t0));
        Gdip.GdipClosePathFigure(_path);
        Gdip.GdipFillPath(G, Brush(argb), _path);
        Gdip.GdipDrawPath(G, Pen(argb, corner * 2), _path);
    }

    static float Deg(float rad) => rad * 180f / MathF.PI;

    IntPtr Font(string family, float px, bool bold)
    {
        var key = (family, px, bold ? 1 : 0);
        if (_fonts.TryGetValue(key, out var f)) return f;
        if (!Families.TryGetValue(family, out var fam))
        {
            if (Gdip.GdipCreateFontFamilyFromName(family, IntPtr.Zero, out fam) != 0 || fam == IntPtr.Zero)
                Gdip.GdipGetGenericFontFamilySansSerif(out fam);
            Families[family] = fam;
        }
        Gdip.GdipCreateFont(fam, px, bold ? 1 : 0, 2 /*UnitPixel*/, out f);
        _fonts[key] = f;
        return f;
    }

    IntPtr Format(Align h, Align v, bool rtl, bool wrap)
    {
        var key = (h, v, rtl, wrap);
        if (_formats.TryGetValue(key, out var f)) return f;
        int flags = (rtl ? 0x1 /*DirectionRightToLeft*/ : 0) | (wrap ? 0 : 0x1000 /*NoWrap*/);
        Gdip.GdipCreateStringFormat(0, 0, out f);
        Gdip.GdipSetStringFormatFlags(f, flags);
        Gdip.GdipSetStringFormatAlign(f, (int)h);
        Gdip.GdipSetStringFormatLineAlign(f, (int)v);
        Gdip.GdipSetStringFormatTrimming(f, 3 /*EllipsisCharacter*/);
        _formats[key] = f;
        return f;
    }

    public void Text(string text, float x, float y, float w, float h, float px, uint argb, bool bold = false,
        Align align = Align.Center, Align valign = Align.Center, bool? rtl = null, bool wrap = false)
    {
        var rect = new RectF(x, y, w, h);
        bool isRtl = rtl ?? Core.L.Arabic;
        // callers think in screen terms (Near = left); GDI+ mirrors Near/Far for right-to-left text
        if (isRtl && align != Align.Center) align = align == Align.Near ? Align.Far : Align.Near;
        var family = bold ? "Segoe UI Semibold" : "Segoe UI";
        Gdip.GdipDrawString(G, text, text.Length, Font(family, px, false), ref rect, Format(align, valign, isRtl, wrap), Brush(argb));
    }

    public float MeasureWidth(string text, float px, bool bold = false)
    {
        var rect = new RectF(0, 0, 10000, 1000);
        var family = bold ? "Segoe UI Semibold" : "Segoe UI";
        Gdip.GdipMeasureString(G, text, text.Length, Font(family, px, false), ref rect, Format(Align.Near, Align.Near, false, false), out var box, out _, out _);
        return box.W;
    }

    void Release()
    {
        foreach (var f in _fonts.Values) Gdip.GdipDeleteFont(f);
        _fonts.Clear();
        if (G != IntPtr.Zero) { Gdip.GdipDeleteGraphics(G); G = IntPtr.Zero; }
        if (_bitmap != IntPtr.Zero) { Gdip.GdipDisposeImage(_bitmap); _bitmap = IntPtr.Zero; }
        if (MemDc != IntPtr.Zero)
        {
            W.SelectObject(MemDc, _old);
            W.DeleteObject(_dib);
            W.DeleteDC(MemDc);
            MemDc = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        Release();
        foreach (var f in _formats.Values) Gdip.GdipDeleteStringFormat(f);
        _formats.Clear();
        if (_brush != IntPtr.Zero) Gdip.GdipDeleteBrush(_brush);
        if (_pen != IntPtr.Zero) Gdip.GdipDeletePen(_pen);
        if (_path != IntPtr.Zero) Gdip.GdipDeletePath(_path);
        _brush = _pen = _path = IntPtr.Zero;
    }
}

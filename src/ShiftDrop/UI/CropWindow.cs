using System.Runtime.InteropServices;
using ShiftDrop.Core;
using ShiftDrop.Native;

namespace ShiftDrop.UI;

/// <summary>
/// Pick the part of a picture to keep: drag the frame, its corners or edges, or draw a new one.
/// Enter crops, Escape cancels. Works on a preview; the result is in full-size pixels.
/// </summary>
public sealed unsafe partial class CropWindow : Window
{
    readonly int _fullW, _fullH;           // the picture, upright, in pixels
    readonly IntPtr _bitmap, _pixels;      // GDI+ preview bitmap and its memory
    readonly Canvas _canvas = new();
    readonly string _name;
    float _s;
    bool _rtl, _done, _ok, _dark;

    // display: picture drawn at (_ix, _iy) with size (_dw, _dh)
    float _ix, _iy, _dw, _dh, _z;
    int _clientW, _clientH;
    float _barH;

    // crop rectangle in picture pixels
    double _cx, _cy, _cw, _ch;
    double _ratio; // 0 = free

    enum Drag { None, Move, New, Resize }
    Drag _drag;
    int _edges; // 1 left, 2 top, 4 right, 8 bottom
    double _ax, _ay, _grabX, _grabY, _startX, _startY;

    sealed record Chip(string Label, double Ratio) { public RectF Box; }
    readonly List<Chip> _chips;
    RectF _cancelBox, _okBox, _resetBox;
    int _hover = -1; // chip index, or 100 cancel, 101 ok, 102 reset

    CropWindow(string name, byte[] bgra, int pw, int ph, int fullW, int fullH)
    {
        _name = name;
        _fullW = fullW; _fullH = fullH;
        _pixels = Marshal.AllocHGlobal(bgra.Length);
        Marshal.Copy(bgra, 0, _pixels, bgra.Length);
        GdipCreateBitmapFromScan0(pw, ph, pw * 4, PixelFormat32bppARGB, _pixels, out _bitmap);
        _chips =
        [
            new(L.T("Free", "حر"), 0), new("1:1", 1), new("4:3", 4.0 / 3), new("3:2", 1.5),
            new("16:9", 16.0 / 9), new("9:16", 9.0 / 16),
        ];
        _cx = 0; _cy = 0; _cw = fullW; _ch = fullH;
    }

    /// <summary>Shows the editor; returns the area to keep in picture pixels, or null when cancelled.</summary>
    public static (int X, int Y, int W, int H)? Ask(string name, byte[] bgra, int previewW, int previewH, int fullW, int fullH)
    {
        var win = new CropWindow(name, bgra, previewW, previewH, fullW, fullH);
        try { return win.Run(); }
        finally { win.Free(); }
    }

    (int, int, int, int)? Run()
    {
        var at = W.Cursor();
        _s = W.ScaleAt(at);
        _rtl = L.Arabic;
        _dark = Theme.Current == Theme.Dark;
        var work = W.MonitorAt(at).Work;
        float margin = 24 * _s;
        _barH = 64 * _s;
        float maxW = work.Width * 0.82f - margin * 2, maxH = work.Height * 0.82f - margin * 2 - _barH - 40 * _s;
        _z = Math.Min(Math.Min(maxW / _fullW, maxH / _fullH), 3 * _s);
        _dw = _fullW * _z; _dh = _fullH * _z;
        _clientW = (int)Math.Ceiling(Math.Max(_dw + margin * 2, 640 * _s));
        _clientH = (int)Math.Ceiling(_dh + margin * 2 + _barH);
        _ix = (_clientW - _dw) / 2; _iy = margin;

        const uint style = W.WS_POPUP | W.WS_CAPTION | W.WS_SYSMENU;
        var r = new RECT { Right = _clientW, Bottom = _clientH };
        AdjustWindowRectEx(ref r, style, 0, W.WS_EX_DLGMODALFRAME);
        int ww = r.Width, wh = r.Height;
        int x = work.Left + (work.Width - ww) / 2, y = work.Top + (work.Height - wh) / 2;
        Create("ShiftDrop.Crop", W.WS_EX_DLGMODALFRAME | W.WS_EX_TOPMOST, style,
            $"ShiftDrop · {L.T("Crop", "قص الصورة")} · {_name}", x, y, ww, wh);
        if (_dark) { int on = 1; DwmSetWindowAttribute(Hwnd, 20, &on, 4); }

        W.ShowWindow(Hwnd, W.SW_SHOW);
        W.SetForegroundWindow(Hwnd);
        while (!_done && W.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            W.TranslateMessage(ref msg);
            W.DispatchMessageW(ref msg);
        }
        Destroy();
        if (!_ok) return null;
        int cx = (int)Math.Round(_cx), cy = (int)Math.Round(_cy);
        int cw = (int)Math.Round(_cw), ch = (int)Math.Round(_ch);
        cw = Math.Clamp(cw, 1, _fullW - cx); ch = Math.Clamp(ch, 1, _fullH - cy);
        return (cx, cy, cw, ch);
    }

    void Free()
    {
        if (_bitmap != IntPtr.Zero) Gdip.GdipDisposeImage(_bitmap);
        Marshal.FreeHGlobal(_pixels);
        _canvas.Dispose();
    }

    // ---------------- geometry ----------------

    float Dx(double px) => _ix + (float)(px * _z);
    float Dy(double py) => _iy + (float)(py * _z);
    double Px(float x) => (x - _ix) / _z;
    double Py(float y) => (y - _iy) / _z;
    const double MinSize = 8;

    int EdgesAt(float x, float y)
    {
        float tol = 10 * _s;
        float l = Dx(_cx), t = Dy(_cy), rr = Dx(_cx + _cw), b = Dy(_cy + _ch);
        if (x < l - tol || x > rr + tol || y < t - tol || y > b + tol) return 0;
        int e = 0;
        if (Math.Abs(x - l) <= tol) e |= 1;
        else if (Math.Abs(x - rr) <= tol) e |= 4;
        if (Math.Abs(y - t) <= tol) e |= 2;
        else if (Math.Abs(y - b) <= tol) e |= 8;
        return e;
    }

    bool InsideCrop(float x, float y) => x >= Dx(_cx) && x <= Dx(_cx + _cw) && y >= Dy(_cy) && y <= Dy(_cy + _ch);

    /// <summary>Largest rectangle of the ratio around the current centre that fits the picture.</summary>
    void ApplyRatio()
    {
        if (_ratio <= 0) return;
        double mx = _cx + _cw / 2, my = _cy + _ch / 2;
        double w = Math.Min(_fullW, _fullH * _ratio), h = w / _ratio;
        // keep roughly the size the user had, but never smaller than half the picture
        double keep = Math.Max(Math.Sqrt(_cw * _ch / (w * h)), 0.5);
        w *= Math.Min(1, keep); h = w / _ratio;
        _cw = w; _ch = h;
        _cx = Math.Clamp(mx - w / 2, 0, _fullW - w);
        _cy = Math.Clamp(my - h / 2, 0, _fullH - h);
    }

    /// <summary>Rectangle from a fixed anchor to the pointer, honouring the ratio and the picture bounds.</summary>
    void FromAnchor(double ax, double ay, double px, double py)
    {
        px = Math.Clamp(px, 0, _fullW); py = Math.Clamp(py, 0, _fullH);
        double w = Math.Abs(px - ax), h = Math.Abs(py - ay);
        double maxW = px >= ax ? _fullW - ax : ax, maxH = py >= ay ? _fullH - ay : ay;
        if (_ratio > 0)
        {
            if (w / Math.Max(h, 1e-9) > _ratio) h = w / _ratio; else w = h * _ratio;
            if (w > maxW) { w = maxW; h = w / _ratio; }
            if (h > maxH) { h = maxH; w = h * _ratio; }
        }
        w = Math.Max(w, MinSize); h = Math.Max(h, MinSize);
        _cx = px >= ax ? ax : ax - w;
        _cy = py >= ay ? ay : ay - h;
        _cw = w; _ch = h;
        _cx = Math.Clamp(_cx, 0, _fullW - _cw); _cy = Math.Clamp(_cy, 0, _fullH - _ch);
    }

    void ResizeEdge(double px, double py)
    {
        bool corner = (_edges & 5) != 0 && (_edges & 10) != 0;
        if (corner || _ratio <= 0)
        {
            // fixed point: the opposite side(s)
            double l = _startX, t = _startY, r = _startX + _grabX, b = _startY + _grabY; // stored at grab time
            double ax = (_edges & 1) != 0 ? r : l, ay = (_edges & 2) != 0 ? b : t;
            if (corner) { FromAnchor(ax, ay, px, py); return; }
            if ((_edges & 5) != 0)
            {
                double nl = (_edges & 1) != 0 ? Math.Clamp(Math.Min(px, r - MinSize), 0, r - MinSize) : l;
                double nr = (_edges & 4) != 0 ? Math.Clamp(Math.Max(px, l + MinSize), l + MinSize, _fullW) : r;
                _cx = nl; _cw = nr - nl;
            }
            else
            {
                double nt = (_edges & 2) != 0 ? Math.Clamp(Math.Min(py, b - MinSize), 0, b - MinSize) : t;
                double nb = (_edges & 8) != 0 ? Math.Clamp(Math.Max(py, t + MinSize), t + MinSize, _fullH) : b;
                _cy = nt; _ch = nb - nt;
            }
            return;
        }
        // one edge with a fixed ratio: grow from the opposite edge, centred on the other axis
        double l0 = _startX, t0 = _startY, r0 = _startX + _grabX, b0 = _startY + _grabY;
        if ((_edges & 5) != 0)
        {
            double ax = (_edges & 1) != 0 ? r0 : l0;
            double w = Math.Max(MinSize, (_edges & 1) != 0 ? ax - px : px - ax);
            w = Math.Min(w, (_edges & 1) != 0 ? ax : _fullW - ax);
            double h = w / _ratio;
            if (h > _fullH) { h = _fullH; w = h * _ratio; }
            double my = (t0 + b0) / 2;
            _cw = w; _ch = h;
            _cx = (_edges & 1) != 0 ? ax - w : ax;
            _cy = Math.Clamp(my - h / 2, 0, _fullH - h);
        }
        else
        {
            double ay = (_edges & 2) != 0 ? b0 : t0;
            double h = Math.Max(MinSize, (_edges & 2) != 0 ? ay - py : py - ay);
            h = Math.Min(h, (_edges & 2) != 0 ? ay : _fullH - ay);
            double w = h * _ratio;
            if (w > _fullW) { w = _fullW; h = w / _ratio; }
            double mx = (l0 + r0) / 2;
            _cw = w; _ch = h;
            _cy = (_edges & 2) != 0 ? ay - h : ay;
            _cx = Math.Clamp(mx - w / 2, 0, _fullW - w);
        }
    }

    // ---------------- drawing ----------------

    uint Bg => _dark ? 0xFF11151Cu : 0xFFE9EDF2u;
    uint Bar => _dark ? 0xFF1A202Bu : 0xFFFFFFFFu;
    uint ChipBg => _dark ? 0xFF262D3Bu : 0xFFEEF1F5u;
    uint ChipHover => _dark ? 0xFF323A4Au : 0xFFE2E7EEu;
    uint Text => _dark ? 0xFFF1F5F9u : 0xFF0B1220u;
    uint Sub => _dark ? 0xFF94A3B8u : 0xFF64748Bu;

    void Layout(Canvas c)
    {
        float s = _s, h = 34 * s, y = _clientH - _barH + (_barH - h) / 2, x = 16 * s;
        foreach (var chip in _chips)
        {
            float w = Math.Max(46 * s, c.MeasureWidth(chip.Label, 13 * s, true) + 24 * s);
            chip.Box = Mirror(new RectF(x, y, w, h));
            x += w + 6 * s;
        }
        float bw = 96 * s, rx = _clientW - 16 * s;
        _okBox = Mirror(new RectF(rx - bw, y, bw, h));
        _cancelBox = Mirror(new RectF(rx - bw * 2 - 8 * s, y, bw, h));
        float resetW = c.MeasureWidth(L.T("Reset", "إعادة"), 13 * s, true) + 24 * s;
        _resetBox = Mirror(new RectF(rx - bw * 2 - 16 * s - resetW, y, resetW, h));
    }

    RectF Mirror(RectF r) => _rtl ? new RectF(_clientW - r.X - r.W, r.Y, r.W, r.H) : r;

    void Render(IntPtr hdc)
    {
        var c = _canvas;
        c.Ensure(_clientW, _clientH);
        c.Scale = _s;
        c.Clear();
        float s = _s;
        c.FillRect(0, 0, _clientW, _clientH, Bg);
        GdipSetInterpolationMode(c.G, 7 /*HighQualityBicubic*/);
        GdipDrawImageRect(c.G, _bitmap, _ix, _iy, _dw, _dh);

        // dim what will be cut away
        float l = Dx(_cx), t = Dy(_cy), r = Dx(_cx + _cw), b = Dy(_cy + _ch);
        const uint shade = 0xA0000000;
        c.FillRect(_ix, _iy, _dw, t - _iy, shade);
        c.FillRect(_ix, b, _dw, _iy + _dh - b, shade);
        c.FillRect(_ix, t, l - _ix, b - t, shade);
        c.FillRect(r, t, _ix + _dw - r, b - t, shade);

        // frame, thirds and corner handles
        if (_drag != Drag.None)
            for (int i = 1; i <= 2; i++)
            {
                c.Line(l + (r - l) * i / 3, t, l + (r - l) * i / 3, b, 0x70FFFFFF, 1 * s);
                c.Line(l, t + (b - t) * i / 3, r, t + (b - t) * i / 3, 0x70FFFFFF, 1 * s);
            }
        c.RoundRect(l, t, r - l, b - t, 0, 0, 0xFFFFFFFF, 1.5f * s);
        float k = Math.Min(18 * s, Math.Min(r - l, b - t) / 2), th = 4 * s;
        foreach (var (px, py, sx, sy) in new[] { (l, t, 1, 1), (r, t, -1, 1), (l, b, 1, -1), (r, b, -1, -1) })
        {
            c.Line(px, py, px + sx * k, py, 0xFFFFFFFF, th);
            c.Line(px, py, px, py + sy * k, 0xFFFFFFFF, th);
        }

        // size badge above the frame
        var size = $"{(int)Math.Round(_cw)} × {(int)Math.Round(_ch)}";
        float sw = c.MeasureWidth(size, 12 * s, true) + 16 * s;
        float by = Math.Max(_iy + 6 * s, t - 30 * s);
        float bx = Math.Clamp((l + r) / 2 - sw / 2, 4 * s, _clientW - sw - 4 * s);
        c.RoundRect(bx, by, sw, 22 * s, 11 * s, 0xCC000000);
        c.Text(size, bx, by, sw, 22 * s, 12 * s, 0xFFFFFFFF, bold: true, rtl: false);

        // bottom bar
        c.FillRect(0, _clientH - _barH, _clientW, _barH, Bar);
        c.Line(0, _clientH - _barH, _clientW, _clientH - _barH, _dark ? 0x22FFFFFFu : 0x1A0B1220u, 1);
        Layout(c);
        var th2 = Theme.Current;
        for (int i = 0; i < _chips.Count; i++)
        {
            var chip = _chips[i];
            bool on = Math.Abs(chip.Ratio - _ratio) < 1e-6;
            c.RoundRect(chip.Box.X, chip.Box.Y, chip.Box.W, chip.Box.H, 17 * s, on ? th2.Accent : _hover == i ? ChipHover : ChipBg);
            c.Text(chip.Label, chip.Box.X, chip.Box.Y, chip.Box.W, chip.Box.H, 13 * s, on ? 0xFFFFFFFF : Text, bold: true);
        }
        Button(_resetBox, L.T("Reset", "إعادة"), _hover == 102, false);
        Button(_cancelBox, L.T("Cancel", "إلغاء"), _hover == 100, false);
        Button(_okBox, L.T("Crop", "قص"), _hover == 101, true);

        c.Flush();
        BitBlt(hdc, 0, 0, _clientW, _clientH, c.MemDc, 0, 0, 0x00CC0020 /*SRCCOPY*/);

        void Button(RectF box, string text, bool hot, bool primary)
        {
            uint fill = primary ? (hot ? 0xFF1D4ED8u : th2.Accent) : hot ? ChipHover : ChipBg;
            c.RoundRect(box.X, box.Y, box.W, box.H, 8 * s, fill);
            c.Text(text, box.X, box.Y, box.W, box.H, 13 * s, primary ? 0xFFFFFFFF : Text, bold: true);
        }
    }

    void Redraw() => InvalidateRect(Hwnd, IntPtr.Zero, 0);

    // ---------------- input ----------------

    static bool In(RectF r, float x, float y) => x >= r.X && x <= r.X + r.W && y >= r.Y && y <= r.Y + r.H;

    int HitBar(float x, float y)
    {
        for (int i = 0; i < _chips.Count; i++) if (In(_chips[i].Box, x, y)) return i;
        if (In(_cancelBox, x, y)) return 100;
        if (In(_okBox, x, y)) return 101;
        if (In(_resetBox, x, y)) return 102;
        return -1;
    }

    void Click(int hit)
    {
        switch (hit)
        {
            case 100: _done = true; break;
            case 101: _ok = true; _done = true; break;
            case 102: _ratio = 0; _cx = 0; _cy = 0; _cw = _fullW; _ch = _fullH; break;
            case >= 0 and < 100: _ratio = _chips[hit].Ratio; ApplyRatio(); break;
        }
        Redraw();
    }

    void Nudge(int dx, int dy)
    {
        _cx = Math.Clamp(_cx + dx, 0, _fullW - _cw);
        _cy = Math.Clamp(_cy + dy, 0, _fullH - _ch);
        Redraw();
    }

    protected override IntPtr Proc(uint msg, IntPtr w, IntPtr l)
    {
        switch (msg)
        {
            case WM_PAINT:
            {
                PAINTSTRUCT ps;
                var hdc = BeginPaint(Hwnd, &ps);
                Render(hdc);
                EndPaint(Hwnd, &ps);
                return 0;
            }
            case WM_ERASEBKGND:
                return 1;
            case W.WM_LBUTTONDOWN:
            {
                float x = W.LoWord(l), y = W.HiWord(l);
                int hit = HitBar(x, y);
                if (hit >= 0) { Click(hit); return 0; }
                if (y >= _clientH - _barH) return 0;
                double px = Px(x), py = Py(y);
                _edges = EdgesAt(x, y);
                if (_edges != 0)
                {
                    _drag = Drag.Resize;
                    _startX = _cx; _startY = _cy; _grabX = _cw; _grabY = _ch;
                }
                else if (InsideCrop(x, y))
                {
                    _drag = Drag.Move;
                    _grabX = px - _cx; _grabY = py - _cy;
                }
                else
                {
                    _drag = Drag.New;
                    _ax = Math.Clamp(px, 0, _fullW); _ay = Math.Clamp(py, 0, _fullH);
                }
                SetCapture(Hwnd);
                Redraw();
                return 0;
            }
            case W.WM_MOUSEMOVE:
            {
                float x = W.LoWord(l), y = W.HiWord(l);
                double px = Px(x), py = Py(y);
                switch (_drag)
                {
                    case Drag.Move:
                        _cx = Math.Clamp(px - _grabX, 0, _fullW - _cw);
                        _cy = Math.Clamp(py - _grabY, 0, _fullH - _ch);
                        Redraw();
                        break;
                    case Drag.New:
                        if (Math.Abs(px - _ax) * _z > 3 || Math.Abs(py - _ay) * _z > 3) { FromAnchor(_ax, _ay, px, py); Redraw(); }
                        break;
                    case Drag.Resize:
                        ResizeEdge(px, py);
                        Redraw();
                        break;
                    default:
                        int hover = HitBar(x, y);
                        if (hover != _hover) { _hover = hover; Redraw(); }
                        break;
                }
                return 0;
            }
            case W.WM_LBUTTONUP:
                if (_drag != Drag.None) { _drag = Drag.None; ReleaseCapture(); Redraw(); }
                return 0;
            case W.WM_SETCURSOR when W.LoWord(l) == 1 /*HTCLIENT*/:
            {
                var p = W.Cursor();
                W.ScreenToClient(Hwnd, ref p);
                int id = IDC_CROSS;
                if (HitBar(p.X, p.Y) >= 0) id = W.IDC_HAND;
                else if (p.Y >= _clientH - _barH) id = W.IDC_ARROW;
                else
                {
                    int e = _drag == Drag.Resize ? _edges : EdgesAt(p.X, p.Y);
                    id = e switch
                    {
                        3 or 12 => IDC_SIZENWSE,
                        6 or 9 => IDC_SIZENESW,
                        1 or 4 => IDC_SIZEWE,
                        2 or 8 => IDC_SIZENS,
                        _ => _drag == Drag.Move || InsideCrop(p.X, p.Y) ? IDC_SIZEALL : IDC_CROSS,
                    };
                }
                W.SetCursor(W.LoadCursorW(IntPtr.Zero, id));
                return 1;
            }
            case W.WM_KEYDOWN:
            {
                int step = W.KeyDown(W.VK_SHIFT) ? 10 : 1;
                switch ((int)w)
                {
                    case W.VK_RETURN: _ok = true; _done = true; return 0;
                    case W.VK_ESCAPE: _done = true; return 0;
                    case W.VK_LEFT: Nudge(-step, 0); return 0;
                    case W.VK_RIGHT: Nudge(step, 0); return 0;
                    case W.VK_UP: Nudge(0, -step); return 0;
                    case W.VK_DOWN: Nudge(0, step); return 0;
                }
                break;
            }
            case W.WM_CLOSE:
                _done = true;
                return 0;
        }
        return base.Proc(msg, w, l);
    }

    // ---------------- native ----------------

    const uint WM_PAINT = 0x000F, WM_ERASEBKGND = 0x0014;
    const int IDC_CROSS = 32515, IDC_SIZENWSE = 32642, IDC_SIZENESW = 32643, IDC_SIZEWE = 32644, IDC_SIZENS = 32645, IDC_SIZEALL = 32646;
    const int PixelFormat32bppARGB = 0x0026200A;

    [StructLayout(LayoutKind.Sequential)]
    struct PAINTSTRUCT { public IntPtr Hdc; public int Erase; public RECT Paint; public int Restore, IncUpdate; public fixed byte Reserved[32]; }

    [LibraryImport("user32.dll")] private static partial IntPtr BeginPaint(IntPtr hwnd, PAINTSTRUCT* ps);
    [LibraryImport("user32.dll")] private static partial int EndPaint(IntPtr hwnd, PAINTSTRUCT* ps);
    [LibraryImport("user32.dll")] private static partial int InvalidateRect(IntPtr hwnd, IntPtr rect, int erase);
    [LibraryImport("user32.dll")] private static partial IntPtr SetCapture(IntPtr hwnd);
    [LibraryImport("user32.dll")] private static partial int ReleaseCapture();
    [LibraryImport("user32.dll")] private static partial int AdjustWindowRectEx(ref RECT rect, uint style, int menu, uint exStyle);
    [LibraryImport("gdi32.dll")] private static partial int BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [LibraryImport("dwmapi.dll")] private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, void* value, int size);
    [LibraryImport("gdiplus.dll")] private static partial int GdipCreateBitmapFromScan0(int w, int h, int stride, int format, IntPtr scan0, out IntPtr bitmap);
    [LibraryImport("gdiplus.dll")] private static partial int GdipDrawImageRect(IntPtr g, IntPtr image, float x, float y, float w, float h);
    [LibraryImport("gdiplus.dll")] private static partial int GdipSetInterpolationMode(IntPtr g, int mode);
}

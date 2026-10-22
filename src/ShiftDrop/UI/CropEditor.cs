using ShiftDrop.Core;
using ShiftDrop.Native;

namespace ShiftDrop.UI;

/// <summary>
/// Pick the part of a picture or a video to keep: drag the frame, its corners or edges, or draw a new one;
/// choose a ratio; for a video, slide through time to see any frame. Result is in full-size pixels.
/// </summary>
public sealed class CropEditor : EditorWindow
{
    readonly string _name;
    readonly int _fullW, _fullH;
    readonly double _duration;                                   // 0 for a picture
    readonly Func<double, Task<(byte[] Bgra, int W, int H)>>? _frameAt;
    Picture? _picture;
    double _time, _shownTime = -1;
    bool _busy;

    // crop rectangle in picture pixels
    double _cx, _cy, _cw, _ch, _ratio;
    static readonly (string En, string Ar, double Ratio)[] Ratios =
        [("Free", "حر", 0), ("1:1", "1:1", 1), ("4:3", "4:3", 4.0 / 3), ("3:2", "3:2", 1.5), ("16:9", "16:9", 16.0 / 9), ("9:16", "9:16", 9.0 / 16)];

    enum Mode { None, Move, New, Resize }
    Mode _mode;
    int _edges;
    double _ax, _ay, _grabX, _grabY, _startX, _startY;
    const double MinSize = 8;

    // picture position on screen (device pixels)
    float _ix, _iy, _dw, _dh, _z;

    CropEditor(string name, Picture? first, int fullW, int fullH, double duration, Func<double, Task<(byte[], int, int)>>? frameAt)
    {
        _name = name; _picture = first; _fullW = fullW; _fullH = fullH; _duration = duration; _frameAt = frameAt;
        _cw = fullW; _ch = fullH;
        _shownTime = 0;
    }

    float Height => _duration > 0 ? 600 : 556;

    /// <summary>Opens the editor; returns the area to keep, or null when cancelled or nothing was cut away.</summary>
    public static (int X, int Y, int W, int H)? Ask(string name, byte[] bgra, int previewW, int previewH, int fullW, int fullH,
        double duration = 0, Func<double, Task<(byte[], int, int)>>? frameAt = null)
    {
        var e = new CropEditor(name, new Picture(bgra, previewW, previewH), fullW, fullH, duration, frameAt);
        bool ok = e.RunModal(duration > 0 ? L.T("Crop video", "قص إطار الفيديو") : L.T("Crop picture", "قص الصورة"), 760, e.Height);
        e._picture?.Dispose();
        if (!ok) return null;
        int x = (int)Math.Round(e._cx), y = (int)Math.Round(e._cy);
        int w = Math.Clamp((int)Math.Round(e._cw), 1, fullW - x), h = Math.Clamp((int)Math.Round(e._ch), 1, fullH - y);
        if (x == 0 && y == 0 && w == fullW && h == fullH) return null;
        return (x, y, w, h);
    }

    public static void RenderPreview(string path, (byte[] Bgra, int W, int H) frame, bool video, float scale, bool dark, bool arabic)
    {
        L.Arabic = arabic;
        var e = new CropEditor("Holiday at the sea." + (video ? "mp4" : "jpg"), new Picture(frame.Bgra, frame.W, frame.H), 1920, 1080, video ? 95.4 : 0, null)
        {
            _ratio = 1, _cx = 520, _cy = 60, _cw = 900, _ch = 900, _time = 31.2,
        };
        e.RenderToPng(path, 760, e.Height, scale, dark, arabic);
        e._picture?.Dispose();
    }

    // ---------------- geometry ----------------

    float Dx(double px) => _ix + (float)(px * _z);
    float Dy(double py) => _iy + (float)(py * _z);
    double Px(float x) => (x - _ix) / _z;
    double Py(float y) => (y - _iy) / _z;

    int EdgesAt(float x, float y)
    {
        float tol = 10 * S;
        float l = Dx(_cx), t = Dy(_cy), r = Dx(_cx + _cw), b = Dy(_cy + _ch);
        if (x < l - tol || x > r + tol || y < t - tol || y > b + tol) return 0;
        int e = 0;
        if (Math.Abs(x - l) <= tol) e |= 1; else if (Math.Abs(x - r) <= tol) e |= 4;
        if (Math.Abs(y - t) <= tol) e |= 2; else if (Math.Abs(y - b) <= tol) e |= 8;
        return e;
    }

    bool Inside(float x, float y) => x >= Dx(_cx) && x <= Dx(_cx + _cw) && y >= Dy(_cy) && y <= Dy(_cy + _ch);

    void ApplyRatio()
    {
        if (_ratio <= 0) return;
        double mx = _cx + _cw / 2, my = _cy + _ch / 2;
        double w = Math.Min(_fullW, _fullH * _ratio), h = w / _ratio;
        double keep = Math.Max(Math.Sqrt(_cw * _ch / (w * h)), 0.5);
        w *= Math.Min(1, keep); h = w / _ratio;
        _cw = w; _ch = h;
        _cx = Math.Clamp(mx - w / 2, 0, _fullW - w);
        _cy = Math.Clamp(my - h / 2, 0, _fullH - h);
    }

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
        _cx = Math.Clamp(px >= ax ? ax : ax - w, 0, _fullW - w);
        _cy = Math.Clamp(py >= ay ? ay : ay - h, 0, _fullH - h);
        _cw = w; _ch = h;
    }

    void ResizeEdge(double px, double py)
    {
        double l = _startX, t = _startY, r = _startX + _grabX, b = _startY + _grabY;
        bool corner = (_edges & 5) != 0 && (_edges & 10) != 0;
        if (corner) { FromAnchor((_edges & 1) != 0 ? r : l, (_edges & 2) != 0 ? b : t, px, py); return; }
        if (_ratio <= 0)
        {
            if ((_edges & 5) != 0)
            {
                double nl = (_edges & 1) != 0 ? Math.Clamp(px, 0, r - MinSize) : l;
                double nr = (_edges & 4) != 0 ? Math.Clamp(px, l + MinSize, _fullW) : r;
                _cx = nl; _cw = nr - nl;
            }
            else
            {
                double nt = (_edges & 2) != 0 ? Math.Clamp(py, 0, b - MinSize) : t;
                double nb = (_edges & 8) != 0 ? Math.Clamp(py, t + MinSize, _fullH) : b;
                _cy = nt; _ch = nb - nt;
            }
            return;
        }
        if ((_edges & 5) != 0)
        {
            double ax = (_edges & 1) != 0 ? r : l;
            double w = Math.Clamp((_edges & 1) != 0 ? ax - px : px - ax, MinSize, (_edges & 1) != 0 ? ax : _fullW - ax);
            double h = w / _ratio;
            if (h > _fullH) { h = _fullH; w = h * _ratio; }
            _cw = w; _ch = h;
            _cx = (_edges & 1) != 0 ? ax - w : ax;
            _cy = Math.Clamp((t + b) / 2 - h / 2, 0, _fullH - h);
        }
        else
        {
            double ay = (_edges & 2) != 0 ? b : t;
            double h = Math.Clamp((_edges & 2) != 0 ? ay - py : py - ay, MinSize, (_edges & 2) != 0 ? ay : _fullH - ay);
            double w = h * _ratio;
            if (w > _fullW) { w = _fullW; h = w / _ratio; }
            _cw = w; _ch = h;
            _cy = (_edges & 2) != 0 ? ay - h : ay;
            _cx = Math.Clamp((l + r) / 2 - w / 2, 0, _fullW - w);
        }
    }

    // ---------------- input ----------------

    protected override bool Press(float x, float y)
    {
        if (_z <= 0) return false;
        _edges = EdgesAt(x, y);
        if (_edges != 0) { _mode = Mode.Resize; _startX = _cx; _startY = _cy; _grabX = _cw; _grabY = _ch; return true; }
        if (Inside(x, y)) { _mode = Mode.Move; _grabX = Px(x) - _cx; _grabY = Py(y) - _cy; return true; }
        if (x >= _ix && x <= _ix + _dw && y >= _iy && y <= _iy + _dh)
        {
            _mode = Mode.New; _ax = Math.Clamp(Px(x), 0, _fullW); _ay = Math.Clamp(Py(y), 0, _fullH);
            return true;
        }
        return false;
    }

    protected override void Move(float x, float y)
    {
        double px = Px(x), py = Py(y);
        switch (_mode)
        {
            case Mode.Move:
                _cx = Math.Clamp(px - _grabX, 0, _fullW - _cw);
                _cy = Math.Clamp(py - _grabY, 0, _fullH - _ch);
                break;
            case Mode.New when Math.Abs(px - _ax) * _z > 3 || Math.Abs(py - _ay) * _z > 3:
                FromAnchor(_ax, _ay, px, py);
                break;
            case Mode.Resize:
                ResizeEdge(px, py);
                break;
        }
    }

    protected override void Release() => _mode = Mode.None;

    protected override int CursorAt(float x, float y)
    {
        int e = _mode == Mode.Resize ? _edges : EdgesAt(x, y);
        return e switch
        {
            3 or 12 => IDC_SIZENWSE,
            6 or 9 => IDC_SIZENESW,
            1 or 4 => IDC_SIZEWE,
            2 or 8 => IDC_SIZENS,
            _ => _mode == Mode.Move || Inside(x, y) ? IDC_SIZEALL
                : x >= _ix && x <= _ix + _dw && y >= _iy && y <= _iy + _dh ? IDC_CROSS : W.IDC_ARROW,
        };
    }

    protected override bool Key(int vk)
    {
        int step = W.KeyDown(W.VK_SHIFT) ? 10 : 1;
        (int dx, int dy) = vk switch { W.VK_LEFT => (-step, 0), W.VK_RIGHT => (step, 0), W.VK_UP => (0, -step), W.VK_DOWN => (0, step), _ => (0, 0) };
        if (dx == 0 && dy == 0) return false;
        _cx = Math.Clamp(_cx + dx, 0, _fullW - _cw);
        _cy = Math.Clamp(_cy + dy, 0, _fullH - _ch);
        return true;
    }

    // ---------------- video frames ----------------

    void ShowTime(double t)
    {
        _time = Math.Clamp(t, 0, _duration);
        if (_busy || _frameAt == null) return;
        _ = LoadFrames();
    }

    async Task LoadFrames()
    {
        _busy = true;
        try
        {
            while (Hwnd != IntPtr.Zero && Math.Abs(_time - _shownTime) > 0.04)
            {
                double t = _time;
                var (bgra, w, h) = await _frameAt!(t);
                if (Hwnd == IntPtr.Zero) return;
                _picture?.Dispose();
                _picture = new Picture(bgra, w, h);
                _shownTime = t;
                Invalidate();
            }
        }
        catch (Exception e) { Log.Write("crop frame: " + e.Message); }
        finally { _busy = false; }
    }

    // ---------------- painting ----------------

    protected override void Paint(Canvas c)
    {
        float s = S, pad = 24 * s;
        Header(c, _duration > 0 ? L.T("Crop video", "قص إطار الفيديو") : L.T("Crop picture", "قص الصورة"), _name);

        // the picture, fitted
        float areaY = 80 * s, areaH = 340 * s, areaW = ClientW - 2 * pad;
        _z = (float)Math.Min(areaW / _fullW, areaH / _fullH);
        _dw = _fullW * _z; _dh = _fullH * _z;
        _ix = (ClientW - _dw) / 2; _iy = areaY + (areaH - _dh) / 2;
        c.RoundRect(pad, areaY - 4 * s, areaW, areaH + 8 * s, 12 * s, Dark ? 0xFF0B0F16u : 0xFFE5E9F0u);
        if (_picture is { Bitmap: not 0 } p) c.Image(p.Bitmap, _ix, _iy, _dw, _dh, 0);

        // outside the crop is dimmed; the crop has a white frame, thirds while dragging, and corner handles
        float l = Dx(_cx), t = Dy(_cy), r = Dx(_cx + _cw), b = Dy(_cy + _ch);
        const uint shade = 0xA0000000;
        c.FillRect(_ix, _iy, _dw, t - _iy, shade);
        c.FillRect(_ix, b, _dw, _iy + _dh - b, shade);
        c.FillRect(_ix, t, l - _ix, b - t, shade);
        c.FillRect(r, t, _ix + _dw - r, b - t, shade);
        if (_mode != Mode.None)
            for (int i = 1; i <= 2; i++)
            {
                c.Line(l + (r - l) * i / 3, t, l + (r - l) * i / 3, b, 0x70FFFFFF, 1 * s);
                c.Line(l, t + (b - t) * i / 3, r, t + (b - t) * i / 3, 0x70FFFFFF, 1 * s);
            }
        c.RoundRect(l, t, r - l, b - t, 0, 0, 0xFFFFFFFF, 1.5f * s);
        float k = Math.Min(16 * s, Math.Min(r - l, b - t) / 2);
        foreach (var (px, py, sx, sy) in new[] { (l, t, 1f, 1f), (r, t, -1f, 1f), (l, b, 1f, -1f), (r, b, -1f, -1f) })
        {
            c.Line(px, py, px + sx * k, py, Accent, 4 * s);
            c.Line(px, py, px, py + sy * k, Accent, 4 * s);
        }
        float y = areaY + areaH + 20 * s;

        // time (video)
        if (_duration > 0)
        {
            float lw = 120 * s;
            var label = $"{Time(_time)} / {Time(_duration)}";
            c.Text(label, Rtl ? pad : ClientW - pad - lw, y, lw, 28 * s, 12 * s, Sub, align: Rtl ? Align.Near : Align.Far, rtl: false);
            Slider(c, new RectF(Rtl ? pad + lw + 12 * s : pad + 8 * s, y, ClientW - 2 * pad - lw - 20 * s, 28 * s), "time",
                (float)(_time / _duration), v => ShowTime(v * _duration));
            y += 44 * s;
        }

        // ratios and size
        int sel = Array.FindIndex(Ratios, x => Math.Abs(x.Ratio - _ratio) < 1e-6);
        Segments(c, Mirror(new RectF(pad, y, 470 * s, 36 * s)), "ratio", Ratios.Select(x => L.T(x.En, x.Ar)).ToArray(), sel,
            i => { _ratio = Ratios[i].Ratio; ApplyRatio(); });
        var size = $"{(int)Math.Round(_cw)} × {(int)Math.Round(_ch)} px";
        c.Text(size, Rtl ? pad : ClientW - pad - 200 * s, y, 200 * s, 36 * s, 13 * s, Ink, bold: true, align: Rtl ? Align.Near : Align.Far, rtl: false);

        // footer
        float fy = ClientH - 62 * s;
        c.Line(0, fy, ClientW, fy, PanelLine, 1);
        Button(c, Mirror(new RectF(pad, fy + 14 * s, 96 * s, 36 * s)), "reset", L.T("Reset", "إعادة"), false,
            () => { _ratio = 0; _cx = 0; _cy = 0; _cw = _fullW; _ch = _fullH; });
        Button(c, Mirror(new RectF(ClientW - pad - 220 * s, fy + 14 * s, 104 * s, 36 * s)), "cancel", L.T("Cancel", "إلغاء"), false, () => Close(false));
        Button(c, Mirror(new RectF(ClientW - pad - 108 * s, fy + 14 * s, 108 * s, 36 * s)), "ok", L.T("Crop", "قص"), true, () => Close(true));
    }
}

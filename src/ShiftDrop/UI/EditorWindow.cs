using System.Runtime.InteropServices;
using ShiftDrop.Core;
using ShiftDrop.Native;

namespace ShiftDrop.UI;

/// <summary>
/// The base of ShiftDrop's small editors (trim, crop, channels): a modal window drawn with GDI+ in the
/// app's blue style, light or dark, with a few simple widgets (buttons, chips, checkbox, sliders)
/// and a generic "drag" helper. Subclasses paint and register what can be clicked or dragged.
/// </summary>
public abstract unsafe partial class EditorWindow : Window
{
    protected float S = 1;
    protected bool Rtl, Dark;
    protected int ClientW, ClientH;
    protected bool Accepted;
    readonly Canvas _canvas = new();
    bool _done;

    // colours
    protected uint Bg => Dark ? 0xFF161B24u : 0xFFF4F6FAu;
    protected uint Panel => Dark ? 0xFF1E2532u : 0xFFFFFFFFu;
    protected uint PanelLine => Dark ? 0x22FFFFFFu : 0x140B1220u;
    protected uint Ink => Dark ? 0xFFF1F5F9u : 0xFF0B1220u;
    protected uint Sub => Dark ? 0xFF94A3B8u : 0xFF64748Bu;
    protected uint Accent => Dark ? 0xFF3B82F6u : 0xFF2563EBu;
    protected uint AccentHot => Dark ? 0xFF60A5FAu : 0xFF1D4ED8u;
    protected uint Soft => Dark ? 0xFF2A3344u : 0xFFEEF2F7u;
    protected uint SoftHot => Dark ? 0xFF343E52u : 0xFFE2E8F0u;
    protected uint Track => Dark ? 0xFF334155u : 0xFFDDE3EBu;

    // ---------------- window ----------------

    /// <summary>Shows the editor centred on the pointer's screen and waits until it closes.</summary>
    protected bool RunModal(string title, float widthDip, float heightDip)
    {
        var at = W.Cursor();
        S = W.ScaleAt(at);
        Rtl = L.Arabic;
        Dark = Theme.Current == Theme.Dark;
        ClientW = (int)Math.Ceiling(widthDip * S);
        ClientH = (int)Math.Ceiling(heightDip * S);
        const uint style = W.WS_POPUP | W.WS_CAPTION | W.WS_SYSMENU;
        var r = new RECT { Right = ClientW, Bottom = ClientH };
        AdjustWindowRectEx(ref r, style, 0, W.WS_EX_DLGMODALFRAME);
        var work = W.MonitorAt(at).Work;
        int x = work.Left + (work.Width - r.Width) / 2, y = work.Top + (work.Height - r.Height) / 2;
        Create("ShiftDrop.Editor", W.WS_EX_DLGMODALFRAME | W.WS_EX_TOPMOST, style, $"ShiftDrop · {title}", x, y, r.Width, r.Height);
        int on = Dark ? 1 : 0;
        DwmSetWindowAttribute(Hwnd, 20 /*DWMWA_USE_IMMERSIVE_DARK_MODE*/, &on, 4);
        var icoPath = Path.Combine(AppContext.BaseDirectory, "shiftdrop.ico");
        var icon = W.LoadImageW(IntPtr.Zero, icoPath, W.IMAGE_ICON, (int)(16 * S), (int)(16 * S), W.LR_LOADFROMFILE);
        if (icon != IntPtr.Zero) W.SendMessageW(Hwnd, 0x0080 /*WM_SETICON*/, 0, icon);
        Opened();
        W.ShowWindow(Hwnd, W.SW_SHOW);
        W.SetForegroundWindow(Hwnd);
        while (!_done && W.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            W.TranslateMessage(ref msg);
            W.DispatchMessageW(ref msg);
        }
        Closing();
        Destroy();
        if (icon != IntPtr.Zero) W.DestroyIcon(icon);
        _canvas.Dispose();
        return Accepted;
    }

    /// <summary>Draws one frame to a PNG without showing a window (ShiftDrop.exe --preview).</summary>
    protected void RenderToPng(string path, float widthDip, float heightDip, float scale, bool dark, bool arabic)
    {
        S = scale; Dark = dark; Rtl = arabic;
        ClientW = (int)Math.Ceiling(widthDip * S);
        ClientH = (int)Math.Ceiling(heightDip * S);
        _canvas.Scale = S;
        _canvas.Ensure(ClientW, ClientH);
        _canvas.Clear();
        _canvas.FillRect(0, 0, ClientW, ClientH, Bg);
        _hits.Clear();
        Paint(_canvas);
        _canvas.SavePng(path);
    }

    protected virtual void Opened() { }
    protected virtual void Closing() { }
    protected abstract void Paint(Canvas c);

    protected void Close(bool accept)
    {
        Accepted = accept;
        _done = true;
    }

    protected void Invalidate() { if (Hwnd != IntPtr.Zero) InvalidateRect(Hwnd, IntPtr.Zero, 0); }

    // ---------------- hit areas ----------------

    sealed record Hit(RectF Box, string Id, Action? Click, Action<float, float>? Drag, int Cursor);
    readonly List<Hit> _hits = [];
    string? _hover, _pressed;
    Hit? _dragging;

    /// <summary>Something clickable (a button, a chip). Registered while painting.</summary>
    protected void Clickable(RectF box, string id, Action click) => _hits.Add(new Hit(box, id, click, null, W.IDC_HAND));

    /// <summary>Something draggable: <paramref name="drag"/> gets the pointer on press and on every move.</summary>
    protected void Draggable(RectF box, string id, Action<float, float> drag, int cursor = IDC_SIZEWE) => _hits.Add(new Hit(box, id, null, drag, cursor));

    protected bool IsHot(string id) => _hover == id || _pressed == id;
    protected bool IsDragging(string id) => _dragging?.Id == id;

    /// <summary>Called when the press isn't on a registered area (free-form dragging, like drawing a crop box).</summary>
    protected virtual bool Press(float x, float y) => false;
    protected virtual void Move(float x, float y) { }
    protected virtual void Release() { }
    protected virtual int CursorAt(float x, float y) => W.IDC_ARROW;
    protected virtual bool Key(int vk) => false;
    protected virtual void Tick() { }

    bool _freeDrag;

    Hit? HitAt(float x, float y)
    {
        for (int i = _hits.Count - 1; i >= 0; i--)
        {
            var b = _hits[i].Box;
            if (x >= b.X && x <= b.X + b.W && y >= b.Y && y <= b.Y + b.H) return _hits[i];
        }
        return null;
    }

    const nuint TimerTick = 7;
    protected void StartTicking(uint ms = 30) => W.SetTimer(Hwnd, TimerTick, ms, IntPtr.Zero);
    protected void StopTicking() => W.KillTimer(Hwnd, TimerTick);

    protected override IntPtr Proc(uint msg, IntPtr w, IntPtr l)
    {
        switch (msg)
        {
            case WM_PAINT:
            {
                PAINTSTRUCT ps;
                var hdc = BeginPaint(Hwnd, &ps);
                _canvas.Scale = S;
                _canvas.Ensure(ClientW, ClientH);
                _canvas.Clear();
                _canvas.FillRect(0, 0, ClientW, ClientH, Bg);
                _hits.Clear();
                Paint(_canvas);
                _canvas.Flush();
                BitBlt(hdc, 0, 0, ClientW, ClientH, _canvas.MemDc, 0, 0, 0x00CC0020);
                EndPaint(Hwnd, &ps);
                return 0;
            }
            case WM_ERASEBKGND:
                return 1;
            case W.WM_TIMER when (nuint)w == TimerTick:
                Tick();
                return 0;
            case W.WM_MOUSEMOVE:
            {
                float x = W.LoWord(l), y = W.HiWord(l);
                if (_dragging != null) { _dragging.Drag!(x, y); Invalidate(); return 0; }
                if (_freeDrag) { Move(x, y); Invalidate(); return 0; }
                var id = HitAt(x, y)?.Id;
                if (id != _hover) { _hover = id; Invalidate(); }
                return 0;
            }
            case W.WM_LBUTTONDOWN:
            {
                float x = W.LoWord(l), y = W.HiWord(l);
                var hit = HitAt(x, y);
                SetCapture(Hwnd);
                if (hit?.Drag != null) { _dragging = hit; hit.Drag(x, y); }
                else if (hit != null) _pressed = hit.Id;
                else _freeDrag = Press(x, y);
                Invalidate();
                return 0;
            }
            case W.WM_LBUTTONUP:
            {
                float x = W.LoWord(l), y = W.HiWord(l);
                ReleaseCapture();
                if (_dragging != null) { _dragging = null; Release(); }
                else if (_freeDrag) { _freeDrag = false; Release(); }
                else if (_pressed != null)
                {
                    var hit = HitAt(x, y);
                    var id = _pressed;
                    _pressed = null;
                    if (hit != null && hit.Id == id) hit.Click?.Invoke();
                }
                Invalidate();
                return 0;
            }
            case W.WM_SETCURSOR when W.LoWord(l) == 1 /*HTCLIENT*/:
            {
                var p = W.Cursor();
                W.ScreenToClient(Hwnd, ref p);
                var hit = _dragging ?? HitAt(p.X, p.Y);
                W.SetCursor(W.LoadCursorW(IntPtr.Zero, hit?.Cursor ?? CursorAt(p.X, p.Y)));
                return 1;
            }
            case W.WM_KEYDOWN:
                if (Key((int)w)) { Invalidate(); return 0; }
                if ((int)w == W.VK_ESCAPE) { Close(false); return 0; }
                if ((int)w == W.VK_RETURN) { Close(true); return 0; }
                break;
            case W.WM_CLOSE:
                Close(false);
                return 0;
        }
        return base.Proc(msg, w, l);
    }

    // ---------------- widgets ----------------

    protected RectF Mirror(RectF r) => Rtl ? new RectF(ClientW - r.X - r.W, r.Y, r.W, r.H) : r;

    protected void Header(Canvas c, string title, string subtitle)
    {
        float s = S;
        c.Text(title, 0, 14 * s, ClientW, 26 * s, 17 * s, Ink, bold: true);
        c.Text(subtitle, 24 * s, 40 * s, ClientW - 48 * s, 18 * s, 11.5f * s, Sub, rtl: Core.Engines.Documents.IsRtl(subtitle));
        c.Line(0, 66 * s, ClientW, 66 * s, PanelLine, 1);
    }

    protected void Button(Canvas c, RectF box, string id, string text, bool primary, Action click, string? icon = null)
    {
        float s = S;
        bool hot = IsHot(id);
        uint fill = primary ? (hot ? AccentHot : Accent) : hot ? SoftHot : Soft;
        uint fg = primary ? 0xFFFFFFFF : Ink;
        c.RoundRect(box.X, box.Y, box.W, box.H, 9 * s, fill);
        if (icon == null) c.Text(text, box.X, box.Y, box.W, box.H, 13 * s, fg, bold: true);
        else
        {
            float tw = c.MeasureWidth(text, 13 * s, true), iw = 16 * s, gap = 7 * s, total = iw + gap + tw;
            float start = box.X + (box.W - total) / 2;
            float ix = Rtl ? start + tw + gap + iw / 2 : start + iw / 2, tx = Rtl ? start : start + iw + gap;
            DrawGlyph(c, icon, ix, box.Y + box.H / 2, iw, fg);
            c.Text(text, tx, box.Y, tw + 2 * s, box.H, 13 * s, fg, bold: true, align: Align.Near, rtl: false);
        }
        Clickable(box, id, click);
    }

    /// <summary>Small built-in glyphs: play, stop, reset.</summary>
    protected static void DrawGlyph(Canvas c, string name, float cx, float cy, float size, uint color)
    {
        float k = size / 16f;
        switch (name)
        {
            case "play":
                for (int i = 0; i < 9; i++) c.Line(cx - 4 * k + i * k, cy - (5.5f - i * 0.61f) * k, cx - 4 * k + i * k, cy + (5.5f - i * 0.61f) * k, color, 1.4f * k);
                break;
            case "stop":
                c.RoundRect(cx - 5 * k, cy - 5 * k, 10 * k, 10 * k, 2 * k, color);
                break;
            default:
                Icons.Draw(c, name, cx, cy, size, color);
                break;
        }
    }

    /// <summary>A row of options; the chosen one is filled blue.</summary>
    protected void Segments(Canvas c, RectF box, string id, string[] options, int selected, Action<int> choose)
    {
        float s = S;
        c.RoundRect(box.X, box.Y, box.W, box.H, box.H / 2, Soft);
        float w = box.W / options.Length;
        for (int i = 0; i < options.Length; i++)
        {
            int visual = Rtl ? options.Length - 1 - i : i;
            var seg = new RectF(box.X + visual * w + 3 * s, box.Y + 3 * s, w - 6 * s, box.H - 6 * s);
            bool on = i == selected, hot = IsHot($"{id}{i}");
            if (on) c.RoundRect(seg.X, seg.Y, seg.W, seg.H, seg.H / 2, Accent);
            else if (hot) c.RoundRect(seg.X, seg.Y, seg.W, seg.H, seg.H / 2, SoftHot);
            c.Text(options[i], seg.X, seg.Y, seg.W, seg.H, 12.5f * s, on ? 0xFFFFFFFF : Ink, bold: on);
            int index = i;
            Clickable(seg, $"{id}{i}", () => choose(index));
        }
    }

    protected void Checkbox(Canvas c, RectF box, string id, string text, bool value, Action<bool> toggle)
    {
        float s = S, size = 18 * s;
        float bx = Rtl ? box.X + box.W - size : box.X, by = box.Y + (box.H - size) / 2;
        c.RoundRect(bx, by, size, size, 5 * s, value ? Accent : Panel, value ? Accent : (IsHot(id) ? Accent : Track), 1.5f * s);
        if (value) c.Polyline(0xFFFFFFFF, 2 * s, bx + 4.5f * s, by + 9.5f * s, bx + 7.8f * s, by + 12.8f * s, bx + 13.5f * s, by + 5.5f * s);
        float tx = Rtl ? box.X : bx + size + 9 * s;
        c.Text(text, tx, box.Y, box.W - size - 9 * s, box.H, 12.5f * s, Ink, align: Rtl ? Align.Far : Align.Near);
        Clickable(box, id, () => toggle(!value));
    }

    /// <summary>A horizontal slider (0..1) with a round knob.</summary>
    protected void Slider(Canvas c, RectF box, string id, float value, Action<float> change)
    {
        float s = S, cy = box.Y + box.H / 2;
        value = Math.Clamp(value, 0, 1);
        c.RoundRect(box.X, cy - 2.5f * s, box.W, 5 * s, 2.5f * s, Track);
        float vx = Rtl ? box.X + box.W * (1 - value) : box.X + box.W * value;
        if (Rtl) c.RoundRect(vx, cy - 2.5f * s, box.X + box.W - vx, 5 * s, 2.5f * s, Accent);
        else c.RoundRect(box.X, cy - 2.5f * s, vx - box.X, 5 * s, 2.5f * s, Accent);
        float kr = (IsHot(id) || IsDragging(id) ? 10 : 9) * s;
        c.FillCircle(vx, cy + 1 * s, kr, 0x22000000);
        c.FillCircle(vx, cy, kr, 0xFFFFFFFF);
        c.StrokeCircle(vx, cy, kr, Dark ? 0x00000000u : 0x220B1220u, 1);
        Draggable(new RectF(box.X - 10 * s, box.Y, box.W + 20 * s, box.H), id, (x, _) =>
        {
            float v = Math.Clamp((x - box.X) / box.W, 0, 1);
            change(Rtl ? 1 - v : v);
        }, W.IDC_HAND);
    }

    // ---------------- helpers ----------------

    /// <summary>"1:05.42" or "1:02:05.4".</summary>
    protected static string Time(double sec)
    {
        sec = Math.Max(0, sec);
        var t = TimeSpan.FromSeconds(sec);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 100}"
            : $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds / 10:00}";
    }

    /// <summary>A GDI+ bitmap over BGRA pixels; the memory is owned by the returned handle.</summary>
    protected sealed class Picture : IDisposable
    {
        public IntPtr Bitmap { get; private set; }
        public int Width { get; }
        public int Height { get; }
        IntPtr _mem;

        public Picture(byte[] bgra, int w, int h)
        {
            Width = w; Height = h;
            _mem = Marshal.AllocHGlobal(bgra.Length);
            Marshal.Copy(bgra, 0, _mem, bgra.Length);
            Gdip.GdipCreateBitmapFromScan0(w, h, w * 4, 0x0026200A /*32bppARGB*/, _mem, out var bmp);
            Bitmap = bmp;
        }

        public void Dispose()
        {
            if (Bitmap != IntPtr.Zero) Gdip.GdipDisposeImage(Bitmap);
            if (_mem != IntPtr.Zero) Marshal.FreeHGlobal(_mem);
            Bitmap = _mem = IntPtr.Zero;
        }
    }

    // ---------------- native ----------------

    const uint WM_PAINT = 0x000F, WM_ERASEBKGND = 0x0014;
    protected const int IDC_CROSS = 32515, IDC_SIZENWSE = 32642, IDC_SIZENESW = 32643, IDC_SIZEWE = 32644, IDC_SIZENS = 32645, IDC_SIZEALL = 32646;

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
}

/// <summary>Plays a WAV held in memory through Windows (no player needed).</summary>
public static partial class SoundPreview
{
    static GCHandle _pinned;
    static long _startedAt;
    public static double Length { get; private set; }
    public static bool Playing => _pinned.IsAllocated && Environment.TickCount64 - _startedAt < Length * 1000 + 150;
    /// <summary>Seconds since playback began.</summary>
    public static double Position => (Environment.TickCount64 - _startedAt) / 1000.0;

    public static void Play(byte[] wav, double seconds)
    {
        Stop();
        _pinned = GCHandle.Alloc(wav, GCHandleType.Pinned);
        Length = seconds;
        _startedAt = Environment.TickCount64;
        PlaySoundW(_pinned.AddrOfPinnedObject(), IntPtr.Zero, 0x0004 /*SND_MEMORY*/ | 0x0001 /*SND_ASYNC*/ | 0x0002 /*SND_NODEFAULT*/);
    }

    public static void Stop()
    {
        PlaySoundW(IntPtr.Zero, IntPtr.Zero, 0);
        if (_pinned.IsAllocated) _pinned.Free();
        Length = 0;
    }

    [LibraryImport("winmm.dll")] private static partial int PlaySoundW(IntPtr sound, IntPtr module, uint flags);
}

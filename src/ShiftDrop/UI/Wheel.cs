using ShiftDrop.Core;
using ShiftDrop.Native;

namespace ShiftDrop.UI;

/// <summary>
/// The radial menu. It is created once at start-up (so it appears instantly), registered as an
/// OLE drop target, and shown under the pointer while a file is being dragged.
/// </summary>
public sealed class Wheel : LayeredWindow, IDropHandler
{
    // design sizes in device-independent pixels
    const float PlateR = 170, OuterR = 156, InnerR = 74, HubR = 66, Margin = 22, Corner = 9, Gap = 6, StripH = 46;
    const int OpenMs = 260, StaggerMs = 16, PetalMs = 170;
    const nuint TimerAnim = 1;

    public required Func<IReadOnlyList<string>, OpGroup, List<Operation>> ItemsFor { get; init; }
    public required Action<Operation, IReadOnlyList<string>> Execute { get; init; }
    /// <summary>Raised when the drag carries no files, or after a drop: don't reopen until the button is released.</summary>
    public Action? Done { get; init; }
    /// <summary>Size multiplier from the settings.</summary>
    public Func<float> SizeFactor { get; init; } = () => 1f;
    /// <summary>Key names for the mode strip (open, tools, actions), or null to hide it.</summary>
    public Func<(string Open, string Tools, string Actions)?> Hints { get; init; } = () => ("Shift", "Ctrl", "Alt");

    public OpGroup Mode { get; private set; } = OpGroup.Convert;
    public bool ClickMode { get; private set; }
    /// <summary>True once Explorer has delivered the dragged files to us.</summary>
    public bool GotFiles { get; private set; }

    IReadOnlyList<string> _files = [];
    List<Operation> _items = [];
    float[] _hot = [];
    int _hover = -1;
    long _openStart, _petalStart, _lastFrame;
    float _scale = 1;
    bool _strip;
    Theme _theme = Theme.Light;
    POINT _center;

    public Wheel()
    {
        Create("ShiftDrop.Wheel", W.WS_EX_LAYERED | W.WS_EX_TOPMOST | W.WS_EX_TOOLWINDOW | W.WS_EX_NOACTIVATE, W.WS_POPUP);
        DropTarget.Register(Hwnd, new DropTarget(Hwnd, this));
    }

    int WidthPx => (int)Math.Ceiling((PlateR + Margin) * 2 * _scale);
    int HeightPx => (int)Math.Ceiling(((PlateR + Margin) * 2 + (_strip ? StripH : 0)) * _scale);
    float CenterYPx => (PlateR + Margin) * _scale;

    /// <summary>Shows the wheel under the pointer during a drag (no focus taken).</summary>
    public void ShowForDrag(POINT at, OpGroup mode)
    {
        ClickMode = false;
        GotFiles = false;
        SetItems([], [], mode);
        SetExStyle(activate: false);
        Open(at, activate: false);
    }

    /// <summary>Shows the wheel for files picked without dragging (Send to, Enter, a second launch). Click or use the keyboard.</summary>
    public void ShowForFiles(POINT at, IReadOnlyList<string> files, OpGroup mode = OpGroup.Convert)
    {
        ClickMode = true;
        GotFiles = true;
        SetItems(files, ItemsFor(files, mode), mode);
        _hover = _items.Count > 0 ? 0 : -1;
        SetExStyle(activate: true);
        Open(at, activate: true);
        W.SetForegroundWindow(Hwnd);
        W.SetFocus(Hwnd);
    }

    void SetItems(IReadOnlyList<string> files, List<Operation> items, OpGroup mode)
    {
        _files = files;
        _items = items;
        _hot = new float[items.Count];
        _hover = -1;
        Mode = mode;
    }

    void SetExStyle(bool activate)
    {
        nint ex = (nint)(W.WS_EX_LAYERED | W.WS_EX_TOPMOST | W.WS_EX_TOOLWINDOW | (activate ? 0 : W.WS_EX_NOACTIVATE));
        W.SetWindowLongPtrW(Hwnd, W.GWL_EXSTYLE, ex);
    }

    void Open(POINT at, bool activate)
    {
        _theme = Theme.Current;
        _scale = W.ScaleAt(at) * SizeFactor();
        _strip = Hints() != null;
        Canvas.Scale = _scale;
        Canvas.Ensure(WidthPx, HeightPx);
        // centre the plate on the pointer, but keep the whole wheel on the screen
        var work = W.MonitorAt(at).Work;
        int x = Math.Clamp(at.X - WidthPx / 2, work.Left, Math.Max(work.Left, work.Right - WidthPx));
        int y = Math.Clamp(at.Y - (int)CenterYPx, work.Top, Math.Max(work.Top, work.Bottom - HeightPx));
        _center = new POINT(x + WidthPx / 2, y + (int)CenterYPx);
        _openStart = _petalStart = _lastFrame = Environment.TickCount64;
        Render();
        Present(x, y, 0);
        ShowTopmost(activate);
        Animate();
    }

    public void Hide()
    {
        if (!Visible) return;
        W.KillTimer(Hwnd, TimerAnim);
        HideWindow();
        ClickMode = false;
        GotFiles = false;
        SetItems([], [], Mode);
    }

    public void SetMode(OpGroup mode)
    {
        if (mode == Mode || !Visible) return;
        SetItems(_files, GotFiles ? ItemsFor(_files, mode) : [], mode);
        if (ClickMode && _items.Count > 0) _hover = 0;
        _petalStart = Environment.TickCount64;
        Animate();
    }

    void Animate()
    {
        if (!Visible) return;
        W.SetTimer(Hwnd, TimerAnim, 15, IntPtr.Zero);
        Redraw();
    }

    /// <summary>Advances the animations one frame; returns true while something is still moving.</summary>
    bool Step()
    {
        long now = Environment.TickCount64;
        float dt = Math.Clamp(now - _lastFrame, 1, 50);
        _lastFrame = now;
        float k = 1 - MathF.Exp(-dt / 55f);
        bool moving = now - _openStart < OpenMs || now - _petalStart < PetalMs + StaggerMs * Math.Max(8, _items.Count) || !GotFiles;
        for (int i = 0; i < _hot.Length; i++)
        {
            float target = i == _hover ? 1 : 0;
            _hot[i] += (target - _hot[i]) * k;
            if (Math.Abs(target - _hot[i]) > 0.01f) moving = true; else _hot[i] = target;
        }
        return moving;
    }

    void Redraw()
    {
        if (!Visible) return;
        Render();
        float t = Math.Clamp((Environment.TickCount64 - _openStart) / (float)OpenMs, 0, 1);
        Present(X, Y, (byte)(255 * Math.Min(1, Ease(t) * 1.4f)));
    }

    static float Ease(float t) => 1 - MathF.Pow(1 - Math.Clamp(t, 0, 1), 3);
    static float Back(float t) { t = Math.Clamp(t, 0, 1); const float c = 1.4f; return 1 + (c + 1) * MathF.Pow(t - 1, 3) + c * MathF.Pow(t - 1, 2); }

    static uint Mix(uint a, uint b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        uint Ch(int sh) => (uint)Math.Round(((a >> sh) & 0xFF) * (1 - t) + ((b >> sh) & 0xFF) * t);
        return (Ch(24) << 24) | (Ch(16) << 16) | (Ch(8) << 8) | Ch(0);
    }

    static uint Fade(uint c, float alpha) => ((uint)Math.Round(((c >> 24) & 0xFF) * Math.Clamp(alpha, 0, 1)) << 24) | (c & 0xFFFFFF);

    // ---------------- drawing ----------------

    bool Dark => _theme == Theme.Dark;

    void Render()
    {
        var c = Canvas;
        var th = _theme;
        float s = _scale, cx = c.Width / 2f, cy = CenterYPx;
        long now = Environment.TickCount64;
        float open = Back((now - _openStart) / (float)OpenMs);
        c.Clear();
        c.Transform(cx, cy, 0.88f + 0.12f * open);

        // soft shadow, glassy plate with a thin rim
        for (int i = 4; i >= 1; i--) c.FillCircle(cx, cy + 5 * s, (PlateR + i * 4) * s, Dark ? 0x16000000u : 0x0C0F172Au);
        c.GradientCircle(cx, cy, PlateR * s, Dark ? 0xF5222A38u : 0xF7F8FAFDu, Dark ? 0xF5161B25u : 0xF5E8EDF4u);
        c.StrokeCircle(cx, cy, PlateR * s - 0.5f, Dark ? 0x26FFFFFFu : 0x140B1220u, 1);

        int n = _items.Count;
        bool waiting = !GotFiles;
        int slots = waiting ? 8 : n;
        float step = slots > 0 ? 360f / slots : 360;
        float rm = (OuterR + InnerR) / 2 * s;
        float gapDeg = Gap * s / rm * 180 / MathF.PI;
        uint petal = Dark ? 0xFF2A3242u : 0xFFFFFFFFu;
        uint petalText = th.PetalText;

        for (int i = 0; i < slots; i++)
        {
            float p = Ease((now - _petalStart - i * StaggerMs) / (float)PetalMs);
            if (p <= 0.001f) continue;
            float mid = -90 + i * step;
            float h = waiting || i >= _hot.Length ? 0 : _hot[i];
            var op = waiting ? null : _items[i];
            uint family = op == null ? th.Accent : Icons.Family(op);
            uint fill = waiting ? (Dark ? 0x22FFFFFFu : 0x0F0B1220u) : Mix(petal, family, h);
            float outer = (InnerR + (OuterR - InnerR) * p + 6 * h) * s;
            c.RingSlice(cx, cy, outer, InnerR * s, mid - step / 2 + gapDeg / 2, mid + step / 2 - gapDeg / 2, Corner * s, Fade(fill, p));
            if (op == null) continue;

            float rad = mid * MathF.PI / 180;
            float r = rm + 3 * h * s;
            float lx = cx + MathF.Cos(rad) * r, ly = cy + MathF.Sin(rad) * r;
            float chord = 2 * rm * MathF.Sin(MathF.Min(step, 180) / 2 * MathF.PI / 180) * 0.84f;
            float bw = Math.Min(chord, 112 * s);
            uint text = Fade(Mix(petalText, 0xFFFFFFFF, h), p);
            if (op.Group == OpGroup.Convert)
            {
                c.Text(op.Label, lx - bw / 2, ly - 14 * s, bw, 24 * s, 16f * s, text, bold: true);
                c.RoundRect(lx - 9 * s, ly + 11 * s, 18 * s, 3.5f * s, 1.75f * s, Fade(Mix(family, 0xCCFFFFFF, h), p));
            }
            else
            {
                Icons.Draw(c, Icons.For(op), lx, ly - 11 * s, 22 * s, Fade(Mix(family, 0xFFFFFFFF, h), p));
                c.Text(op.Label, lx - bw / 2, ly + 2 * s, bw, 30 * s, 11.5f * s, text, bold: true, valign: Align.Near, wrap: true);
            }
        }

        DrawHub(c, cx, cy, s, now);
        c.ResetTransform();
        if (_strip) DrawStrip(c, cx, cy + (PlateR + 12) * s, s, open);
    }

    void DrawHub(Canvas c, float cx, float cy, float s, long now)
    {
        var th = _theme;
        c.FillCircle(cx, cy, HubR * s, Dark ? 0xFF121721u : 0xFFFFFFFFu);
        c.StrokeCircle(cx, cy, HubR * s, Dark ? 0x1FFFFFFFu : 0x120B1220u, 1);
        float hw = HubR * 2 * s * 0.80f;

        if (!GotFiles)
        {
            // waiting for Explorer to hand over the files: a small spinner
            float a = (now % 900) / 900f * 360;
            c.Arc(cx, cy, 14 * s, a, 100, th.Accent, 3 * s);
            c.Arc(cx, cy, 14 * s, a + 180, 100, Fade(th.Accent, 0.35f), 3 * s);
            return;
        }

        int n = _items.Count;
        if (_hover >= 0 && _hover < n)
        {
            var op = _items[_hover];
            uint family = Icons.Family(op);
            c.FillCircle(cx, cy - 14 * s, 21 * s, family);
            if (op.Group == OpGroup.Convert)
                c.Text(op.Label.Length > 4 ? op.Label[..4] : op.Label, cx - 21 * s, cy - 35 * s, 42 * s, 42 * s, (op.Label.Length > 3 ? 11f : 13f) * s, 0xFFFFFFFF, bold: true, rtl: false);
            else Icons.Draw(c, Icons.For(op), cx, cy - 14 * s, 22 * s, 0xFFFFFFFF);
            string caption = op.Group == OpGroup.Convert ? L.T($"Save as {op.En}", $"حفظ كـ {op.En}") : op.Label;
            c.Text(caption, cx - hw / 2, cy + 12 * s, hw, 20 * s, 12.5f * s, th.HubText, bold: true);
            return;
        }

        // the dragged file: Explorer's thumbnail, its name, and how many there are
        float ts = 50 * s;
        var thumb = _files.Count > 0 ? Thumbs.Get(_files[0], (int)Math.Round(ts * 1.5f), () => { if (Visible) Redraw(); }) : IntPtr.Zero;
        if (thumb != IntPtr.Zero) c.Image(thumb, cx - ts / 2, cy - 44 * s, ts, ts, 9 * s);
        else Icons.Draw(c, _files.Count > 0 && Directory.Exists(_files[0]) ? "folder" : "doc", cx, cy - 19 * s, 30 * s, th.HubSub);
        if (_files.Count > 1)
        {
            var more = $"+{_files.Count - 1}";
            float bw = Math.Max(20 * s, c.MeasureWidth(more, 10.5f * s, true) + 10 * s);
            c.RoundRect(cx + ts / 2 - bw + 6 * s, cy - 48 * s, bw, 18 * s, 9 * s, th.Accent);
            c.Text(more, cx + ts / 2 - bw + 6 * s, cy - 48 * s, bw, 18 * s, 10.5f * s, 0xFFFFFFFF, bold: true, rtl: false);
        }
        string name = n == 0 ? L.T("Nothing here", "لا خيارات")
            : _files.Count == 1 ? Path.GetFileName(_files[0].TrimEnd('\\', '/'))
            : L.T($"{_files.Count} files", $"{_files.Count} ملفات");
        c.Text(name, cx - hw / 2, cy + 11 * s, hw, 18 * s, 12f * s, th.HubText, bold: true,
            rtl: Core.Engines.Documents.IsRtl(name));
        string mode = Mode switch { OpGroup.Tool => L.T("Tools", "أدوات"), OpGroup.Action => L.T("Actions", "إجراءات"), _ => L.T("Convert", "تحويل") };
        c.Text(mode, cx - hw / 2, cy + 29 * s, hw, 16 * s, 10.5f * s, th.HubSub);
    }

    /// <summary>The three wheels as a segmented strip under the plate, with their keys.</summary>
    void DrawStrip(Canvas c, float cx, float top, float s, float open)
    {
        if (Hints() is not { } k) return;
        var th = _theme;
        (OpGroup g, string key, string label)[] tabs =
        [
            (OpGroup.Convert, ClickMode ? "Tab" : k.Open, L.T("Formats", "صيغ")),
            (OpGroup.Tool, ClickMode ? "Tab" : k.Tools, L.T("Tools", "أدوات")),
            (OpGroup.Action, ClickMode ? "Tab" : k.Actions, L.T("Actions", "إجراءات")),
        ];
        float h = 30 * s, pad = 4 * s;
        var widths = tabs.Select(t => c.MeasureWidth(t.key, 10.5f * s, true) + c.MeasureWidth(t.label, 11.5f * s, true) + 30 * s).ToArray();
        float total = widths.Sum() + pad * 2;
        float x = cx - total / 2;
        float alpha = Math.Clamp(open, 0, 1);
        c.RoundRect(x, top, total, h + pad * 2, (h + pad * 2) / 2, Fade(Dark ? 0xF01B212Cu : 0xF4FFFFFFu, alpha), Fade(Dark ? 0x22FFFFFFu : 0x140B1220u, alpha), 1);
        float tx = x + pad;
        bool rtl = L.Arabic;
        var order = rtl ? Enumerable.Range(0, 3).Reverse().ToArray() : [0, 1, 2];
        foreach (var i in order)
        {
            var (g, key, label) = tabs[i];
            bool on = g == Mode;
            float w = widths[i];
            if (on) c.RoundRect(tx, top + pad, w, h, h / 2, Fade(th.Accent, alpha));
            uint fg = Fade(on ? 0xFFFFFFFF : th.HubSub, alpha);
            float kw = c.MeasureWidth(key, 10.5f * s, true) + 10 * s;
            float lw = w - kw - 14 * s;
            // key chip then label (mirrored for Arabic)
            float kx = rtl ? tx + w - 8 * s - kw : tx + 8 * s, lx = rtl ? tx + 6 * s : kx + kw + 4 * s;
            c.RoundRect(kx, top + pad + 6 * s, kw, h - 12 * s, 4 * s, Fade(on ? 0x33FFFFFFu : (Dark ? 0x1AFFFFFFu : 0x0F0B1220u), alpha));
            c.Text(key, kx, top + pad + 6 * s, kw, h - 12 * s, 10.5f * s, fg, bold: true, rtl: false);
            c.Text(label, lx, top + pad, lw, h, 11.5f * s, fg, bold: on);
            tx += w;
        }
    }

    /// <summary>Draws one finished frame to a PNG, for design reviews (ShiftDrop.exe --preview).</summary>
    public void RenderPreview(string path, IReadOnlyList<string> files, OpGroup mode, int hover, Theme theme, float scale, bool waiting = false)
    {
        _theme = theme;
        _scale = scale;
        _strip = Hints() != null;
        ClickMode = false;
        GotFiles = !waiting;
        SetItems(files, waiting ? [] : ItemsFor(files, mode), mode);
        _hover = hover < _items.Count ? hover : -1;
        if (_hover >= 0) _hot[_hover] = 1;
        if (files.Count > 0) Thumbs.Preload(files[0], (int)Math.Round(50 * scale * 1.5f));
        _openStart = _petalStart = Environment.TickCount64 - 10_000;
        Canvas.Scale = scale;
        Canvas.Ensure(WidthPx, HeightPx);
        Render();
        Canvas.SavePng(path);
    }

    // ---------------- hit testing ----------------

    int HitTest(POINT screen)
    {
        if (!GotFiles || _items.Count == 0) return -1;
        float dx = screen.X - _center.X, dy = screen.Y - _center.Y;
        float d = MathF.Sqrt(dx * dx + dy * dy) / _scale;
        if (d < InnerR - 2 || d > PlateR + Margin) return -1;
        float ang = MathF.Atan2(dy, dx) * 180 / MathF.PI + 90; // 0 = top, clockwise
        float step = 360f / _items.Count;
        int idx = (int)MathF.Round(ang / step);
        return ((idx % _items.Count) + _items.Count) % _items.Count;
    }

    void SetHover(int idx)
    {
        if (idx == _hover) return;
        _hover = idx;
        Animate();
    }

    // ---------------- OLE drop target ----------------

    public void DragEnter(IReadOnlyList<string> files, POINT pt, uint keys)
    {
        if (!Visible) return;
        if (files.Count == 0) { Hide(); Done?.Invoke(); return; }
        GotFiles = true;
        SetItems(files, ItemsFor(files, Mode), Mode);
        _petalStart = Environment.TickCount64;
        _hover = HitTest(pt);
        Animate();
    }

    public bool DragOver(POINT pt, uint keys)
    {
        if (!Visible) return false;
        SetHover(HitTest(pt));
        return _hover >= 0;
    }

    public void DragLeave() => SetHover(-1);

    public bool Drop(POINT pt, uint keys)
    {
        int idx = HitTest(pt);
        var files = _files;
        var op = idx >= 0 && idx < _items.Count ? _items[idx] : null;
        Hide();
        Done?.Invoke();
        if (op == null) return false;
        // run after the drop returns, so Explorer's drag loop isn't kept waiting
        App.Post(() => Execute(op, files));
        return true;
    }

    // ---------------- click & keyboard mode ----------------

    protected override IntPtr Proc(uint msg, IntPtr w, IntPtr l)
    {
        switch (msg)
        {
            case W.WM_TIMER when (nuint)w == TimerAnim:
                if (!Step()) W.KillTimer(Hwnd, TimerAnim);
                Redraw();
                return 0;
            case W.WM_MOUSEMOVE when ClickMode:
                SetHover(HitTest(new POINT(X + W.LoWord(l), Y + W.HiWord(l))));
                return 0;
            case W.WM_LBUTTONUP when ClickMode:
            {
                int idx = HitTest(new POINT(X + W.LoWord(l), Y + W.HiWord(l)));
                if (idx >= 0) Run(idx);
                else
                {
                    float dx = X + W.LoWord(l) - _center.X, dy = Y + W.HiWord(l) - _center.Y;
                    if (MathF.Sqrt(dx * dx + dy * dy) / _scale < InnerR) NextMode(); // click the hub to switch
                    else Hide();
                }
                return 0;
            }
            case W.WM_RBUTTONUP when ClickMode:
                NextMode();
                return 0;
            case W.WM_KEYDOWN when ClickMode:
                Key((int)w);
                return 0;
            case W.WM_ACTIVATE when ClickMode && W.LoWord(w) == 0:
                Hide(); // clicked elsewhere
                return 0;
        }
        return base.Proc(msg, w, l);
    }

    void Key(int vk)
    {
        int n = _items.Count;
        switch (vk)
        {
            case W.VK_ESCAPE: Hide(); break;
            case W.VK_TAB: NextMode(); break;
            case W.VK_RIGHT or W.VK_DOWN when n > 0: SetHover((_hover + 1 + n) % n); break;
            case W.VK_LEFT or W.VK_UP when n > 0: SetHover((_hover - 1 + n) % n); break;
            case W.VK_RETURN or W.VK_SPACE when _hover >= 0: Run(_hover); break;
            case >= '1' and <= '9' when vk - '1' < n: Run(vk - '1'); break;
        }
    }

    void NextMode() => SetMode(Mode switch { OpGroup.Convert => OpGroup.Tool, OpGroup.Tool => OpGroup.Action, _ => OpGroup.Convert });

    void Run(int idx)
    {
        var op = _items[idx];
        var files = _files;
        Hide();
        App.Post(() => Execute(op, files));
    }
}

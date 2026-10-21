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
    const float PlateR = 164, OuterR = 150, InnerR = 66, HubR = 57, Margin = 20, Corner = 7, Gap = 7;
    const int AnimMs = 150;
    const nuint TimerAnim = 1;

    public required Func<IReadOnlyList<string>, OpGroup, List<Operation>> ItemsFor { get; init; }
    public required Action<Operation, IReadOnlyList<string>> Execute { get; init; }
    /// <summary>Raised when the drag carries no files, or after a drop: don't reopen until the button is released.</summary>
    public Action? Done { get; init; }
    /// <summary>Size multiplier from the settings.</summary>
    public Func<float> SizeFactor { get; init; } = () => 1f;
    /// <summary>Key names for the hint line (open, tools, actions), or null to hide hints.</summary>
    public Func<(string Open, string Tools, string Actions)?> Hints { get; init; } = () => ("Shift", "Ctrl", "Alt");

    public OpGroup Mode { get; private set; } = OpGroup.Convert;
    public bool ClickMode { get; private set; }
    /// <summary>True once Explorer has delivered the dragged files to us.</summary>
    public bool GotFiles { get; private set; }

    IReadOnlyList<string> _files = [];
    List<Operation> _items = [];
    int _hover = -1;
    long _animStart;
    float _scale = 1;
    Theme _theme = Theme.Light;
    POINT _center;

    public Wheel()
    {
        Create("ShiftDrop.Wheel", W.WS_EX_LAYERED | W.WS_EX_TOPMOST | W.WS_EX_TOOLWINDOW | W.WS_EX_NOACTIVATE, W.WS_POPUP);
        DropTarget.Register(Hwnd, new DropTarget(Hwnd, this));
    }

    int SizePx => (int)Math.Ceiling((PlateR + Margin) * 2 * _scale);

    /// <summary>Shows the wheel under the pointer during a drag (no focus taken).</summary>
    public void ShowForDrag(POINT at, OpGroup mode)
    {
        ClickMode = false;
        GotFiles = false;
        _files = [];
        _items = [];
        _hover = -1;
        Mode = mode;
        SetExStyle(activate: false);
        Open(at, activate: false);
    }

    /// <summary>Shows the wheel for files picked without dragging (Send to, or a second launch). Click or use the keyboard.</summary>
    public void ShowForFiles(POINT at, IReadOnlyList<string> files, OpGroup mode = OpGroup.Convert)
    {
        ClickMode = true;
        GotFiles = true;
        _files = files;
        Mode = mode;
        _items = ItemsFor(_files, Mode);
        _hover = _items.Count > 0 ? 0 : -1;
        SetExStyle(activate: true);
        Open(at, activate: true);
        W.SetForegroundWindow(Hwnd);
        W.SetFocus(Hwnd);
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
        Canvas.Scale = _scale;
        Canvas.Ensure(SizePx, SizePx);
        // centre on the pointer, but keep the whole wheel on the screen
        var work = W.MonitorAt(at).Work;
        int x = Math.Clamp(at.X - SizePx / 2, work.Left, Math.Max(work.Left, work.Right - SizePx));
        int y = Math.Clamp(at.Y - SizePx / 2, work.Top, Math.Max(work.Top, work.Bottom - SizePx));
        _center = new POINT(x + SizePx / 2, y + SizePx / 2);
        _animStart = Environment.TickCount64;
        Render(0);
        Present(x, y, 0);
        ShowTopmost(activate);
        W.SetTimer(Hwnd, TimerAnim, 15, IntPtr.Zero);
    }

    public void Hide()
    {
        if (!Visible) return;
        W.KillTimer(Hwnd, TimerAnim);
        HideWindow();
        ClickMode = false;
        GotFiles = false;
        _files = [];
        _items = [];
        _hover = -1;
    }

    public void SetMode(OpGroup mode)
    {
        if (mode == Mode || !Visible) return;
        Mode = mode;
        _items = GotFiles ? ItemsFor(_files, Mode) : [];
        _hover = ClickMode && _items.Count > 0 ? 0 : -1;
        Redraw();
    }

    void Redraw()
    {
        if (!Visible) return;
        double t = Math.Min(1, (Environment.TickCount64 - _animStart) / (double)AnimMs);
        Render(t);
        Present(X, Y, (byte)(255 * Ease(t)));
    }

    static double Ease(double t) => 1 - Math.Pow(1 - t, 3);

    // ---------------- drawing ----------------

    void Render(double t)
    {
        var c = Canvas;
        var th = _theme;
        float s = _scale, cx = c.Width / 2f, cy = c.Height / 2f;
        c.Clear();
        c.Transform(cx, cy, (float)(0.9 + 0.1 * Ease(t)));

        // soft shadow, then the plate
        for (int i = 3; i >= 1; i--) c.FillCircle(cx, cy + 3 * s, (PlateR + i * 4) * s, th.Shadow);
        c.FillCircle(cx, cy, PlateR * s, th.Plate);

        int n = _items.Count;
        bool waiting = !GotFiles;
        int slots = waiting ? 8 : n;
        float step = slots > 0 ? 360f / slots : 360;
        float rm = (OuterR + InnerR) / 2 * s;
        float gapDeg = Gap * s / rm * 180 / MathF.PI;

        for (int i = 0; i < slots; i++)
        {
            float mid = -90 + i * step;
            bool hot = !waiting && i == _hover;
            uint fill = waiting ? th.PetalLine : hot ? th.Accent : th.Petal;
            float outer = (OuterR + (hot ? 5 : 0)) * s;
            c.RingSlice(cx, cy, outer, InnerR * s, mid - step / 2 + gapDeg / 2, mid + step / 2 - gapDeg / 2, Corner * s, fill);
            if (waiting) continue;

            var op = _items[i];
            float rad = mid * MathF.PI / 180;
            float lx = cx + MathF.Cos(rad) * rm, ly = cy + MathF.Sin(rad) * rm;
            float chord = 2 * rm * MathF.Sin(MathF.Min(step, 180) / 2 * MathF.PI / 180) * 0.84f;
            float bw = Math.Min(chord, 112 * s), bh = 44 * s;
            bool convert = op.Group == OpGroup.Convert;
            c.Text(op.Label, lx - bw / 2, ly - bh / 2, bw, bh, (convert ? 15f : 12.5f) * s,
                hot ? th.AccentText : th.PetalText, bold: true, wrap: !convert);
        }

        // hub
        c.FillCircle(cx, cy, HubR * s, th.Hub);
        string title = Mode switch
        {
            OpGroup.Tool => L.T("Tools", "أدوات"),
            OpGroup.Action => L.T("Actions", "إجراءات"),
            _ => L.T("Convert", "تحويل"),
        };
        string main = _hover >= 0 && _hover < n ? _items[_hover].Label
            : waiting ? "…"
            : n == 0 ? L.T("Nothing here", "لا خيارات")
            : _files.Count == 1 ? Path.GetFileName(_files[0].TrimEnd('\\', '/'))
            : L.T($"{_files.Count} files", $"{_files.Count} ملفات");
        var keys = Hints();
        string hint = keys is not { } k ? ""
            : ClickMode ? L.T("Tab: switch", "Tab: تبديل")
            : Mode switch
            {
                OpGroup.Tool => L.T($"{k.Open} formats · {k.Actions} actions", $"{k.Open} صيغ · {k.Actions} إجراءات"),
                OpGroup.Action => L.T($"{k.Open} formats · {k.Tools} tools", $"{k.Open} صيغ · {k.Tools} أدوات"),
                _ => L.T($"{k.Tools} tools · {k.Actions} actions", $"{k.Tools} أدوات · {k.Actions} إجراءات"),
            };
        float hw = HubR * 2 * s * 0.86f;
        c.Text(title, cx - hw / 2, cy - 33 * s, hw, 16 * s, 11.5f * s, th.HubSub);
        c.Text(main, cx - hw / 2, cy - 14 * s, hw, 26 * s, 15f * s, th.HubText, bold: true);
        c.Text(hint, cx - hw / 2, cy + 14 * s, hw, 16 * s, 9.5f * s, th.HubSub);
        c.ResetTransform();
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
        Redraw();
    }

    // ---------------- OLE drop target ----------------

    public void DragEnter(IReadOnlyList<string> files, POINT pt, uint keys)
    {
        if (!Visible) return;
        if (files.Count == 0) { Hide(); Done?.Invoke(); return; }
        _files = files;
        GotFiles = true;
        _items = ItemsFor(_files, Mode);
        _hover = HitTest(pt);
        Redraw();
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
                Redraw();
                if (Environment.TickCount64 - _animStart > AnimMs) W.KillTimer(Hwnd, TimerAnim);
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

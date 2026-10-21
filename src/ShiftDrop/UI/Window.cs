using System.Runtime.InteropServices;
using ShiftDrop.Native;

namespace ShiftDrop.UI;

/// <summary>Minimal Win32 window: one class per subclass name, messages routed to <see cref="Proc"/>.</summary>
public abstract unsafe class Window
{
    static readonly Dictionary<IntPtr, Window> Live = [];
    static readonly HashSet<string> Registered = [];
    [ThreadStatic] static Window? _creating;

    public IntPtr Hwnd { get; private set; }

    protected void Create(string className, uint exStyle, uint style, string? title = null,
        int x = 0, int y = 0, int w = 0, int h = 0, IntPtr parent = default, IntPtr background = default)
    {
        Register(className, background);
        _creating = this;
        var hwnd = W.CreateWindowExW(exStyle, className, title, style, x, y, w, h, parent, IntPtr.Zero, W.GetModuleHandleW(null), IntPtr.Zero);
        _creating = null;
        if (hwnd == IntPtr.Zero) throw new InvalidOperationException($"CreateWindowEx({className}) failed: {Marshal.GetLastPInvokeError()}");
        Hwnd = hwnd;
        Live[hwnd] = this;
    }

    static void Register(string className, IntPtr background)
    {
        if (!Registered.Add(className)) return;
        fixed (char* name = className)
        {
            var wc = new WNDCLASSEXW
            {
                CbSize = (uint)sizeof(WNDCLASSEXW),
                WndProc = (IntPtr)(delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&Dispatch,
                Instance = W.GetModuleHandleW(null),
                Cursor = W.LoadCursorW(IntPtr.Zero, W.IDC_ARROW),
                Background = background,
                ClassName = (IntPtr)name,
            };
            if (W.RegisterClassExW(ref wc) == 0) Core.Log.Write($"RegisterClassEx({className}) failed: {Marshal.GetLastPInvokeError()}");
        }
    }

    [UnmanagedCallersOnly]
    static IntPtr Dispatch(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
    {
        try
        {
            if (!Live.TryGetValue(hwnd, out var win))
            {
                if (_creating == null) return W.DefWindowProcW(hwnd, msg, w, l);
                win = _creating;
                win.Hwnd = hwnd;
                Live[hwnd] = win;
            }
            var result = win.Proc(msg, w, l);
            if (msg == W.WM_NCDESTROY) { Live.Remove(hwnd); win.Hwnd = IntPtr.Zero; }
            return result;
        }
        catch (Exception e)
        {
            Core.Log.Write($"window message 0x{msg:X4}: {e}");
            return W.DefWindowProcW(hwnd, msg, w, l);
        }
    }

    protected virtual IntPtr Proc(uint msg, IntPtr w, IntPtr l) => W.DefWindowProcW(Hwnd, msg, w, l);

    public void Destroy() { if (Hwnd != IntPtr.Zero) W.DestroyWindow(Hwnd); }
}

/// <summary>A borderless, per-pixel-alpha window drawn with GDI+.</summary>
public abstract unsafe class LayeredWindow : Window
{
    protected readonly Canvas Canvas = new();
    public bool Visible { get; protected set; }
    public int X { get; protected set; }
    public int Y { get; protected set; }

    protected void Present(int x, int y, byte alpha = 255)
    {
        X = x; Y = y;
        Canvas.Flush();
        var dst = new POINT(x, y);
        var size = new SIZE(Canvas.Width, Canvas.Height);
        var src = new POINT(0, 0);
        var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };
        W.UpdateLayeredWindow(Hwnd, IntPtr.Zero, &dst, &size, Canvas.MemDc, &src, 0, &blend, W.ULW_ALPHA);
    }

    protected void ShowTopmost(bool activate)
    {
        W.SetWindowPos(Hwnd, W.HWND_TOPMOST, X, Y, Canvas.Width, Canvas.Height,
            W.SWP_SHOWWINDOW | (activate ? 0 : W.SWP_NOACTIVATE));
        Visible = true;
    }

    protected void HideWindow()
    {
        W.ShowWindow(Hwnd, W.SW_HIDE);
        Visible = false;
    }
}

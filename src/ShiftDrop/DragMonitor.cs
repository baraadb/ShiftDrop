using System.Runtime.InteropServices;
using ShiftDrop.Native;

namespace ShiftDrop;

/// <summary>
/// Watches the mouse on its own thread with a low-level hook (no admin rights needed).
/// The hook only records and posts messages, so it costs microseconds per event.
/// </summary>
public static unsafe class DragMonitor
{
    public const uint WM_HOOK_DOWN = W.WM_APP + 10, WM_HOOK_MOVE = W.WM_APP + 11, WM_HOOK_UP = W.WM_APP + 12;

    static IntPtr _target, _hook;
    static uint _thread;
    static int _tx, _ty;
    static bool _down, _moved;
    static POINT _start;

    public static void Start(IntPtr target)
    {
        _target = target;
        _tx = Math.Max(4, W.GetSystemMetrics(W.SM_CXDRAG) * 2);
        _ty = Math.Max(4, W.GetSystemMetrics(W.SM_CYDRAG) * 2);
        var ready = new ManualResetEventSlim();
        var t = new Thread(() =>
        {
            _thread = W.GetCurrentThreadId();
            _hook = W.SetWindowsHookExW(W.WH_MOUSE_LL, &Hook, W.GetModuleHandleW(null), 0);
            if (_hook == IntPtr.Zero) Core.Log.Write("mouse hook failed: " + Marshal.GetLastPInvokeError());
            ready.Set();
            while (W.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                W.TranslateMessage(ref msg);
                W.DispatchMessageW(ref msg);
            }
            if (_hook != IntPtr.Zero) W.UnhookWindowsHookEx(_hook);
        })
        { IsBackground = true, Name = "ShiftDrop mouse hook", Priority = ThreadPriority.AboveNormal };
        t.SetApartmentState(ApartmentState.MTA);
        t.Start();
        ready.Wait(2000);
    }

    public static void Stop()
    {
        if (_thread != 0) W.PostThreadMessageW(_thread, W.WM_QUIT, 0, 0);
    }

    [UnmanagedCallersOnly]
    static nint Hook(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var info = (MSLLHOOKSTRUCT*)lParam;
            switch ((uint)wParam)
            {
                case W.WM_LBUTTONDOWN:
                    _down = true;
                    _moved = false;
                    _start = info->Pt;
                    W.PostMessageW(_target, WM_HOOK_DOWN, info->Pt.X, info->Pt.Y);
                    break;
                case W.WM_MOUSEMOVE when _down && !_moved:
                    if (Math.Abs(info->Pt.X - _start.X) > _tx || Math.Abs(info->Pt.Y - _start.Y) > _ty)
                    {
                        _moved = true;
                        W.PostMessageW(_target, WM_HOOK_MOVE, info->Pt.X, info->Pt.Y);
                    }
                    break;
                case W.WM_LBUTTONUP:
                    if (_down)
                    {
                        _down = false;
                        W.PostMessageW(_target, WM_HOOK_UP, info->Pt.X, info->Pt.Y);
                    }
                    break;
            }
        }
        return W.CallNextHookEx(_hook, code, wParam, lParam);
    }
}

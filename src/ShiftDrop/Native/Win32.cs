using System.Runtime.InteropServices;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

namespace ShiftDrop.Native;

[StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
[StructLayout(LayoutKind.Sequential)] public struct SIZE { public int Cx, Cy; public SIZE(int cx, int cy) { Cx = cx; Cy = cy; } }
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }

[StructLayout(LayoutKind.Sequential)]
public struct MSG { public IntPtr Hwnd; public uint Message; public IntPtr WParam; public IntPtr LParam; public uint Time; public POINT Pt; public uint Private; }

[StructLayout(LayoutKind.Sequential)]
public struct WNDCLASSEXW
{
    public uint CbSize, Style;
    public IntPtr WndProc;
    public int ClsExtra, WndExtra;
    public IntPtr Instance, Icon, Cursor, Background, MenuName, ClassName, IconSm;
}

[StructLayout(LayoutKind.Sequential)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

[StructLayout(LayoutKind.Sequential)]
public struct BITMAPINFOHEADER
{
    public uint Size; public int Width, Height; public ushort Planes, BitCount;
    public uint Compression, SizeImage; public int XPelsPerMeter, YPelsPerMeter; public uint ClrUsed, ClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
public struct MSLLHOOKSTRUCT { public POINT Pt; public uint MouseData, Flags, Time; public IntPtr ExtraInfo; }

[StructLayout(LayoutKind.Sequential)]
public struct MONITORINFO { public uint CbSize; public RECT Monitor, Work; public uint Flags; }

[StructLayout(LayoutKind.Sequential)]
public struct COPYDATASTRUCT { public IntPtr Data; public uint CbData; public IntPtr LpData; }

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public unsafe struct NOTIFYICONDATAW
{
    public uint CbSize;
    public IntPtr Hwnd;
    public uint Id, Flags, CallbackMessage;
    public IntPtr Icon;
    public fixed char Tip[128];
    public uint State, StateMask;
    public fixed char Info[256];
    public uint TimeoutOrVersion;
    public fixed char InfoTitle[64];
    public uint InfoFlags;
    public Guid GuidItem;
    public IntPtr BalloonIcon;
}

[StructLayout(LayoutKind.Sequential)]
public struct BROWSEINFOW
{
    public IntPtr Owner, Root, DisplayName, Title;
    public uint Flags;
    public IntPtr Callback, LParam;
    public int Image;
}

[StructLayout(LayoutKind.Sequential)]
public struct SHELLEXECUTEINFOW
{
    public uint CbSize, Mask;
    public IntPtr Hwnd, Verb, File, Parameters, Directory;
    public int Show;
    public IntPtr InstApp, IDList, Class, KeyClass;
    public uint HotKey;
    public IntPtr IconOrMonitor, Process;
}

[StructLayout(LayoutKind.Sequential)]
public struct DOCINFOW { public int CbSize; public IntPtr DocName, Output, Datatype; public uint Type; }

public static unsafe partial class W
{
    // messages
    public const uint WM_DESTROY = 0x0002, WM_ACTIVATE = 0x0006, WM_SETFOCUS = 0x0007, WM_CLOSE = 0x0010, WM_QUIT = 0x0012,
        WM_SETTINGCHANGE = 0x001A, WM_SETCURSOR = 0x0020, WM_SETFONT = 0x0030, WM_COPYDATA = 0x004A,
        WM_NCDESTROY = 0x0082, WM_KEYDOWN = 0x0100, WM_CHAR = 0x0102, WM_SYSKEYDOWN = 0x0104, WM_COMMAND = 0x0111,
        WM_TIMER = 0x0113, WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202,
        WM_RBUTTONUP = 0x0205, WM_MOUSEWHEEL = 0x020A, WM_MOUSELEAVE = 0x02A3, WM_DPICHANGED = 0x02E0,
        WM_NULL = 0x0000, WM_APP = 0x8000;

    // styles
    public const uint WS_POPUP = 0x80000000, WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CAPTION = 0x00C00000,
        WS_SYSMENU = 0x00080000, WS_TABSTOP = 0x00010000, WS_BORDER = 0x00800000, WS_GROUP = 0x00020000;
    public const uint WS_EX_DLGMODALFRAME = 0x1, WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80,
        WS_EX_CLIENTEDGE = 0x200, WS_EX_LAYOUTRTL = 0x400000, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    public const uint ES_AUTOHSCROLL = 0x80, BS_PUSHBUTTON = 0, BS_DEFPUSHBUTTON = 1, SS_LEFT = 0;

    public const int SW_HIDE = 0, SW_SHOWNORMAL = 1, SW_SHOWNOACTIVATE = 4, SW_SHOW = 5;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_HIDEWINDOW = 0x80;
    public static readonly IntPtr HWND_TOPMOST = -1;
    public const int GWL_EXSTYLE = -20;
    public const int WH_MOUSE_LL = 14;
    public const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_SHIFT = 0x10, VK_CONTROL = 0x11,
        VK_MENU = 0x12, VK_ESCAPE = 0x1B, VK_SPACE = 0x20, VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;
    public const int SM_CXSMICON = 49, SM_CYSMICON = 50, SM_SWAPBUTTON = 23, SM_CXDRAG = 68, SM_CYDRAG = 69;
    public const uint ULW_ALPHA = 2;
    public const int IDC_ARROW = 32512, IDC_HAND = 32649;
    public const uint MF_STRING = 0, MF_GRAYED = 1, MF_CHECKED = 8, MF_POPUP = 0x10, MF_SEPARATOR = 0x800, MFT_RIGHTORDER = 0x2000, MFT_RIGHTJUSTIFY = 0x4000;
    public const uint TPM_RIGHTBUTTON = 0x2, TPM_RIGHTALIGN = 0x8, TPM_BOTTOMALIGN = 0x20, TPM_RETURNCMD = 0x100, TPM_LAYOUTRTL = 0x8000;
    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    public const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_SHOWTIP = 0x80;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint CF_UNICODETEXT = 13, GMEM_MOVEABLE = 2;
    public const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;
    public const uint ASFW_ANY = 0xFFFFFFFF;
    public const int IDOK = 1, IDCANCEL = 2;

    public static int LoWord(IntPtr v) => (short)((long)v & 0xFFFF);
    public static int HiWord(IntPtr v) => (short)(((long)v >> 16) & 0xFFFF);
    public static IntPtr MakeLParam(int lo, int hi) => (IntPtr)(((hi & 0xFFFF) << 16) | (lo & 0xFFFF));

    // ---- user32 ----
    [LibraryImport("user32.dll", SetLastError = true)] public static partial ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateWindowExW(uint exStyle, string className, string? windowName, uint style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [LibraryImport("user32.dll")] public static partial IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [LibraryImport("user32.dll")] public static partial int DestroyWindow(IntPtr hwnd);
    [LibraryImport("user32.dll")] public static partial int ShowWindow(IntPtr hwnd, int cmd);
    [LibraryImport("user32.dll")] public static partial int SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [LibraryImport("user32.dll")] public static partial int GetMessageW(out MSG msg, IntPtr hwnd, uint min, uint max);
    [LibraryImport("user32.dll")] public static partial int TranslateMessage(ref MSG msg);
    [LibraryImport("user32.dll")] public static partial IntPtr DispatchMessageW(ref MSG msg);
    [LibraryImport("user32.dll")] public static partial int IsDialogMessageW(IntPtr hwnd, ref MSG msg);
    [LibraryImport("user32.dll")] public static partial int PostMessageW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [LibraryImport("user32.dll")] public static partial IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [LibraryImport("user32.dll")] public static partial int PostThreadMessageW(uint thread, uint msg, IntPtr w, IntPtr l);
    [LibraryImport("user32.dll")] public static partial void PostQuitMessage(int code);
    [LibraryImport("user32.dll")] public static partial nuint SetTimer(IntPtr hwnd, nuint id, uint ms, IntPtr proc);
    [LibraryImport("user32.dll")] public static partial int KillTimer(IntPtr hwnd, nuint id);
    [LibraryImport("user32.dll")] public static partial int GetCursorPos(out POINT p);
    [LibraryImport("user32.dll")] public static partial IntPtr WindowFromPoint(POINT p);
    [LibraryImport("user32.dll")] public static partial IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [LibraryImport("user32.dll")] public static partial int GetClassNameW(IntPtr hwnd, char* buffer, int max);
    [LibraryImport("user32.dll")] public static partial short GetAsyncKeyState(int vk);
    [LibraryImport("user32.dll")] public static partial int GetSystemMetrics(int index);
    [LibraryImport("user32.dll")]
    public static partial int UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, POINT* dst, SIZE* size, IntPtr hdcSrc,
        POINT* src, uint key, BLENDFUNCTION* blend, uint flags);
    [LibraryImport("user32.dll")] public static partial IntPtr GetDC(IntPtr hwnd);
    [LibraryImport("user32.dll")] public static partial int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr SetWindowsHookExW(int id, delegate* unmanaged<int, nint, nint, nint> proc, IntPtr module, uint thread);
    [LibraryImport("user32.dll")] public static partial int UnhookWindowsHookEx(IntPtr hook);
    [LibraryImport("user32.dll")] public static partial nint CallNextHookEx(IntPtr hook, int code, nint w, nint l);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr LoadImageW(IntPtr instance, string name, uint type, int cx, int cy, uint flags);
    [LibraryImport("user32.dll")] public static partial int DestroyIcon(IntPtr icon);
    [LibraryImport("user32.dll")] public static partial IntPtr CreatePopupMenu();
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int AppendMenuW(IntPtr menu, uint flags, nuint id, string? text);
    [LibraryImport("user32.dll")] public static partial int DestroyMenu(IntPtr menu);
    [LibraryImport("user32.dll")] public static partial int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [LibraryImport("user32.dll")] public static partial int SetForegroundWindow(IntPtr hwnd);
    [LibraryImport("user32.dll")] public static partial IntPtr GetForegroundWindow();
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial uint RegisterWindowMessageW(string name);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial IntPtr FindWindowW(string? cls, string? title);
    [LibraryImport("user32.dll")] public static partial int AllowSetForegroundWindow(uint pid);
    [LibraryImport("user32.dll")] public static partial IntPtr MonitorFromPoint(POINT p, uint flags);
    [LibraryImport("user32.dll")] public static partial int GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);
    [LibraryImport("user32.dll")] public static partial int OpenClipboard(IntPtr hwnd);
    [LibraryImport("user32.dll")] public static partial int EmptyClipboard();
    [LibraryImport("user32.dll")] public static partial IntPtr SetClipboardData(uint format, IntPtr mem);
    [LibraryImport("user32.dll")] public static partial int CloseClipboard();
    [LibraryImport("user32.dll")] public static partial int GetWindowTextLengthW(IntPtr hwnd);
    [LibraryImport("user32.dll")] public static partial int GetWindowTextW(IntPtr hwnd, char* buffer, int max);
    [LibraryImport("user32.dll")] public static partial IntPtr SetFocus(IntPtr hwnd);
    [LibraryImport("user32.dll")] public static partial int EnableWindow(IntPtr hwnd, int enable);
    [LibraryImport("user32.dll")] public static partial IntPtr LoadCursorW(IntPtr instance, IntPtr id);
    [LibraryImport("user32.dll")] public static partial IntPtr SetCursor(IntPtr cursor);
    [LibraryImport("user32.dll")] public static partial nint GetWindowLongPtrW(IntPtr hwnd, int index);
    [LibraryImport("user32.dll")] public static partial nint SetWindowLongPtrW(IntPtr hwnd, int index, nint value);
    [LibraryImport("user32.dll")] public static partial int IsWindowVisible(IntPtr hwnd);
    [LibraryImport("user32.dll")] public static partial uint GetDpiForWindow(IntPtr hwnd);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int DrawTextW(IntPtr hdc, string text, int count, ref RECT rect, uint format);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);
    [LibraryImport("user32.dll")] public static partial int ScreenToClient(IntPtr hwnd, ref POINT p);
    [LibraryImport("user32.dll")] public static partial int GetWindowRect(IntPtr hwnd, out RECT r);

    // ---- kernel32 ----
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial IntPtr GetModuleHandleW(string? name);
    [LibraryImport("kernel32.dll")] public static partial uint GetCurrentThreadId();
    [LibraryImport("kernel32.dll")] public static partial IntPtr GlobalAlloc(uint flags, nuint bytes);
    [LibraryImport("kernel32.dll")] public static partial IntPtr GlobalLock(IntPtr mem);
    [LibraryImport("kernel32.dll")] public static partial int GlobalUnlock(IntPtr mem);
    [LibraryImport("kernel32.dll")] public static partial IntPtr GlobalFree(IntPtr mem);

    // ---- gdi32 ----
    [LibraryImport("gdi32.dll")] public static partial IntPtr CreateCompatibleDC(IntPtr hdc);
    [LibraryImport("gdi32.dll")] public static partial IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [LibraryImport("gdi32.dll")] public static partial IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [LibraryImport("gdi32.dll")] public static partial int DeleteObject(IntPtr obj);
    [LibraryImport("gdi32.dll")] public static partial int DeleteDC(IntPtr hdc);
    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic,
        uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateDCW(string? driver, string device, string? port, IntPtr devmode);
    [LibraryImport("gdi32.dll")] public static partial int StartDocW(IntPtr hdc, ref DOCINFOW info);
    [LibraryImport("gdi32.dll")] public static partial int StartPage(IntPtr hdc);
    [LibraryImport("gdi32.dll")] public static partial int EndPage(IntPtr hdc);
    [LibraryImport("gdi32.dll")] public static partial int EndDoc(IntPtr hdc);
    [LibraryImport("gdi32.dll")] public static partial int AbortDoc(IntPtr hdc);
    [LibraryImport("gdi32.dll")] public static partial int GetDeviceCaps(IntPtr hdc, int index);
    [LibraryImport("gdi32.dll")] public static partial int SetBkMode(IntPtr hdc, int mode);
    [LibraryImport("gdi32.dll")] public static partial uint SetTextColor(IntPtr hdc, uint color);

    // ---- shell32 / ole32 / shcore ----
    [LibraryImport("shell32.dll")] public static partial int Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);
    [LibraryImport("shell32.dll")] public static partial uint DragQueryFileW(IntPtr drop, uint index, char* file, uint max);
    [LibraryImport("shell32.dll")] public static partial IntPtr SHBrowseForFolderW(ref BROWSEINFOW info);
    [LibraryImport("shell32.dll")] public static partial int SHGetPathFromIDListW(IntPtr pidl, char* path);
    [LibraryImport("shell32.dll")] public static partial int ShellExecuteExW(ref SHELLEXECUTEINFOW info);
    [LibraryImport("shell32.dll")] public static partial int SHGetKnownFolderPath(in Guid id, uint flags, IntPtr token, out IntPtr path);
    [LibraryImport("ole32.dll")] public static partial int OleInitialize(IntPtr reserved);
    [LibraryImport("ole32.dll")] public static partial void OleUninitialize();
    [LibraryImport("ole32.dll")] public static partial int RegisterDragDrop(IntPtr hwnd, IntPtr dropTarget);
    [LibraryImport("ole32.dll")] public static partial int RevokeDragDrop(IntPtr hwnd);
    [LibraryImport("ole32.dll")] public static partial void ReleaseStgMedium(STGMEDIUM* medium);
    [LibraryImport("ole32.dll")] public static partial void CoTaskMemFree(IntPtr p);
    [LibraryImport("ole32.dll")] public static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint obj);
    [LibraryImport("shcore.dll")] public static partial int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    // ---- helpers ----

    public static string ClassName(IntPtr hwnd)
    {
        char* buf = stackalloc char[256];
        int n = GetClassNameW(hwnd, buf, 256);
        return n > 0 ? new string(buf, 0, n) : "";
    }

    public static string GetText(IntPtr hwnd)
    {
        int len = GetWindowTextLengthW(hwnd);
        if (len <= 0) return "";
        var buf = new char[len + 1];
        fixed (char* b = buf) { int n = GetWindowTextW(hwnd, b, buf.Length); return new string(b, 0, n); }
    }

    public static bool KeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public static bool PrimaryButtonDown() => KeyDown(GetSystemMetrics(SM_SWAPBUTTON) != 0 ? VK_RBUTTON : VK_LBUTTON);

    public static POINT Cursor() { GetCursorPos(out var p); return p; }

    public static MONITORINFO MonitorAt(POINT p)
    {
        var info = new MONITORINFO { CbSize = (uint)sizeof(MONITORINFO) };
        GetMonitorInfoW(MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST), ref info);
        return info;
    }

    public static float ScaleAt(POINT p)
    {
        try
        {
            if (GetDpiForMonitor(MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST), 0, out var x, out _) == 0 && x > 0) return x / 96f;
        }
        catch { }
        return 1f;
    }

    public static string? KnownFolder(Guid id)
    {
        if (SHGetKnownFolderPath(in id, 0, IntPtr.Zero, out var p) != 0) return null;
        try { return Marshal.PtrToStringUni(p); } finally { CoTaskMemFree(p); }
    }

    public static readonly Guid FOLDERID_Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    public static void SetClipboardText(IntPtr owner, string text)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(owner) != 0) break;
            Thread.Sleep(20);
            if (attempt == 9) return;
        }
        try
        {
            EmptyClipboard();
            var bytes = (nuint)((text.Length + 1) * 2);
            var mem = GlobalAlloc(GMEM_MOVEABLE, bytes);
            var ptr = (char*)GlobalLock(mem);
            fixed (char* src = text) Buffer.MemoryCopy(src, ptr, (long)bytes, text.Length * 2);
            ptr[text.Length] = '\0';
            GlobalUnlock(mem);
            if (SetClipboardData(CF_UNICODETEXT, mem) == IntPtr.Zero) GlobalFree(mem);
        }
        finally { CloseClipboard(); }
    }
}

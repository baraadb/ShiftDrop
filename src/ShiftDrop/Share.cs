using System.Runtime.InteropServices;
using ShiftDrop.Core;
using ShiftDrop.Native;
using ShiftDrop.UI;
#if WINDOWS10_0_17763_0_OR_GREATER
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
#endif

namespace ShiftDrop;

/// <summary>
/// Opens the Windows share sheet (Nearby sharing, Mail, Teams, WhatsApp...) for files.
/// If that isn't possible, the files go to the clipboard so they can be pasted anywhere.
/// </summary>
public static unsafe partial class Share
{
    static IReadOnlyList<string> _files = [];

    public static void Files(App app, IReadOnlyList<string> files)
    {
        _files = files.ToList();
        try
        {
#if WINDOWS10_0_17763_0_OR_GREATER
            ShowSheet();
            return;
#else
            throw new PlatformNotSupportedException();
#endif
        }
        catch (Exception e)
        {
            Log.Write("share sheet: " + e);
            if (CopyFiles(app.Hwnd, files))
                app.Hud.Info(L.T("Copied for sharing", "نُسخت للمشاركة"),
                    L.T("The share window could not open, so the files were copied. Paste them in any app.",
                        "تعذّر فتح نافذة المشاركة، فنُسخت الملفات. الصقها في أي تطبيق."), seconds: 8);
            else
                app.Hud.Info(L.T("Share", "مشاركة"), e.Message, error: true);
        }
    }

#if WINDOWS10_0_17763_0_OR_GREATER
    static ShareAnchor? _anchor;
    static DataTransferManager? _manager;
    static IntPtr _interop;

    static void ShowSheet()
    {
        // the sheet needs a visible window of ours: an invisible one at the pointer
        _anchor ??= new ShareAnchor();
        _anchor.ShowAt(W.Cursor());
        var hwnd = _anchor.Hwnd;

        if (_interop == IntPtr.Zero)
        {
            const string cls = "Windows.ApplicationModel.DataTransfer.DataTransferManager";
            Check(WindowsCreateString(cls, cls.Length, out var hs));
            try
            {
                var iid = new Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"); // IDataTransferManagerInterop
                Check(RoGetActivationFactory(hs, in iid, out _interop));
            }
            finally { WindowsDeleteString(hs); }
        }
        var vtbl = *(IntPtr**)_interop;
        if (_manager == null)
        {
            var dtmIid = new Guid("A5CAEE9B-8708-49D1-8D36-67D25A8DA00C"); // IDataTransferManager
            IntPtr dtm;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)vtbl[3])(_interop, hwnd, &dtmIid, &dtm));
            try { _manager = WinRT.MarshalInterface<DataTransferManager>.FromAbi(dtm); }
            finally { Marshal.Release(dtm); }
            _manager.DataRequested += OnDataRequested;
        }
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)vtbl[4])(_interop, hwnd));
    }

    static async void OnDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        var request = args.Request;
        var deferral = request.GetDeferral();
        try
        {
            var files = _files;
            var items = new List<IStorageItem>();
            foreach (var f in files)
            {
                if (Directory.Exists(f)) items.Add(await StorageFolder.GetFolderFromPathAsync(f));
                else items.Add(await StorageFile.GetFileFromPathAsync(f));
            }
            var data = request.Data;
            data.Properties.Title = files.Count == 1 ? Path.GetFileName(files[0]) : L.T($"{files.Count} files", $"{files.Count} ملفات");
            data.Properties.Description = "ShiftDrop";
            data.RequestedOperation = DataPackageOperation.Copy;
            data.SetStorageItems(items, true);
        }
        catch (Exception e)
        {
            Log.Write("share data: " + e);
            request.FailWithDisplayText(e.Message);
        }
        finally { deferral.Complete(); }
    }

    static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string source, int length, out IntPtr hstring);
    [LibraryImport("combase.dll")] private static partial int WindowsDeleteString(IntPtr hstring);
    [LibraryImport("combase.dll")] private static partial int RoGetActivationFactory(IntPtr classId, in Guid iid, out IntPtr factory);
#endif

    /// <summary>Puts the files on the clipboard the way Explorer's Copy does (CF_HDROP).</summary>
    public static bool CopyFiles(IntPtr owner, IReadOnlyList<string> files)
    {
        var list = string.Join("\0", files) + "\0\0";
        int header = 20; // DROPFILES: pFiles, pt, fNC, fWide
        var mem = W.GlobalAlloc(W.GMEM_MOVEABLE | 0x40 /*ZEROINIT*/, (nuint)(header + list.Length * 2));
        if (mem == IntPtr.Zero) return false;
        var p = (byte*)W.GlobalLock(mem);
        *(int*)p = header;
        *(int*)(p + 16) = 1; // wide characters
        fixed (char* src = list) Buffer.MemoryCopy(src, p + header, list.Length * 2, list.Length * 2);
        W.GlobalUnlock(mem);
        for (int attempt = 0; attempt < 10 && W.OpenClipboard(owner) == 0; attempt++) Thread.Sleep(20);
        try
        {
            W.EmptyClipboard();
            if (W.SetClipboardData(15 /*CF_HDROP*/, mem) != IntPtr.Zero) return true;
        }
        finally { W.CloseClipboard(); }
        W.GlobalFree(mem);
        return false;
    }
}

/// <summary>A see-through 1×1 window the share sheet can belong to.</summary>
sealed partial class ShareAnchor : Window
{
    const nuint TimerHide = 1;

    public ShareAnchor() =>
        Create("ShiftDrop.ShareAnchor", W.WS_EX_TOOLWINDOW | W.WS_EX_TOPMOST | W.WS_EX_LAYERED, W.WS_POPUP, "ShiftDrop");

    public void ShowAt(POINT p)
    {
        SetLayeredWindowAttributes(Hwnd, 0, 0, 2 /*LWA_ALPHA*/);
        W.SetWindowPos(Hwnd, W.HWND_TOPMOST, p.X, p.Y, 1, 1, W.SWP_SHOWWINDOW);
        W.SetForegroundWindow(Hwnd);
        W.SetTimer(Hwnd, TimerHide, 10 * 60 * 1000, IntPtr.Zero);
    }

    protected override IntPtr Proc(uint msg, IntPtr w, IntPtr l)
    {
        if (msg == W.WM_TIMER && (nuint)w == TimerHide)
        {
            W.KillTimer(Hwnd, TimerHide);
            W.ShowWindow(Hwnd, W.SW_HIDE);
            return 0;
        }
        return base.Proc(msg, w, l);
    }

    [LibraryImport("user32.dll")] private static partial int SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
}

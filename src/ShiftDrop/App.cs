using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ShiftDrop.Core;
using ShiftDrop.Core.Engines;
using ShiftDrop.Native;
using ShiftDrop.UI;

namespace ShiftDrop;

/// <summary>
/// The tray app: a hidden window that receives the mouse-hook messages, decides when a drag
/// deserves the wheel, owns the tray icon, and starts jobs.
/// </summary>
public sealed unsafe class App : Window
{
    public const string MainClass = "ShiftDrop.Main";
    const uint WM_INVOKE = W.WM_APP + 1, WM_TRAY = W.WM_APP + 2;
    const nuint TimerPoll = 1, TimerHide = 2;
    public const nint CopyFiles = 1, CopyShowTip = 2, CopyQuit = 3;

    static App? _instance;
    static readonly ConcurrentQueue<Action> Pending = new();

    public Settings Settings { get; }
    public Services Services { get; }
    public JobQueue Queue { get; }
    public Wheel Wheel { get; private set; } = null!;
    public Hud Hud { get; private set; } = null!;

    readonly uint _taskbarCreated;
    IntPtr _icon;
    bool _down, _onShell, _polling, _suppress, _prevShift, _prevCtrl, _prevAlt;
    long _shownAt;
    POINT _shownPt;

    /// <summary>Runs an action on the UI thread.</summary>
    public static void Post(Action a)
    {
        Pending.Enqueue(a);
        if (_instance != null) W.PostMessageW(_instance.Hwnd, WM_INVOKE, 0, 0);
    }

    App(Settings settings, Services services)
    {
        Settings = settings;
        Services = services;
        Queue = new JobQueue(services)
        {
            DestinationFor = input => Settings.SaveTo == "downloads"
                ? Downloads()
                : Path.GetDirectoryName(Path.GetFullPath(input.TrimEnd('\\', '/'))) ?? Downloads(),
            Fallback = Downloads,
        };
        Queue.Changed += job => Post(() => Hud.Update(job));
        _taskbarCreated = W.RegisterWindowMessageW("TaskbarCreated");
        Create(MainClass, W.WS_EX_TOOLWINDOW, W.WS_POPUP, "ShiftDrop");
    }

    static string Downloads() =>
        W.KnownFolder(W.FOLDERID_Downloads) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public static int Run(IReadOnlyList<string> startFiles)
    {
        var appDir = AppContext.BaseDirectory;
        Log.Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShiftDrop", "logs");
        Log.Write($"start {typeof(App).Assembly.GetName().Version} in {appDir}");
        var settings = Settings.Load();
        L.Arabic = settings.Arabic;

        Gdip.Start();
        W.OleInitialize(IntPtr.Zero);

        var tools = Path.Combine(appDir, "tools");
        Pdfium.SearchDirs.Add(tools);
        var services = new Services { FFmpeg = FFmpeg.Locate(tools, appDir) };
        foreach (var sz in new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe") })
            if (File.Exists(sz)) { services.SevenZipPath = sz; break; }

        var app = new App(settings, services);
        _instance = app;
        app.Hud = new Hud();
        app.Wheel = new Wheel
        {
            ItemsFor = (files, group) => Catalog.For(files, group, services, app.Settings.Hidden.ToHashSet()),
            Execute = app.Execute,
            Done = () => app._suppress = true,
        };
        AppActions.Register(app);
        app.AddTrayIcon();
        DragMonitor.Start(app.Hwnd);

        // slower checks off the UI thread: the printer for document PDFs, PDFium, the best H.264 encoder
        Task.Run(() =>
        {
            try { if (PrintPdfRenderer.Available()) services.DocRenderer = new PrintPdfRenderer(); } catch (Exception e) { Log.Write("printer: " + e.Message); }
            bool pdf = Pdfium.Available;
            Post(() => app.StartupNotes(pdf));
            if (services.FFmpeg.Available) { Thread.Sleep(3000); Log.Write("h264 encoder: " + services.FFmpeg.H264); }
        });

        if (startFiles.Count > 0) Post(() => app.Wheel.ShowForFiles(W.Cursor(), startFiles));

        while (W.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            W.TranslateMessage(ref msg);
            W.DispatchMessageW(ref msg);
        }

        DragMonitor.Stop();
        app.RemoveTrayIcon();
        W.OleUninitialize();
        Gdip.Stop();
        Log.Write("exit");
        return 0;
    }

    void StartupNotes(bool pdfium)
    {
        if (!Services.FFmpeg.Available)
            Hud.Info(L.T("FFmpeg is missing", "FFmpeg غير موجود"),
                L.T("Image, video and audio conversions are off. Reinstall ShiftDrop to bring them back.",
                    "تحويلات الصور والفيديو والصوت معطلة. أعد تثبيت ShiftDrop لإرجاعها."), error: true, seconds: 15);
        if (!pdfium)
            Hud.Info(L.T("PDFium is missing", "PDFium غير موجود"),
                L.T("PDF tools are off. Reinstall ShiftDrop to bring them back.", "أدوات PDF معطلة. أعد تثبيت ShiftDrop لإرجاعها."), error: true, seconds: 15);
        if (!Settings.Welcomed)
        {
            ShowTip(20);
            Settings.Welcomed = true;
            Settings.Save();
        }
    }

    void ShowTip(double seconds = 10) =>
        Hud.Info(L.T("ShiftDrop is ready", "ShiftDrop جاهز"),
            L.T("Drag a file and press Shift, then drop it on a format. While the wheel is open: Ctrl for tools, Alt for actions.",
                "اسحب ملفًا واضغط Shift، ثم أفلته على الصيغة. والعجلة مفتوحة: Ctrl للأدوات، Alt للإجراءات."),
            seconds: seconds);

    // ---------------- running operations ----------------

    public void Execute(Operation op, IReadOnlyList<string> files)
    {
        try
        {
            if (op.Immediate != null) { op.Immediate(files); return; }
            Dictionary<string, string>? options = null;
            if (op.Fields is { Length: > 0 })
            {
                var subtitle = files.Count == 1 ? Path.GetFileName(files[0]) : L.T($"{files.Count} files", $"{files.Count} ملفات");
                options = InputDialog.Ask(op.Label, subtitle, op.Fields);
                if (options == null) return;
            }
            var job = Queue.Enqueue(op, files, options);
            Hud.Track(job);
            Hud.Update(job);
        }
        catch (Exception e)
        {
            Log.Write($"execute {op.Id}: {e}");
            Hud.Info(op.Label, e.Message, error: true);
        }
    }

    // ---------------- drag detection ----------------

    static readonly HashSet<string> ShellClasses = ["CabinetWClass", "ExploreWClass", "Progman", "WorkerW"];

    static bool IsShellAt(POINT p)
    {
        var hwnd = W.WindowFromPoint(p);
        if (hwnd == IntPtr.Zero) return false;
        var root = W.GetAncestor(hwnd, 2 /*GA_ROOT*/);
        return ShellClasses.Contains(W.ClassName(root));
    }

    void Poll()
    {
        if (!W.PrimaryButtonDown()) { EndDrag(); return; }
        bool shift = W.KeyDown(W.VK_SHIFT), ctrl = W.KeyDown(W.VK_CONTROL), alt = W.KeyDown(W.VK_MENU);
        if (!Wheel.Visible)
        {
            if (shift && !_suppress)
            {
                var mode = ctrl ? OpGroup.Tool : alt ? OpGroup.Action : OpGroup.Convert;
                _shownPt = W.Cursor();
                _shownAt = Environment.TickCount64;
                Wheel.ShowForDrag(_shownPt, mode);
            }
        }
        else if (!Wheel.ClickMode)
        {
            // once open, a tap switches the wheel; the keys can be released
            if (ctrl && !_prevCtrl) Wheel.SetMode(OpGroup.Tool);
            else if (alt && !_prevAlt) Wheel.SetMode(OpGroup.Action);
            else if (shift && !_prevShift) Wheel.SetMode(OpGroup.Convert);

            // no files arrived although the pointer moved: a selection box, not a file drag
            var p = W.Cursor();
            if (!Wheel.GotFiles && Environment.TickCount64 - _shownAt > 450 &&
                Math.Abs(p.X - _shownPt.X) + Math.Abs(p.Y - _shownPt.Y) > 8)
            {
                _suppress = true;
                Wheel.Hide();
            }
        }
        _prevShift = shift; _prevCtrl = ctrl; _prevAlt = alt;
    }

    void EndDrag()
    {
        _down = false;
        _suppress = false;
        if (_polling) { W.KillTimer(Hwnd, TimerPoll); _polling = false; }
        // the drop reaches the wheel through OLE right around now; hide only if it didn't
        if (Wheel.Visible && !Wheel.ClickMode) W.SetTimer(Hwnd, TimerHide, 300, IntPtr.Zero);
    }

    // ---------------- window messages ----------------

    protected override IntPtr Proc(uint msg, IntPtr w, IntPtr l)
    {
        switch (msg)
        {
            case WM_INVOKE:
                while (Pending.TryDequeue(out var a))
                {
                    try { a(); } catch (Exception e) { Log.Write("invoke: " + e); }
                }
                return 0;

            case DragMonitor.WM_HOOK_DOWN:
                if (Wheel.Visible && !Wheel.ClickMode) Wheel.Hide();
                _down = true;
                _suppress = false;
                var at = new POINT((int)w, (int)l);
                _onShell = !Settings.Paused && (Settings.AllApps || IsShellAt(at));
                return 0;

            case DragMonitor.WM_HOOK_MOVE:
                if (_down && _onShell && !_polling)
                {
                    _polling = true;
                    _prevShift = _prevCtrl = _prevAlt = false;
                    W.SetTimer(Hwnd, TimerPoll, 25, IntPtr.Zero);
                    Poll();
                }
                return 0;

            case DragMonitor.WM_HOOK_UP:
                EndDrag();
                return 0;

            case W.WM_TIMER:
                if ((nuint)w == TimerPoll) Poll();
                else if ((nuint)w == TimerHide)
                {
                    W.KillTimer(Hwnd, TimerHide);
                    if (!_down && Wheel.Visible && !Wheel.ClickMode) Wheel.Hide();
                }
                return 0;

            case WM_TRAY:
                switch ((uint)l)
                {
                    case W.WM_LBUTTONUP: ShowTip(); break;
                    case W.WM_RBUTTONUP: TrayMenu(); break;
                }
                return 0;

            case W.WM_COPYDATA:
            {
                var cds = (COPYDATASTRUCT*)l;
                if (cds->Data == CopyQuit) { W.PostQuitMessage(0); return 1; }
                if (cds->Data == CopyShowTip) { ShowTip(); return 1; }
                var text = new string((char*)cds->LpData, 0, (int)cds->CbData / 2);
                var files = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
                if (files.Count > 0) Post(() => Wheel.ShowForFiles(W.Cursor(), files));
                return 1;
            }
        }
        if (msg == _taskbarCreated && _taskbarCreated != 0) { AddTrayIcon(); return 0; }
        return base.Proc(msg, w, l);
    }

    // ---------------- tray ----------------

    NOTIFYICONDATAW TrayData()
    {
        var d = new NOTIFYICONDATAW
        {
            CbSize = (uint)sizeof(NOTIFYICONDATAW),
            Hwnd = Hwnd,
            Id = 1,
            Flags = W.NIF_MESSAGE | W.NIF_ICON | W.NIF_TIP,
            CallbackMessage = WM_TRAY,
            Icon = _icon,
        };
        var tip = Settings.Paused ? L.T("ShiftDrop (paused)", "ShiftDrop (متوقف)") : L.T("ShiftDrop — drag a file, press Shift", "ShiftDrop — اسحب ملفًا واضغط Shift");
        for (int i = 0; i < tip.Length && i < 127; i++) d.Tip[i] = tip[i];
        return d;
    }

    void AddTrayIcon()
    {
        if (_icon == IntPtr.Zero)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "shiftdrop.ico");
            _icon = W.LoadImageW(IntPtr.Zero, path, W.IMAGE_ICON, W.GetSystemMetrics(W.SM_CXSMICON), W.GetSystemMetrics(W.SM_CYSMICON), W.LR_LOADFROMFILE);
        }
        var d = TrayData();
        if (W.Shell_NotifyIconW(W.NIM_ADD, ref d) == 0) W.Shell_NotifyIconW(W.NIM_MODIFY, ref d);
    }

    void UpdateTrayIcon()
    {
        var d = TrayData();
        W.Shell_NotifyIconW(W.NIM_MODIFY, ref d);
    }

    void RemoveTrayIcon()
    {
        var d = TrayData();
        W.Shell_NotifyIconW(W.NIM_DELETE, ref d);
    }

    void TrayMenu()
    {
        bool rtl = L.Arabic;
        uint dir = rtl ? W.MFT_RIGHTORDER | W.MFT_RIGHTJUSTIFY : 0;
        uint Check(bool on) => on ? W.MF_CHECKED : 0;
        var menu = W.CreatePopupMenu();
        var lang = W.CreatePopupMenu();
        var save = W.CreatePopupMenu();
        try
        {
            W.AppendMenuW(menu, dir, 1, Settings.Paused ? L.T("Resume ShiftDrop", "تشغيل ShiftDrop") : L.T("Pause ShiftDrop", "إيقاف ShiftDrop مؤقتًا"));
            W.AppendMenuW(menu, W.MF_SEPARATOR, 0, null);
            W.AppendMenuW(lang, dir | Check(Settings.Language == "auto"), 11, L.T("Same as Windows", "مثل Windows"));
            W.AppendMenuW(lang, dir | Check(Settings.Language == "ar"), 12, "العربية");
            W.AppendMenuW(lang, dir | Check(Settings.Language == "en"), 13, "English");
            W.AppendMenuW(menu, dir | W.MF_POPUP, (nuint)lang, L.T("Language", "اللغة"));
            W.AppendMenuW(save, dir | Check(Settings.SaveTo != "downloads"), 21, L.T("Next to the original", "بجانب الملف الأصلي"));
            W.AppendMenuW(save, dir | Check(Settings.SaveTo == "downloads"), 22, L.T("Downloads folder", "مجلد التنزيلات"));
            W.AppendMenuW(menu, dir | W.MF_POPUP, (nuint)save, L.T("Save results", "حفظ النتائج"));
            W.AppendMenuW(menu, dir | Check(Settings.AllApps), 30, L.T("Work in all apps", "العمل في كل التطبيقات"));
            W.AppendMenuW(menu, dir | Check(Settings.StartWithWindows), 40, L.T("Start with Windows", "التشغيل مع Windows"));
            W.AppendMenuW(menu, W.MF_SEPARATOR, 0, null);
            W.AppendMenuW(menu, dir, 50, L.T("How to use", "طريقة الاستخدام"));
            W.AppendMenuW(menu, dir, 51, L.T("Open log folder", "فتح مجلد السجلات"));
            W.AppendMenuW(menu, dir, 52, L.T("About ShiftDrop", "عن ShiftDrop"));
            W.AppendMenuW(menu, W.MF_SEPARATOR, 0, null);
            W.AppendMenuW(menu, dir, 99, L.T("Quit", "خروج"));

            var p = W.Cursor();
            W.SetForegroundWindow(Hwnd);
            int cmd = W.TrackPopupMenuEx(menu, W.TPM_RETURNCMD | W.TPM_RIGHTBUTTON | W.TPM_BOTTOMALIGN | (rtl ? W.TPM_LAYOUTRTL : 0), p.X, p.Y, Hwnd, IntPtr.Zero);
            W.PostMessageW(Hwnd, W.WM_NULL, 0, 0);
            OnMenu(cmd);
        }
        finally { W.DestroyMenu(menu); } // also destroys the submenus
    }

    void OnMenu(int cmd)
    {
        switch (cmd)
        {
            case 1: Settings.Paused = !Settings.Paused; break;
            case 11: Settings.Language = "auto"; break;
            case 12: Settings.Language = "ar"; break;
            case 13: Settings.Language = "en"; break;
            case 21: Settings.SaveTo = "beside"; break;
            case 22: Settings.SaveTo = "downloads"; break;
            case 30: Settings.AllApps = !Settings.AllApps; break;
            case 40: Settings.StartWithWindows = !Settings.StartWithWindows; return;
            case 50: ShowTip(15); return;
            case 51:
                Directory.CreateDirectory(Log.Folder!);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.Folder}\"") { UseShellExecute = false });
                return;
            case 52:
                Hud.Info($"ShiftDrop {typeof(App).Assembly.GetName().Version?.ToString(3)}",
                    L.T("Free and open source (MIT). Uses FFmpeg (LGPL) and PDFium (BSD). Everything runs on this PC.",
                        "مجاني ومفتوح المصدر (MIT). يستخدم FFmpeg (LGPL) وPDFium (BSD). كل شيء يعمل على جهازك."), seconds: 12);
                return;
            case 99: W.PostQuitMessage(0); return;
            default: return;
        }
        L.Arabic = Settings.Arabic;
        Settings.Save();
        UpdateTrayIcon();
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ShiftDrop.Core;
using ShiftDrop.Core.Engines;
using ShiftDrop.Core.Ops;
using ShiftDrop.Native;
using ShiftDrop.UI;

namespace ShiftDrop;

/// <summary>
/// The tray app: a hidden window that receives the mouse-hook messages, decides when a drag
/// deserves the wheel, owns the tray icon, and starts jobs.
/// </summary>
public sealed partial class App : Window
{
    public const string MainClass = "ShiftDrop.Main";
    const uint WM_INVOKE = W.WM_APP + 1, WM_TRAY = W.WM_APP + 2;
    const nuint TimerPoll = 1, TimerHide = 2, TimerTrim = 3;
    public const nint CopyFiles = 1, CopyShowTip = 2, CopyQuit = 3;

    static App? _instance;
    public static App? Current => _instance;
    static readonly ConcurrentQueue<Action> Pending = new();

    public Settings Settings { get; private set; }
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
            DestinationFor = input => Settings.SaveTo switch
            {
                "downloads" => Downloads(),
                "custom" when Directory.Exists(Settings.CustomFolder) => Settings.CustomFolder,
                _ => Path.GetDirectoryName(Path.GetFullPath(input.TrimEnd('\\', '/'))) ?? Downloads(),
            },
            Fallback = Downloads,
        };
        Queue.Changed += job => Post(() => OnJobChanged(job));
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
        Theme.Override = settings.Theme;

        Gdip.Start();
        W.OleInitialize(IntPtr.Zero);

        var tools = Path.Combine(appDir, "tools");
        Pdfium.SearchDirs.Add(tools);
        var services = new Services { FFmpeg = FFmpeg.Locate(tools, appDir), ResvgPath = Svg.Locate(tools, appDir) };
        foreach (var sz in new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe") })
            if (File.Exists(sz)) { services.SevenZipPath = sz; break; }

        var app = new App(settings, services);
        _instance = app;
        // awaits in UI code come back to this thread
        SynchronizationContext.SetSynchronizationContext(new UiContext());
        app.Hud = new Hud();
        app.Wheel = new Wheel
        {
            ItemsFor = (files, group) => Catalog.For(files, group, services, app.Settings.Hidden.ToHashSet()),
            Execute = app.Execute,
            Done = () => app._suppress = true,
            SizeFactor = () => app.Settings.WheelScale,
            Hints = () => app.Settings.ShowHints
                ? (app.Settings.OpenKeyInfo.Name, app.Settings.ToolsKeyInfo.Name, app.Settings.ActionsKeyInfo.Name)
                : null,
        };
        AppActions.Register(app);
        app.AddTrayIcon();
        DragMonitor.Start(app.Hwnd);

        // slower checks off the UI thread: the printer for document PDFs, PDFium, the best H.264 encoder
        Task.Run(() =>
        {
            try { if (PrintPdfRenderer.Available()) services.DocRenderer = new PrintPdfRenderer(); } catch (Exception e) { Log.Write("printer: " + e.Message); }
            bool pdf = Pdfium.Available;
            if (Packaged.IsPackaged) Packaged.RefreshStartupAsync().GetAwaiter().GetResult();
            Log.Write("svg: " + (services.ResvgPath ?? "resvg not found"));
            Post(() => app.StartupNotes(pdf));
            if (services.FFmpeg.Available) { Thread.Sleep(3000); Log.Write("h264 encoder: " + services.FFmpeg.H264); }
            // daily update check, a little after start-up so it never slows the login
            Thread.Sleep(20000);
            if (app.Settings.AutoUpdate && Updater.Configured && DateTime.Now - app.Settings.LastUpdateCheck > TimeSpan.FromHours(20))
                Post(async () => await app.CheckForUpdatesAsync(manual: false));
            // start-up is done: hand back the memory it needed
            Post(() => W.SetTimer(app.Hwnd, TimerTrim, 5000, IntPtr.Zero));
        });

        if (startFiles.Count > 0) Post(() => app.Wheel.ShowForFiles(W.Cursor(), startFiles));

        while (W.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (SettingsWindow.Route(ref msg)) continue;
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

    public void ShowTip(double seconds = 10) =>
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
            switch (op.Editor)
            {
                case "crop": _ = CropAsync(op, files[0]); return;
                case "vcrop": _ = VideoCropAsync(op, files[0]); return;
                case "trim": _ = TrimAsync(op, files[0]); return;
                case "channels": _ = ChannelsAsync(op, files[0]); return;
            }
            Dictionary<string, string>? options = null;
            if (op.Fields is { Length: > 0 })
            {
                var subtitle = files.Count == 1 ? Path.GetFileName(files[0]) : L.T($"{files.Count} files", $"{files.Count} ملفات");
                options = InputDialog.Ask(op.Label, subtitle, op.Fields);
                if (options == null) return;
            }
            Start(op, files, options);
        }
        catch (Exception e)
        {
            Log.Write($"execute {op.Id}: {e}");
            Hud.Info(op.Label, e.Message, error: true);
        }
    }

    void Start(Operation op, IReadOnlyList<string> files, Dictionary<string, string>? options)
    {
        var job = Queue.Enqueue(op, files, options);
        if (Settings.ShowCards) { Hud.Track(job); Hud.Update(job); }
        else _quiet.Add(job);
    }

    /// <summary>Opens the crop editor on a preview, then crops the full picture.</summary>
    async Task CropAsync(Operation op, string file)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"shiftdrop-preview-{Guid.NewGuid():N}.png");
        try
        {
            var ff = Services.FFmpeg;
            var source = file;
            if (Svg.IsSvg(file))
            {
                source = Path.ChangeExtension(temp, ".svg.png");
                await Svg.RenderAsync(Services.ResvgPath, file, source, CancellationToken.None);
            }
            var info = await ff.InfoAsync(source, CancellationToken.None);
            var (rgba, pw, ph) = await MediaOps.DecodeRgba(ff, source, temp, CancellationToken.None,
                "scale=w='min(1600,iw)':h='min(1600,ih)':force_original_aspect_ratio=decrease");
            // the picture's upright size: the probe can report it sideways when EXIF rotates it
            int fw = info.Width, fh = info.Height;
            if (fw <= 0 || fh <= 0) (fw, fh) = (pw, ph);
            else if (fw != fh && (pw > ph) != (fw > fh)) (fw, fh) = (fh, fw);
            for (int i = 0; i < rgba.Length; i += 4) (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]); // RGBA → BGRA
            var area = CropEditor.Ask(Path.GetFileName(file), rgba, pw, ph, fw, fh);
            if (area is not var (x, y, w, h)) return;
            Start(op, [file], new() { ["crop"] = Inv($"{x},{y},{w},{h}") });
        }
        catch (Exception e)
        {
            Log.Write("crop: " + e);
            Hud.Info(op.Label, e.Message, error: true);
        }
        finally { try { File.Delete(temp); File.Delete(Path.ChangeExtension(temp, ".svg.png")); } catch { } }
    }

    static string Inv(FormattableString s) => s.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Crop a video: the editor shows frames from anywhere in it.</summary>
    async Task VideoCropAsync(Operation op, string file)
    {
        try
        {
            var ff = Services.FFmpeg;
            var info = await ff.InfoAsync(file, CancellationToken.None);
            var (fw, fh) = await MediaPreview.UprightSize(ff, file, CancellationToken.None);
            if (fw <= 0 || fh <= 0) throw new EngineException(L.T("Could not read this video", "تعذّرت قراءة هذا الفيديو"));
            Task<(byte[], int, int)> FrameAt(double t) => MediaPreview.Frame(ff, file, t, 1280, 720, CancellationToken.None);
            var (first, pw, ph) = await FrameAt(Math.Min(1, info.Duration / 2));
            var area = CropEditor.Ask(Path.GetFileName(file), first, pw, ph, fw, fh, info.Duration, FrameAt);
            if (area is not var (x, y, w, h)) return;
            Start(op, [file], new() { ["crop"] = Inv($"{x},{y},{w},{h}") });
        }
        catch (Exception e)
        {
            Log.Write("video crop: " + e);
            Hud.Info(op.Label, e.Message, error: true);
        }
    }

    /// <summary>Trim audio or video on a timeline.</summary>
    async Task TrimAsync(Operation op, string file)
    {
        try
        {
            var ff = Services.FFmpeg;
            var info = await ff.InfoAsync(file, CancellationToken.None);
            if (info.Duration <= 0) throw new EngineException(L.T("Could not read the length of this file", "تعذّرت قراءة مدة هذا الملف"));
            bool video = info.HasVideo && FileKinds.Of(file) == FileKind.Video;
            var load = new TrimWindow.Loaders(
                buckets => info.HasAudio ? MediaPreview.Peaks(ff, file, buckets, info.Duration, -1, CancellationToken.None) : Task.FromResult(Array.Empty<float>()),
                video ? (t, w, h) => MediaPreview.Frame(ff, file, t, w, h, CancellationToken.None) : null,
                (start, length) => MediaPreview.Wav(ff, file, start, length, null, CancellationToken.None));
            var range = TrimWindow.Ask(Path.GetFileName(file), info.Duration, video, load);
            if (range is not var (a, b)) return;
            Start(op, [file], new() { ["start"] = Inv($"{a:0.###}"), ["end"] = Inv($"{b:0.###}") });
        }
        catch (Exception e)
        {
            Log.Write("trim: " + e);
            Hud.Info(op.Label, e.Message, error: true);
        }
    }

    /// <summary>Mono or stereo with levels for each side.</summary>
    async Task ChannelsAsync(Operation op, string file)
    {
        try
        {
            var ff = Services.FFmpeg;
            var info = await ff.InfoAsync(file, CancellationToken.None);
            if (!info.HasAudio) throw new EngineException(L.T("This file has no sound", "هذا الملف بلا صوت"));
            var load = new ChannelsWindow.Loaders(
                (channel, buckets) => MediaPreview.Peaks(ff, file, buckets, info.Duration, channel, CancellationToken.None),
                filter => MediaPreview.Wav(ff, file, 0, Math.Min(info.Duration, 20), filter, CancellationToken.None));
            var choice = ChannelsWindow.Ask(Path.GetFileName(file), info.Duration, load);
            if (choice is not var (mono, left, right)) return;
            Start(op, [file], new() { ["mode"] = mono ? "mono" : "stereo", ["left"] = Inv($"{left}"), ["right"] = Inv($"{right}") });
        }
        catch (Exception e)
        {
            Log.Write("channels: " + e);
            Hud.Info(op.Label, e.Message, error: true);
        }
    }

    readonly HashSet<Job> _quiet = [];

    void OnJobChanged(Job job)
    {
        Hud.Update(job);
        if (!job.Finished) return;
        // without progress cards, still say when something went wrong
        if (_quiet.Remove(job) && job.State == JobState.Failed)
            Hud.Info(job.Title, job.Error ?? "", error: true, seconds: 15);
        if (job.State == JobState.Done && Settings.RevealWhenDone && job.Outputs.Count > 0)
            Hud.Reveal(job.Outputs[0]);
        // give memory back to Windows a little after the work is done
        W.SetTimer(Hwnd, TimerTrim, 10_000, IntPtr.Zero);
    }

    static void TrimMemory()
    {
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1);
    }

    [System.Runtime.InteropServices.LibraryImport("kernel32.dll")]
    private static partial int SetProcessWorkingSetSize(IntPtr process, nint min, nint max);

    // ---------------- settings ----------------

    /// <summary>Applies changed settings everywhere and saves them.</summary>
    public void ApplySettings()
    {
        L.Arabic = Settings.Arabic;
        Theme.Override = Settings.Theme;
        Settings.Save();
        UpdateTrayIcon();
    }

    public void ReplaceSettings(Settings fresh)
    {
        Settings = fresh;
        ApplySettings();
    }

    public void ShowSettings() => SettingsWindow.Open(this);

    public static void OpenLogFolder()
    {
        Directory.CreateDirectory(Log.Folder!);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.Folder}\"") { UseShellExecute = false });
    }

    public static void OpenText(string path)
    {
        try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = false }); } catch { }
    }

    // ---------------- updates ----------------

    bool _updating;

    /// <summary>Checks GitHub; returns the update (if any) and a sentence for the settings window.</summary>
    public async Task<(UpdateInfo? Info, string Message)> CheckForUpdatesAsync(bool manual)
    {
        try
        {
            var info = await Updater.CheckAsync();
            Settings.LastUpdateCheck = DateTime.Now;
            Settings.Save();
            if (info == null)
            {
                var msg = L.T("You have the latest version", "لديك أحدث إصدار");
                if (manual) Hud.Info(msg, $"ShiftDrop {AppInfo.VersionText}", seconds: 4);
                return (null, msg);
            }
            var v = info.Version.ToString(3);
            if (manual || Settings.SkippedVersion != v)
                Hud.Info(L.T($"ShiftDrop {v} is available", $"الإصدار {v} من ShiftDrop متاح"),
                    L.T("Install it now; ShiftDrop restarts by itself.", "ثبّته الآن، وسيعود ShiftDrop للعمل وحده."),
                    seconds: 30, action: () => InstallUpdate(info), actionText: L.T("Install update", "تثبيت التحديث"));
            return (info, L.T($"Version {v} is available", $"الإصدار {v} متاح"));
        }
        catch (Exception e)
        {
            Log.Write("update check: " + e.Message);
            var msg = e is InvalidOperationException ? e.Message : L.T("Could not check for updates", "تعذّر البحث عن تحديثات");
            if (manual) Hud.Info(L.T("Updates", "التحديثات"), msg, error: e is not InvalidOperationException, seconds: 8);
            return (null, msg);
        }
    }

    /// <summary>Download state for anyone showing it (the settings window, the card in the corner).</summary>
    public (bool Active, long Done, long Total, string Text) Download { get; private set; } = (false, 0, 0, "");
    public event Action? DownloadChanged;

    public static string DownloadText(long done, long total)
    {
        static string Mb(long b) => (b / 1048576.0).ToString(b >= 100 * 1048576 ? "0" : "0.0", System.Globalization.CultureInfo.InvariantCulture);
        if (total <= 0) return L.T($"{Mb(done)} MB downloaded", $"تم تنزيل {Mb(done)} ميغا");
        int pct = (int)Math.Floor(done * 100.0 / total);
        return L.T($"{pct}% · {Mb(done)} of {Mb(total)} MB", $"{pct}% · {Mb(done)} من {Mb(total)} ميغا");
    }

    public async void InstallUpdate(UpdateInfo info)
    {
        if (_updating) return;
        _updating = true;
        var v = info.Version.ToString(3);
        var card = Hud.Progress(L.T($"Downloading ShiftDrop {v}", $"تنزيل ShiftDrop {v}"), L.T("Starting…", "جارٍ البدء…"));
        void Set(bool active, long done, long total, string text)
        {
            Download = (active, done, total, text);
            Hud.SetProgress(card, total > 0 ? (double)done / total : -1, text);
            DownloadChanged?.Invoke();
        }
        try
        {
            Set(true, 0, 0, L.T("Starting…", "جارٍ البدء…"));
            await Updater.InstallAsync(info, (done, total) => Post(() => Set(true, done, total, DownloadText(done, total))));
            if (info.SetupUrl != null)
            {
                Set(true, 1, 1, L.T("Installing… ShiftDrop restarts by itself", "جارٍ التثبيت… سيعود ShiftDrop وحده"));
                W.PostQuitMessage(0); // the installer restarts ShiftDrop
            }
            else Hud.Finish(card);
        }
        catch (Exception e)
        {
            Log.Write("update install: " + e);
            Hud.Finish(card);
            Set(false, 0, 0, L.T("The download failed", "فشل التنزيل"));
            Hud.Info(L.T("Update failed", "فشل التحديث"), e.Message, error: true, seconds: 15);
        }
        finally { _updating = false; }
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
        // "shift/ctrl/alt" here mean the open, tools and actions keys chosen in the settings
        bool shift = W.KeyDown(Settings.OpenKeyInfo.Vk), ctrl = W.KeyDown(Settings.ToolsKeyInfo.Vk), alt = W.KeyDown(Settings.ActionsKeyInfo.Vk);
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

    protected override unsafe IntPtr Proc(uint msg, IntPtr w, IntPtr l)
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
                else if ((nuint)w == TimerTrim) { W.KillTimer(Hwnd, TimerTrim); if (!_down) TrimMemory(); }
                else if ((nuint)w == TimerHide)
                {
                    W.KillTimer(Hwnd, TimerHide);
                    if (!_down && Wheel.Visible && !Wheel.ClickMode) Wheel.Hide();
                }
                return 0;

            case WM_TRAY:
                switch ((uint)l)
                {
                    case W.WM_LBUTTONUP: ShowSettings(); break;
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

    unsafe NOTIFYICONDATAW TrayData()
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
        var menu = W.CreatePopupMenu();
        try
        {
            W.AppendMenuW(menu, dir, 1, L.T("Settings…", "الإعدادات…"));
            W.AppendMenuW(menu, W.MF_SEPARATOR, 0, null);
            W.AppendMenuW(menu, dir, 2, Settings.Paused ? L.T("Turn ShiftDrop back on", "تشغيل ShiftDrop") : L.T("Turn ShiftDrop off for now", "إيقاف ShiftDrop مؤقتًا"));
            W.AppendMenuW(menu, dir, 3, L.T("Check for updates", "البحث عن تحديثات"));
            W.AppendMenuW(menu, dir, 4, L.T("How to use", "طريقة الاستخدام"));
            W.AppendMenuW(menu, dir, 5, L.T("Support the developer ♥", "ادعم المطور ♥"));
            W.AppendMenuW(menu, W.MF_SEPARATOR, 0, null);
            W.AppendMenuW(menu, dir, 99, L.T("Quit", "خروج"));
            SetDefaultItem(menu, 1);

            var p = W.Cursor();
            W.SetForegroundWindow(Hwnd);
            int cmd = W.TrackPopupMenuEx(menu, W.TPM_RETURNCMD | W.TPM_RIGHTBUTTON | W.TPM_BOTTOMALIGN | (rtl ? W.TPM_LAYOUTRTL : 0), p.X, p.Y, Hwnd, IntPtr.Zero);
            W.PostMessageW(Hwnd, W.WM_NULL, 0, 0);
            switch (cmd)
            {
                case 1: ShowSettings(); break;
                case 2: Settings.Paused = !Settings.Paused; ApplySettings(); break;
                case 3: _ = CheckForUpdatesAsync(manual: true); break;
                case 4: ShowTip(15); break;
                case 5: Updater.OpenPage(AppInfo.Donate); break;
                case 99: W.PostQuitMessage(0); break;
            }
        }
        finally { W.DestroyMenu(menu); }
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    private static partial int SetMenuDefaultItem(IntPtr menu, uint item, uint byPosition);
    static void SetDefaultItem(IntPtr menu, uint id) => SetMenuDefaultItem(menu, id, 0);
}

/// <summary>Sends await continuations back to the UI thread through the window message queue.</summary>
sealed class UiContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state) => App.Post(() => d(state));
    public override void Send(SendOrPostCallback d, object? state) => d(state);
    public override SynchronizationContext CreateCopy() => this;
}

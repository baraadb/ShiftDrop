using ShiftDrop.Core;
using ShiftDrop.Native;

namespace ShiftDrop.UI;

/// <summary>
/// The settings window, opened from the tray icon. Native controls, two columns, every change
/// applies and saves at once. In Arabic the whole window mirrors right to left.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    public static SettingsWindow? Current { get; private set; }

    const uint BS_AUTOCHECKBOX = 3, CBS_DROPDOWNLIST = 3, WS_VSCROLL = 0x00200000, SS_NOPREFIX = 0x80,
        SS_PATHELLIPSIS = 0x8000, SS_ENDELLIPSIS = 0x4000, WS_MINIMIZEBOX = 0x00020000;
    const uint WM_CTLCOLORBTN = 0x0135, WM_CTLCOLORSTATIC = 0x0138, WM_SETICON = 0x0080;
    const uint CB_ADDSTRING = 0x0143, CB_GETCURSEL = 0x0147, CB_SETCURSEL = 0x014E, BM_GETCHECK = 0x00F0, BM_SETCHECK = 0x00F1;

    readonly App _app;
    Settings S => _app.Settings;
    float _s = 1;
    IntPtr _font, _bold, _icon, _iconSmall;
    int _nextId = 1000;
    readonly Dictionary<int, Action<IntPtr, int>> _handlers = [];
    readonly HashSet<IntPtr> _muted = [];
    IntPtr _keysNote, _folderLabel, _folderButton, _saveCombo, _updateButton, _updateStatus;
    UpdateInfo? _found;

    public static void Open(App app, POINT? at = null)
    {
        if (Current != null && Current.Hwnd != IntPtr.Zero)
        {
            W.ShowWindow(Current.Hwnd, W.SW_SHOWNORMAL);
            W.SetForegroundWindow(Current.Hwnd);
            return;
        }
        Current = new SettingsWindow(app, at ?? W.Cursor());
    }

    /// <summary>Lets Tab, arrows and Escape work like a dialog.</summary>
    public static bool Route(ref MSG msg) =>
        Current is { Hwnd: var h } && h != IntPtr.Zero && W.IsDialogMessageW(h, ref msg) != 0;

    SettingsWindow(App app, POINT at)
    {
        _app = app;
        _s = W.ScaleAt(at);
        int P(float v) => (int)Math.Round(v * _s);
        bool rtl = L.Arabic;

        Create("ShiftDrop.Settings", rtl ? W.WS_EX_LAYOUTRTL : 0, W.WS_CAPTION | W.WS_SYSMENU | WS_MINIMIZEBOX,
            L.T("ShiftDrop settings", "إعدادات ShiftDrop"), 0, 0, P(760), P(560), background: (IntPtr)(5 + 1) /*COLOR_WINDOW*/);
        _font = W.CreateFontW(-P(14), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        _bold = W.CreateFontW(-P(17), 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI Semibold");
        var icoPath = Path.Combine(AppContext.BaseDirectory, "shiftdrop.ico");
        _icon = W.LoadImageW(IntPtr.Zero, icoPath, W.IMAGE_ICON, P(32), P(32), W.LR_LOADFROMFILE);
        _iconSmall = W.LoadImageW(IntPtr.Zero, icoPath, W.IMAGE_ICON, P(16), P(16), W.LR_LOADFROMFILE);
        W.SendMessageW(Hwnd, WM_SETICON, 1, _icon);
        W.SendMessageW(Hwnd, WM_SETICON, 0, _iconSmall);

        int colW = P(330), gap = P(40), left = P(26), right = left + colW + gap, top = P(18);
        int yL = top, yR = top;

        // ---------------- left column ----------------
        Header(left, ref yL, colW, L.T("General", "عام"));
        Check(left, ref yL, colW, L.T("Start with Windows", "التشغيل مع Windows"), Settings.StartWithWindows,
            v => Settings.StartWithWindows = v);
        Check(left, ref yL, colW, L.T("Turn ShiftDrop off for now", "إيقاف ShiftDrop مؤقتًا"), S.Paused,
            v => { S.Paused = v; Apply(); });
        Combo(left, ref yL, colW, L.T("Language", "اللغة"),
            [L.T("Same as Windows", "مثل Windows"), "العربية", "English"],
            Array.IndexOf(new[] { "auto", "ar", "en" }, S.Language),
            i => { S.Language = new[] { "auto", "ar", "en" }[i]; Apply(); Reopen(); });
        Combo(left, ref yL, colW, L.T("Look", "المظهر"),
            [L.T("Follow Windows", "مثل Windows"), L.T("Light", "فاتح"), L.T("Dark", "داكن")],
            Array.IndexOf(new[] { "system", "light", "dark" }, S.Theme),
            i => { S.Theme = new[] { "system", "light", "dark" }[i]; Apply(); });

        yL += P(10);
        Header(left, ref yL, colW, L.T("The wheel", "العجلة"));
        Combo(left, ref yL, colW, L.T("Open it with", "تُفتح بزر"), ["Shift", "Ctrl", "Alt"],
            Array.IndexOf(new[] { "shift", "ctrl", "alt" }, S.OpenKey),
            i => { S.OpenKey = new[] { "shift", "ctrl", "alt" }[i]; Apply(); UpdateKeysNote(); });
        _keysNote = Note(left, ref yL, colW, "");
        UpdateKeysNote();
        Combo(left, ref yL, colW, L.T("Size", "الحجم"),
            [L.T("Small", "صغير"), L.T("Medium", "متوسط"), L.T("Large", "كبير")],
            Array.IndexOf(new[] { "small", "medium", "large" }, S.WheelSize),
            i => { S.WheelSize = new[] { "small", "medium", "large" }[i]; Apply(); });
        Check(left, ref yL, colW, L.T("Show key hints inside the wheel", "إظهار تلميحات الأزرار داخل العجلة"), S.ShowHints,
            v => { S.ShowHints = v; Apply(); });
        Check(left, ref yL, colW, L.T("Work in all apps, not only File Explorer", "العمل في كل التطبيقات، لا المستكشف فقط"), S.AllApps,
            v => { S.AllApps = v; Apply(); });

        // ---------------- right column ----------------
        Header(right, ref yR, colW, L.T("Results", "النتائج"));
        _saveCombo = Combo(right, ref yR, colW, L.T("Save to", "الحفظ في"),
            [L.T("Next to the original", "بجانب الملف الأصلي"), L.T("Downloads folder", "مجلد التنزيلات"), L.T("A folder I choose…", "مجلد أختاره…")],
            S.SaveTo switch { "downloads" => 1, "custom" => 2, _ => 0 },
            OnSaveTo);
        _folderLabel = Note(right, ref yR, colW - P(100), S.CustomFolder, SS_PATHELLIPSIS);
        _folderButton = Button(right + colW - P(92), yR - P(30), P(92), L.T("Change…", "تغيير…"), () => PickFolder());
        UpdateFolderRow();
        Check(right, ref yR, colW, L.T("Show progress cards", "إظهار بطاقات التقدّم"), S.ShowCards,
            v => { S.ShowCards = v; Apply(); });
        Check(right, ref yR, colW, L.T("Open the folder when a job is done", "فتح المجلد بعد انتهاء العملية"), S.RevealWhenDone,
            v => { S.RevealWhenDone = v; Apply(); });

        yR += P(10);
        Header(right, ref yR, colW, L.T("Updates", "التحديثات"));
        Check(right, ref yR, colW, L.T("Check for updates automatically", "البحث عن تحديثات تلقائيًا"), S.AutoUpdate,
            v => { S.AutoUpdate = v; Apply(); });
        _updateButton = Button(right, yR, P(150), L.T("Check now", "ابحث الآن"), OnUpdateButton);
        _updateStatus = Label(right + P(160), yR + P(6), colW - P(160), P(22),
            L.T($"You have version {AppInfo.VersionText}", $"لديك الإصدار {AppInfo.VersionText}"), muted: true);
        yR += P(40);

        yR += P(10);
        Header(right, ref yR, colW, L.T("About", "حول البرنامج"));
        Label(right, yR, colW, P(22), L.T($"ShiftDrop {AppInfo.VersionText} · free and open source (MIT)",
            $"ShiftDrop {AppInfo.VersionText} · مجاني ومفتوح المصدر (MIT)"));
        yR += P(24);
        Label(right, yR, colW, P(40), L.T("Uses FFmpeg (LGPL), PDFium (BSD) and ONNX Runtime (MIT). Your files never leave this PC.",
            "يستخدم FFmpeg (LGPL) وPDFium (BSD) وONNX Runtime (MIT). ملفاتك لا تغادر جهازك."), muted: true);
        yR += P(44);
        int bw = (colW - P(16)) / 3;
        Button(right, yR, bw, L.T("How to use", "طريقة الاستخدام"), () => _app.ShowTip(15));
        Button(right + bw + P(8), yR, bw, L.T("Log folder", "السجلات"), App.OpenLogFolder);
        Button(right + (bw + P(8)) * 2, yR, bw, L.T("Licences", "التراخيص"), () =>
            App.OpenText(Path.Combine(AppContext.BaseDirectory, "THIRD_PARTY_NOTICES.md")));
        yR += P(40);
        if (AppInfo.Website != null)
            Button(right, yR, bw * 2 + P(8), L.T("Website and source code", "الموقع والكود المصدري"), () => Updater.OpenPage(AppInfo.Website));
        Button(right + (bw + P(8)) * 2, yR, bw, L.T("Support ♥", "ادعم المطور ♥"), () => Updater.OpenPage(AppInfo.Donate));
        yR += P(40);
        Label(right, yR, colW, P(40), L.T("ShiftDrop is free. If it saves you time, you can support its development (optional).",
            "ShiftDrop مجاني. إذا وفّر عليك وقتًا، يمكنك دعم تطويره (اختياري)."), muted: true);
        yR += P(42);

        // ---------------- bottom row ----------------
        int bottom = Math.Max(yL, yR) + P(12);
        Button(left, bottom, P(170), L.T("Reset all settings", "إعادة كل الإعدادات"), ResetAll);
        var close = Button(right + colW - P(110), bottom, P(110), L.T("Close", "إغلاق"), Close, W.IDCANCEL);
        int clientH = bottom + P(32) + P(20), clientW = right + colW + left;

        // size the window so its client area fits, then centre it on the pointer's screen
        W.GetWindowRect(Hwnd, out var wr);
        GetClientRect(Hwnd, out var cr);
        int w = clientW + (wr.Width - cr.Width), h = clientH + (wr.Height - cr.Height);
        var work = W.MonitorAt(at).Work;
        W.SetWindowPos(Hwnd, IntPtr.Zero, work.Left + (work.Width - w) / 2, work.Top + (work.Height - h) / 2, w, h, W.SWP_NOZORDER);
        W.ShowWindow(Hwnd, W.SW_SHOWNORMAL);
        W.SetForegroundWindow(Hwnd);
        W.SetFocus(close);
    }

    // ---------------- building blocks ----------------

    int P(float v) => (int)Math.Round(v * _s);

    IntPtr Add(string cls, string text, uint style, int x, int y, int w, int h, Action<IntPtr, int>? handler = null, int id = 0)
    {
        if (id == 0) id = _nextId++;
        var hwnd = W.CreateWindowExW(0, cls, text, W.WS_CHILD | W.WS_VISIBLE | style, x, y, w, h, Hwnd, id, W.GetModuleHandleW(null), IntPtr.Zero);
        W.SendMessageW(hwnd, W.WM_SETFONT, _font, 1);
        if (handler != null) _handlers[id] = handler;
        return hwnd;
    }

    void Header(int x, ref int y, int w, string text)
    {
        var h = Add("STATIC", text, SS_NOPREFIX, x, y, w, P(26));
        W.SendMessageW(h, W.WM_SETFONT, _bold, 1);
        y += P(34);
    }

    IntPtr Label(int x, int y, int w, int h, string text, bool muted = false, uint extra = 0)
    {
        var hwnd = Add("STATIC", text, SS_NOPREFIX | extra, x, y, w, h);
        if (muted) _muted.Add(hwnd);
        return hwnd;
    }

    IntPtr Note(int x, ref int y, int w, string text, uint extra = SS_ENDELLIPSIS)
    {
        var h = Label(x, y - P(2), w, P(22), text, muted: true, extra);
        y += P(26);
        return h;
    }

    void Check(int x, ref int y, int w, string text, bool value, Action<bool> changed)
    {
        var h = Add("BUTTON", text, BS_AUTOCHECKBOX | W.WS_TABSTOP, x, y, w, P(24),
            (hwnd, code) => { if (code == 0) changed(W.SendMessageW(hwnd, BM_GETCHECK, 0, 0) == 1); });
        W.SendMessageW(h, BM_SETCHECK, value ? 1 : 0, 0);
        y += P(30);
    }

    unsafe IntPtr Combo(int x, ref int y, int w, string label, string[] items, int selected, Action<int> changed)
    {
        int labelW = P(118);
        Label(x, y + P(5), labelW - P(8), P(22), label);
        var h = Add("COMBOBOX", "", CBS_DROPDOWNLIST | WS_VSCROLL | W.WS_TABSTOP, x + labelW, y, w - labelW, P(200),
            (hwnd, code) => { if (code == 1 /*CBN_SELCHANGE*/) changed((int)W.SendMessageW(hwnd, CB_GETCURSEL, 0, 0)); });
        foreach (var item in items)
            fixed (char* p = item) W.SendMessageW(h, CB_ADDSTRING, 0, (IntPtr)p);
        W.SendMessageW(h, CB_SETCURSEL, Math.Max(0, selected), 0);
        y += P(36);
        return h;
    }

    IntPtr Button(int x, int y, int w, string text, Action click, int id = 0) =>
        Add("BUTTON", text, W.BS_PUSHBUTTON | W.WS_TABSTOP, x, y, w, P(30), (_, code) => { if (code == 0) click(); }, id);

    // ---------------- behaviour ----------------

    void Apply() => _app.ApplySettings();

    void UpdateKeysNote()
    {
        var t = S.ToolsKeyInfo.Name; var a = S.ActionsKeyInfo.Name;
        SetText(_keysNote, L.T($"Then {t} for tools and {a} for actions", $"وبعدها {t} للأدوات و{a} للإجراءات"));
    }

    void OnSaveTo(int i)
    {
        if (i == 2 && (string.IsNullOrEmpty(S.CustomFolder) || !Directory.Exists(S.CustomFolder)))
        {
            if (!PickFolder())
            {
                W.SendMessageW(_saveCombo, CB_SETCURSEL, S.SaveTo == "downloads" ? 1 : 0, 0);
                return;
            }
        }
        S.SaveTo = new[] { "beside", "downloads", "custom" }[i];
        Apply();
        UpdateFolderRow();
    }

    bool PickFolder()
    {
        var folder = AppActions.PickFolder(L.T("Save results in", "حفظ النتائج في"));
        if (folder == null) return false;
        S.CustomFolder = folder;
        S.SaveTo = "custom";
        W.SendMessageW(_saveCombo, CB_SETCURSEL, 2, 0);
        Apply();
        UpdateFolderRow();
        return true;
    }

    void UpdateFolderRow()
    {
        bool custom = S.SaveTo == "custom";
        SetText(_folderLabel, custom ? S.CustomFolder : "");
        W.ShowWindow(_folderLabel, custom ? W.SW_SHOW : W.SW_HIDE);
        W.ShowWindow(_folderButton, custom ? W.SW_SHOW : W.SW_HIDE);
    }

    async void OnUpdateButton()
    {
        if (_found != null)
        {
            _app.InstallUpdate(_found, status => SetText(_updateStatus, status));
            return;
        }
        W.EnableWindow(_updateButton, 0);
        SetText(_updateStatus, L.T("Checking…", "جارٍ البحث…"));
        var (info, message) = await _app.CheckForUpdatesAsync(manual: true);
        if (Hwnd == IntPtr.Zero) return; // closed meanwhile
        W.EnableWindow(_updateButton, 1);
        SetText(_updateStatus, message);
        if (info != null)
        {
            _found = info;
            SetText(_updateButton, L.T($"Install {info.Version.ToString(3)}", $"تثبيت {info.Version.ToString(3)}"));
        }
    }

    void ResetAll()
    {
        var keep = (S.Welcomed, S.LastUpdateCheck);
        var fresh = new Settings { Welcomed = keep.Welcomed, LastUpdateCheck = keep.LastUpdateCheck };
        _app.ReplaceSettings(fresh);
        Reopen();
    }

    void Reopen()
    {
        W.GetWindowRect(Hwnd, out var r);
        var centre = new POINT(r.Left + r.Width / 2, r.Top + r.Height / 2);
        var app = _app;
        Close();
        App.Post(() => Open(app, centre));
    }

    void Close() => Destroy();

    static unsafe void SetText(IntPtr hwnd, string text)
    {
        fixed (char* p = text) W.SendMessageW(hwnd, 0x000C /*WM_SETTEXT*/, 0, (IntPtr)p);
    }

    protected override IntPtr Proc(uint msg, IntPtr w, IntPtr l)
    {
        switch (msg)
        {
            case W.WM_COMMAND:
            {
                int id = W.LoWord(w), code = W.HiWord(w);
                if (_handlers.TryGetValue(id, out var h)) { h(l, code); return 0; }
                if (id == W.IDCANCEL) { Close(); return 0; }
                break;
            }
            case WM_CTLCOLORSTATIC:
            case WM_CTLCOLORBTN:
                W.SetBkMode(w, 1);
                W.SetTextColor(w, _muted.Contains(l) ? 0x00706660u /*BGR: slate*/ : 0x00201812u);
                return GetStockObject(0 /*WHITE_BRUSH*/);
            case W.WM_CLOSE:
                Close();
                return 0;
            case W.WM_DESTROY:
                W.DeleteObject(_font);
                W.DeleteObject(_bold);
                if (_icon != IntPtr.Zero) W.DestroyIcon(_icon);
                if (_iconSmall != IntPtr.Zero) W.DestroyIcon(_iconSmall);
                if (Current == this) Current = null;
                break;
        }
        return base.Proc(msg, w, l);
    }

    [System.Runtime.InteropServices.LibraryImport("gdi32.dll")] private static partial IntPtr GetStockObject(int index);
    [System.Runtime.InteropServices.LibraryImport("user32.dll")] private static partial int GetClientRect(IntPtr hwnd, out RECT rect);
}

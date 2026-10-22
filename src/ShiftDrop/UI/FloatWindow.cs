using System.Runtime.InteropServices;
using ShiftDrop.Core;
using ShiftDrop.Native;

namespace ShiftDrop.UI;

/// <summary>
/// "Float": a small card that stays on top of everything and holds files, so they can be dragged
/// into another window later (a mail, a chat, another folder). Drag the card to drop the files; × closes it.
/// </summary>
public sealed unsafe partial class FloatWindow : LayeredWindow
{
    const float CardW = 270, CardH = 70, Radius = 16, Pad = 12, Shadow = 14;
    static readonly List<FloatWindow> Open = [];

    readonly IReadOnlyList<string> _files;
    float _s = 1;
    RectF _close;
    bool _hotClose, _pressed;
    POINT _downAt;

    FloatWindow(IReadOnlyList<string> files)
    {
        _files = files.ToList();
        Create("ShiftDrop.Float", W.WS_EX_LAYERED | W.WS_EX_TOPMOST | W.WS_EX_TOOLWINDOW | W.WS_EX_NOACTIVATE, W.WS_POPUP);
    }

    public static void Show(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return;
        var win = new FloatWindow(files);
        Open.Add(win);
        win.Place(W.Cursor());
    }

    /// <summary>Draws the card to a PNG (ShiftDrop.exe --preview).</summary>
    public static void RenderPreview(IReadOnlyList<string> files, string path, float scale)
    {
        var win = new FloatWindow(files) { _s = scale };
        win.Canvas.Scale = scale;
        win.Canvas.Ensure((int)Math.Ceiling((CardW + Shadow * 2) * scale), (int)Math.Ceiling((CardH + Shadow * 2) * scale));
        win.Render();
        win.Canvas.SavePng(path);
        win.Destroy();
    }

    void Place(POINT at)
    {
        _s = W.ScaleAt(at);
        Canvas.Scale = _s;
        int w = (int)Math.Ceiling((CardW + Shadow * 2) * _s), h = (int)Math.Ceiling((CardH + Shadow * 2) * _s);
        Canvas.Ensure(w, h);
        var work = W.MonitorAt(at).Work;
        // next to the pointer, stacked under any other floating cards
        int x = Math.Clamp(at.X + (int)(16 * _s), work.Left, work.Right - w);
        int y = Math.Clamp(at.Y - h / 2 + (Open.Count - 1) * (int)((CardH + 8) * _s), work.Top, work.Bottom - h);
        Render();
        Present(x, y);
        ShowTopmost(false);
    }

    void Render()
    {
        var c = Canvas;
        var th = Theme.Current;
        bool dark = th == Theme.Dark;
        float s = _s, x = Shadow * s, y = Shadow * s, w = CardW * s, h = CardH * s;
        c.Clear();
        for (int i = 3; i >= 1; i--) c.RoundRect(x - i * 3 * s, y - i * 2 * s + 4 * s, w + i * 6 * s, h + i * 5 * s, (Radius + i * 3) * s, dark ? 0x18000000u : 0x0E0F172Au);
        c.RoundRect(x, y, w, h, Radius * s, dark ? 0xF71C2230u : 0xFAFFFFFFu, dark ? 0x26FFFFFFu : 0x160B1220u, 1);

        // the file's picture
        float ts = 46 * s, tx = x + Pad * s, ty = y + (h - ts) / 2;
        var thumb = Thumbs.Get(_files[0], (int)(ts * 1.5f), () => { if (Hwnd != IntPtr.Zero) { Render(); Present(X, Y); } });
        if (thumb != IntPtr.Zero) c.Image(thumb, tx, ty, ts, ts, 9 * s);
        else
        {
            c.RoundRect(tx, ty, ts, ts, 9 * s, dark ? 0xFF262E3Cu : 0xFFF1F4F8u);
            Icons.Draw(c, Directory.Exists(_files[0]) ? "folder" : "doc", tx + ts / 2, ty + ts / 2, 24 * s, th.HubSub);
        }
        if (_files.Count > 1)
        {
            var more = $"+{_files.Count - 1}";
            float bw = Math.Max(20 * s, c.MeasureWidth(more, 10f * s, true) + 10 * s), bh = 17 * s;
            c.RoundRect(tx + ts - bw / 2, ty - 5 * s, bw, bh, bh / 2, th.Accent, dark ? 0xFF1C2230u : 0xFFFFFFFFu, 2 * s);
            c.Text(more, tx + ts - bw / 2, ty - 5 * s, bw, bh, 10f * s, 0xFFFFFFFF, bold: true, rtl: false);
        }

        // name and hint
        float textX = tx + ts + 12 * s, closeW = 26 * s, textW = x + w - textX - closeW - 10 * s;
        string name = _files.Count == 1 ? Path.GetFileName(_files[0].TrimEnd('\\', '/')) : L.T($"{_files.Count} files", $"{_files.Count} ملفات");
        bool rtlName = Core.Engines.Documents.IsRtl(name);
        c.Text(name, textX, y + 15 * s, textW, 20 * s, 13 * s, th.CardText, bold: true, align: rtlName ? Align.Far : Align.Near, rtl: rtlName);
        c.Text(L.T("Drag me where the files should go", "اسحبني إلى حيث تريد الملفات"), textX, y + 36 * s, textW, 18 * s, 11 * s, th.CardSub,
            align: L.Arabic ? Align.Far : Align.Near);

        // close
        _close = new RectF(x + w - closeW - 8 * s, y + 8 * s, closeW, closeW);
        if (_hotClose) c.FillCircle(_close.X + closeW / 2, _close.Y + closeW / 2, closeW / 2, dark ? 0x22FFFFFFu : 0x120B1220u);
        c.Text("×", _close.X, _close.Y - 1 * s, closeW, closeW, 17 * s, th.CardSub);
    }

    static bool In(RectF r, float x, float y) => x >= r.X && x <= r.X + r.W && y >= r.Y && y <= r.Y + r.H;

    void Close()
    {
        Open.Remove(this);
        Destroy();
    }

    protected override IntPtr Proc(uint msg, IntPtr w, IntPtr l)
    {
        switch (msg)
        {
            case W.WM_SETCURSOR:
            {
                var p = W.Cursor();
                bool hot = In(_close, p.X - X, p.Y - Y);
                if (hot != _hotClose) { _hotClose = hot; Render(); Present(X, Y); }
                W.SetCursor(W.LoadCursorW(IntPtr.Zero, hot ? W.IDC_HAND : 32646 /*SIZEALL*/));
                return 1;
            }
            case W.WM_LBUTTONDOWN:
            {
                float x = W.LoWord(l), y = W.HiWord(l);
                if (In(_close, x, y)) return 0;
                _pressed = true;
                _downAt = W.Cursor();
                SetCapture(Hwnd);
                return 0;
            }
            case W.WM_MOUSEMOVE when _pressed:
            {
                var p = W.Cursor();
                if (Math.Abs(p.X - _downAt.X) > W.GetSystemMetrics(W.SM_CXDRAG) || Math.Abs(p.Y - _downAt.Y) > W.GetSystemMetrics(W.SM_CYDRAG))
                {
                    _pressed = false;
                    ReleaseCapture();
                    if (StartDrag()) Close();
                }
                return 0;
            }
            case W.WM_LBUTTONUP:
            {
                if (_pressed) { _pressed = false; ReleaseCapture(); return 0; }
                if (In(_close, W.LoWord(l), W.HiWord(l))) Close();
                return 0;
            }
            case W.WM_RBUTTONUP:
                Close();
                return 0;
        }
        return base.Proc(msg, w, l);
    }

    /// <summary>Drags the files with the shell's own data object (so Explorer, mail and chat apps all accept them).</summary>
    bool StartDrag()
    {
        var existing = _files.Where(f => File.Exists(f) || Directory.Exists(f)).ToList();
        if (existing.Count == 0)
        {
            App.Current?.Hud.Info(L.T("Float", "عائم"), L.T("These files are no longer there", "هذه الملفات لم تعد موجودة"), error: true);
            return true;
        }
        // the shell data object wants items from one folder
        var folder = Path.GetDirectoryName(existing[0]);
        existing = existing.Where(f => string.Equals(Path.GetDirectoryName(f), folder, StringComparison.OrdinalIgnoreCase)).ToList();

        var pidls = new IntPtr[existing.Count];
        IntPtr array = IntPtr.Zero, data = IntPtr.Zero;
        try
        {
            for (int i = 0; i < existing.Count; i++)
                if (SHParseDisplayName(existing[i], IntPtr.Zero, out pidls[i], 0, out _) != 0) return false;
            fixed (IntPtr* p = pidls)
                if (SHCreateShellItemArrayFromIDLists((uint)pidls.Length, p, out array) != 0) return false;
            var bhid = new Guid("B8C0BD9F-ED24-455C-83E6-D5390C4FE8C4"); // BHID_DataObject
            var iid = new Guid("0000010E-0000-0000-C000-000000000046");  // IID_IDataObject
            var vtbl = *(IntPtr**)array;
            var bind = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, Guid*, IntPtr*, int>)vtbl[3];
            if (bind(array, IntPtr.Zero, &bhid, &iid, &data) != 0 || data == IntPtr.Zero) return false;
            int hr = SHDoDragDrop(Hwnd, data, IntPtr.Zero, 1 | 2 | 4 /*copy, move, link*/, out var effect);
            return hr == 0x00040100 /*DRAGDROP_S_DROP*/ && effect != 0;
        }
        catch (Exception e) { Log.Write("float drag: " + e); return false; }
        finally
        {
            if (data != IntPtr.Zero) Marshal.Release(data);
            if (array != IntPtr.Zero) Marshal.Release(array);
            foreach (var p in pidls) if (p != IntPtr.Zero) W.CoTaskMemFree(p);
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHParseDisplayName(string name, IntPtr bindCtx, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
    [LibraryImport("shell32.dll")] private static partial int SHCreateShellItemArrayFromIDLists(uint count, IntPtr* pidls, out IntPtr array);
    [LibraryImport("shell32.dll")] private static partial int SHDoDragDrop(IntPtr hwnd, IntPtr dataObject, IntPtr dropSource, uint okEffects, out uint effect);
    [LibraryImport("user32.dll")] private static partial IntPtr SetCapture(IntPtr hwnd);
    [LibraryImport("user32.dll")] private static partial int ReleaseCapture();
}

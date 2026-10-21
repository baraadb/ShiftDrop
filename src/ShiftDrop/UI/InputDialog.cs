using ShiftDrop.Core;
using ShiftDrop.Native;

namespace ShiftDrop.UI;

/// <summary>A small modal form for tools that need values (trim times, page ranges...).</summary>
public sealed class InputDialog : Window
{
    readonly InputField[] _fields;
    readonly List<IntPtr> _edits = [];
    readonly List<IntPtr> _controls = [];
    IntPtr _font;
    bool _done, _ok;

    InputDialog(InputField[] fields) => _fields = fields;

    /// <summary>Shows the form near the pointer; returns null when cancelled.</summary>
    public static Dictionary<string, string>? Ask(string title, string subtitle, InputField[] fields)
    {
        var d = new InputDialog(fields);
        return d.Run(title, subtitle);
    }

    Dictionary<string, string>? Run(string title, string subtitle)
    {
        var at = W.Cursor();
        float s = W.ScaleAt(at);
        int P(float v) => (int)Math.Round(v * s);
        bool rtl = L.Arabic;
        int width = P(380), labelH = P(20), editH = P(28), gap = P(10), pad = P(20);
        int height = pad + labelH + gap + _fields.Length * (labelH + editH + gap) + gap + P(32) + pad + P(40); // + caption
        var work = W.MonitorAt(at).Work;
        int x = Math.Clamp(at.X - width / 2, work.Left, work.Right - width);
        int y = Math.Clamp(at.Y - height / 2, work.Top, work.Bottom - height);

        Create("ShiftDrop.Input", W.WS_EX_DLGMODALFRAME | W.WS_EX_TOPMOST | (rtl ? W.WS_EX_LAYOUTRTL : 0),
            W.WS_POPUP | W.WS_CAPTION | W.WS_SYSMENU, $"ShiftDrop · {title}", x, y, width, height, background: (IntPtr)(15 + 1) /*COLOR_3DFACE*/);
        _font = W.CreateFontW(-P(14), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5 /*CLEARTYPE*/, 0, "Segoe UI");

        int cy = pad, inner = width - pad * 2 - P(16);
        Control("STATIC", subtitle, W.SS_LEFT, pad, cy, inner, labelH, 0);
        cy += labelH + gap;
        for (int i = 0; i < _fields.Length; i++)
        {
            Control("STATIC", _fields[i].Label, W.SS_LEFT, pad, cy, inner, labelH, 0);
            cy += labelH;
            var edit = Control("EDIT", _fields[i].Default, W.WS_TABSTOP | W.ES_AUTOHSCROLL, pad, cy, inner, editH, 100 + i);
            _edits.Add(edit);
            cy += editH + gap;
        }
        cy += gap;
        int bw = P(96), bh = P(32);
        Control("BUTTON", L.T("OK", "تنفيذ"), W.WS_TABSTOP | W.BS_DEFPUSHBUTTON, pad + inner - bw * 2 - P(8), cy, bw, bh, W.IDOK);
        Control("BUTTON", L.T("Cancel", "إلغاء"), W.WS_TABSTOP | W.BS_PUSHBUTTON, pad + inner - bw, cy, bw, bh, W.IDCANCEL);

        W.ShowWindow(Hwnd, W.SW_SHOW);
        W.SetForegroundWindow(Hwnd);
        if (_edits.Count > 0)
        {
            W.SetFocus(_edits[0]);
            W.SendMessageW(_edits[0], 0x00B1 /*EM_SETSEL*/, 0, -1);
        }

        // modal loop; IsDialogMessage gives Tab, Enter (OK) and Escape (Cancel)
        while (!_done && W.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (W.IsDialogMessageW(Hwnd, ref msg) != 0) continue;
            W.TranslateMessage(ref msg);
            W.DispatchMessageW(ref msg);
        }

        Dictionary<string, string>? result = null;
        if (_ok)
        {
            result = [];
            for (int i = 0; i < _fields.Length; i++) result[_fields[i].Key] = W.GetText(_edits[i]);
        }
        Destroy();
        W.DeleteObject(_font);
        return result;
    }

    IntPtr Control(string cls, string text, uint style, int x, int y, int w, int h, int id)
    {
        var hwnd = W.CreateWindowExW(cls == "EDIT" ? W.WS_EX_CLIENTEDGE : 0, cls, text, W.WS_CHILD | W.WS_VISIBLE | style,
            x, y, w, h, Hwnd, id, W.GetModuleHandleW(null), IntPtr.Zero);
        W.SendMessageW(hwnd, W.WM_SETFONT, _font, 1);
        _controls.Add(hwnd);
        return hwnd;
    }

    protected override IntPtr Proc(uint msg, IntPtr w, IntPtr l)
    {
        switch (msg)
        {
            case W.WM_COMMAND:
                int id = W.LoWord(w);
                if (id == W.IDOK) { _ok = true; _done = true; return 0; }
                if (id == W.IDCANCEL) { _done = true; return 0; }
                break;
            case W.WM_CLOSE:
                _done = true;
                return 0;
        }
        return base.Proc(msg, w, l);
    }
}

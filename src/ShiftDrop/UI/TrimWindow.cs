using ShiftDrop.Core;
using ShiftDrop.Native;

namespace ShiftDrop.UI;

/// <summary>
/// Trim audio or video by dragging the two ends of a timeline: a waveform (and, for video,
/// a strip of frames and a big picture of the frame at the end you're moving).
/// </summary>
public sealed class TrimWindow : EditorWindow
{
    public sealed record Loaders(
        Func<int, Task<float[]>> Peaks,
        Func<double, int, int, Task<(byte[] Bgra, int W, int H)>>? Frame,
        Func<double, double, Task<byte[]>> Wav);

    readonly string _name;
    readonly double _duration;
    readonly bool _video;
    readonly Loaders? _load;
    double _start, _end;
    int _active; // 0 = start handle, 1 = end handle
    bool _autoSilence;
    float[]? _peaks;
    readonly List<Picture?> _strip = [];
    Picture? _focus;
    double _focusAt = -1, _wantFocus = 0;
    bool _focusBusy;
    bool _moving;
    double _grab;
    bool _loadingWav;
    const double MinLength = 0.1;

    // layout (device pixels), set while painting
    float _tx, _tw, _ty, _th, _stripH;

    TrimWindow(string name, double duration, bool video, Loaders? load)
    {
        _name = name; _duration = Math.Max(duration, 0.1); _video = video; _load = load;
        _start = 0; _end = _duration;
    }

    /// <summary>Opens the editor; returns the part to keep in seconds, or null when cancelled or unchanged.</summary>
    public static (double Start, double End)? Ask(string name, double duration, bool video, Loaders load)
    {
        var w = new TrimWindow(name, duration, video, load);
        bool ok = w.RunModal(video ? L.T("Trim video", "قص الفيديو") : L.T("Trim audio", "قص الصوت"), 760, video ? 600 : 380);
        if (!ok) return null;
        if (w._start < 0.01 && w._end > w._duration - 0.01) return null; // nothing cut
        return (w._start, w._end);
    }

    /// <summary>Draws the editor with made-up data, for design checks.</summary>
    public static void RenderPreview(string path, bool video, float scale, bool dark, bool arabic, float[] peaks, IReadOnlyList<(byte[], int, int)> frames)
    {
        L.Arabic = arabic;
        var w = new TrimWindow("Holiday at the sea." + (video ? "mp4" : "mp3"), 95.4, video, null)
        {
            _peaks = peaks, _start = 12.4, _end = 71.8, _active = 1, _focusAt = 71.8,
        };
        foreach (var f in frames) w._strip.Add(new Picture(f.Item1, f.Item2, f.Item3));
        if (video && frames.Count > 0) w._focus = new Picture(frames[^1].Item1, frames[^1].Item2, frames[^1].Item3);
        w.RenderToPng(path, 760, video ? 600 : 380, scale, dark, arabic);
        foreach (var p in w._strip) p?.Dispose();
        w._focus?.Dispose();
    }

    protected override void Opened()
    {
        _ = LoadAsync();
    }

    protected override void Closing()
    {
        SoundPreview.Stop();
        StopTicking();
        foreach (var p in _strip) p?.Dispose();
        _focus?.Dispose();
    }

    async Task LoadAsync()
    {
        if (_load == null) return;
        try
        {
            int buckets = Math.Max(50, (int)((760 - 48) / 3f));
            _peaks = await _load.Peaks(buckets);
            Invalidate();
            if (!_video || _load.Frame == null) return;
            RequestFocus(0);
            // a strip of small frames along the timeline
            float rowH = 52, slotW = rowH * 16 / 9;
            int n = Math.Clamp((int)Math.Ceiling((760 - 48) / slotW), 4, 14);
            for (int i = 0; i < n; i++) _strip.Add(null);
            int fw = (int)(slotW * S), fh = (int)(rowH * S);
            for (int i = 0; i < n && Hwnd != IntPtr.Zero; i++)
            {
                var (bgra, w, h) = await _load.Frame((i + 0.5) * _duration / n, fw, fh);
                if (Hwnd == IntPtr.Zero) return;
                _strip[i] = new Picture(bgra, w, h);
                Invalidate();
            }
        }
        catch (Exception e) { Log.Write("trim preview: " + e.Message); }
    }

    /// <summary>Shows the frame at a time; while one is loading, only the latest request is kept.</summary>
    void RequestFocus(double t)
    {
        _wantFocus = t;
        if (_focusBusy || _load?.Frame == null) return;
        _ = FocusAsync();
    }

    async Task FocusAsync()
    {
        _focusBusy = true;
        try
        {
            while (Hwnd != IntPtr.Zero && Math.Abs(_wantFocus - _focusAt) > 0.04)
            {
                double t = _wantFocus;
                var (bgra, w, h) = await _load!.Frame!(t, (int)(400 * S), (int)(226 * S));
                if (Hwnd == IntPtr.Zero) return;
                _focus?.Dispose();
                _focus = new Picture(bgra, w, h);
                _focusAt = t;
                Invalidate();
            }
        }
        catch (Exception e) { Log.Write("trim frame: " + e.Message); }
        finally { _focusBusy = false; }
    }

    // ---------------- painting ----------------

    float X(double t) => _tx + (float)(_tw * t / _duration);
    double T(float x) => Math.Clamp((x - _tx) / _tw * _duration, 0, _duration);

    protected override void Paint(Canvas c)
    {
        float s = S, pad = 24 * s;
        Header(c, _video ? L.T("Trim video", "قص الفيديو") : L.T("Trim audio", "قص الصوت"), _name);
        float y = 80 * s;

        if (_video)
        {
            // the frame at the end being moved
            float bh = 230 * s, bw = bh * 16 / 9;
            float bx = (ClientW - bw) / 2;
            c.RoundRect(bx, y, bw, bh, 12 * s, Dark ? 0xFF0B0F16u : 0xFF111827u);
            if (_focus is { Bitmap: not 0 } f)
            {
                double k = Math.Min(bw / f.Width, bh / f.Height);
                float iw = (float)(f.Width * k), ih = (float)(f.Height * k);
                c.Image(f.Bitmap, bx + (bw - iw) / 2, y + (bh - ih) / 2, iw, ih, 10 * s);
            }
            else c.Text(L.T("Loading…", "جارٍ التحميل…"), bx, y, bw, bh, 12 * s, 0xFF94A3B8);
            var stamp = Time(_focusAt < 0 ? 0 : _focusAt);
            float sw = c.MeasureWidth(stamp, 11.5f * s, true) + 16 * s;
            c.RoundRect(bx + 10 * s, y + bh - 32 * s, sw, 22 * s, 11 * s, 0xB0000000);
            c.Text(stamp, bx + 10 * s, y + bh - 32 * s, sw, 22 * s, 11.5f * s, 0xFFFFFFFF, bold: true, rtl: false);
            y += bh + 14 * s;
        }

        // timeline: frames (video) and the waveform
        _tx = pad + 8 * s; _tw = ClientW - 2 * pad - 16 * s; _ty = y;
        _stripH = _video ? 52 * s : 0;
        float waveH = (_video ? 56 : 130) * s;
        _th = _stripH + (_video ? 6 * s : 0) + waveH;
        c.RoundRect(pad, y - 6 * s, ClientW - 2 * pad, _th + 12 * s, 12 * s, Panel, PanelLine, 1);
        if (_video)
        {
            int n = Math.Max(1, _strip.Count);
            float slot = _tw / n;
            for (int i = 0; i < _strip.Count; i++)
            {
                float sx = _tx + i * slot;
                if (_strip[i] is { Bitmap: not 0 } p) c.Image(p.Bitmap, sx, y, slot - 1 * s, _stripH, i == 0 || i == n - 1 ? 6 * s : 0);
                else c.RoundRect(sx, y, slot - 1 * s, _stripH, 4 * s, Soft);
            }
            if (_strip.Count == 0) c.RoundRect(_tx, y, _tw, _stripH, 6 * s, Soft);
        }
        float wy = y + _th - waveH, mid = wy + waveH / 2;
        if (_peaks is { Length: > 0 } peaks)
        {
            float step = _tw / peaks.Length, bar = Math.Max(1 * s, step * 0.62f);
            for (int i = 0; i < peaks.Length; i++)
            {
                float px = _tx + i * step + (step - bar) / 2;
                double t = (i + 0.5) * _duration / peaks.Length;
                bool inside = t >= _start && t <= _end;
                float hgt = Math.Max(2 * s, MathF.Pow(peaks[i], 0.7f) * waveH * 0.92f);
                c.RoundRect(px, mid - hgt / 2, bar, hgt, bar / 2, inside ? Accent : (Dark ? 0x55FFFFFFu : 0x400B1220u));
            }
        }
        else c.Text(_peaks == null ? L.T("Reading the sound…", "جارٍ قراءة الصوت…") : L.T("No sound in this file", "لا صوت في هذا الملف"),
            _tx, wy, _tw, waveH, 12 * s, Sub);

        // the parts that will be cut away are dimmed; the kept part has a blue frame with two handles
        float x0 = X(_start), x1 = X(_end);
        uint shade = Dark ? 0x99161B24u : 0x99F4F6FAu;
        if (x0 > _tx) c.FillRect(_tx, y, x0 - _tx, _th, shade);
        if (x1 < _tx + _tw) c.FillRect(x1, y, _tx + _tw - x1, _th, shade);
        c.RoundRect(x0, y - 3 * s, x1 - x0, _th + 6 * s, 6 * s, 0, Accent, 2.5f * s);
        Draggable(new RectF(x0 + 8 * s, y, Math.Max(0, x1 - x0 - 16 * s), _th), "range", MoveRange, IDC_SIZEALL);
        Handle(c, 0, x0, y);
        Handle(c, 1, x1, y);

        // playhead
        if (SoundPreview.Playing)
        {
            float px = X(Math.Min(_end, _start + SoundPreview.Position));
            c.Line(px, y - 6 * s, px, y + _th + 6 * s, 0xFFFFFFFF, 3 * s);
            c.Line(px, y - 6 * s, px, y + _th + 6 * s, Ink, 1.5f * s);
        }
        y += _th + 16 * s;

        // times
        var start = L.T($"Start  {Time(_start)}", $"البداية  {Time(_start)}");
        var end = L.T($"End  {Time(_end)}", $"النهاية  {Time(_end)}");
        var kept = L.T($"{Time(_end - _start)} kept", $"المدة المختارة {Time(_end - _start)}");
        c.Text(start, pad, y, 220 * s, 20 * s, 12.5f * s, _active == 0 ? Accent : Ink, bold: true, align: Align.Near, rtl: false);
        c.Text(kept, 0, y, ClientW, 20 * s, 12.5f * s, Sub, rtl: L.Arabic);
        c.Text(end, ClientW - pad - 220 * s, y, 220 * s, 20 * s, 12.5f * s, _active == 1 ? Accent : Ink, bold: true, align: Align.Far, rtl: false);
        y += 34 * s;

        // options and preview
        Checkbox(c, Mirror(new RectF(pad, y, 330 * s, 34 * s)), "silence", L.T("Trim silent ends automatically", "قص الصمت من الأطراف تلقائيًا"),
            _autoSilence, v => { _autoSilence = v; ApplySilence(); });
        bool playing = SoundPreview.Playing || _loadingWav;
        Button(c, Mirror(new RectF(ClientW - pad - 190 * s, y, 190 * s, 34 * s)), "play",
            playing ? L.T("Stop", "إيقاف") : L.T("Play selection", "تشغيل المحدد"), false, TogglePlay, playing ? "stop" : "play");

        // footer
        float fy = ClientH - 62 * s;
        c.Line(0, fy, ClientW, fy, PanelLine, 1);
        Button(c, Mirror(new RectF(pad, fy + 14 * s, 96 * s, 36 * s)), "reset", L.T("Reset", "إعادة"), false, Reset);
        Button(c, Mirror(new RectF(ClientW - pad - 220 * s, fy + 14 * s, 104 * s, 36 * s)), "cancel", L.T("Cancel", "إلغاء"), false, () => Close(false));
        Button(c, Mirror(new RectF(ClientW - pad - 108 * s, fy + 14 * s, 108 * s, 36 * s)), "ok", L.T("Trim", "قص"), true, () => Close(true));
    }

    void Handle(Canvas c, int which, float x, float y)
    {
        float s = S, w = 12 * s;
        float hx = which == 0 ? x - w + 2 * s : x - 2 * s;
        bool hot = IsHot($"h{which}") || IsDragging($"h{which}") || _active == which;
        c.RoundRect(hx, y - 3 * s, w, _th + 6 * s, 5 * s, hot ? AccentHot : Accent);
        c.Line(hx + w / 2, y + _th / 2 - 9 * s, hx + w / 2, y + _th / 2 + 9 * s, 0xFFFFFFFF, 2 * s);
        Draggable(new RectF(hx - 6 * s, y - 6 * s, w + 12 * s, _th + 12 * s), $"h{which}", (px, _) =>
        {
            _active = which;
            SetHandle(which, T(px));
        });
    }

    void SetHandle(int which, double t)
    {
        if (which == 0) _start = Math.Clamp(t, 0, _end - MinLength);
        else _end = Math.Clamp(t, _start + MinLength, _duration);
        if (_video) RequestFocus(which == 0 ? _start : _end);
    }

    void MoveRange(float px, float _)
    {
        double t = T(px);
        if (!_moving) { _moving = true; _grab = t - _start; }
        double len = _end - _start;
        _start = Math.Clamp(t - _grab, 0, _duration - len);
        _end = _start + len;
        if (_video) RequestFocus(_active == 0 ? _start : _end);
    }

    protected override void Release() => _moving = false;

    /// <summary>A press on the timeline outside the handles moves the nearest end there.</summary>
    protected override bool Press(float x, float y)
    {
        if (y < _ty - 6 * S || y > _ty + _th + 6 * S || x < _tx - 8 * S || x > _tx + _tw + 8 * S) return false;
        double t = T(x);
        _active = Math.Abs(t - _start) <= Math.Abs(t - _end) ? 0 : 1;
        SetHandle(_active, t);
        return true;
    }

    protected override void Move(float x, float y) => SetHandle(_active, T(x));

    protected override int CursorAt(float x, float y) =>
        y >= _ty - 6 * S && y <= _ty + _th + 6 * S && x >= _tx && x <= _tx + _tw ? IDC_SIZEWE : W.IDC_ARROW;

    protected override bool Key(int vk)
    {
        double step = W.KeyDown(W.VK_SHIFT) ? 1 : 0.1;
        switch (vk)
        {
            case W.VK_LEFT: SetHandle(_active, (_active == 0 ? _start : _end) - step); return true;
            case W.VK_RIGHT: SetHandle(_active, (_active == 0 ? _start : _end) + step); return true;
            case W.VK_TAB: _active = 1 - _active; if (_video) RequestFocus(_active == 0 ? _start : _end); return true;
            case W.VK_SPACE: TogglePlay(); return true;
        }
        return false;
    }

    void ApplySilence()
    {
        if (_autoSilence && _peaks != null)
        {
            var (a, b) = Core.Ops.MediaPreview.SoundBounds(_peaks, _duration);
            if (b - a >= MinLength) { _start = a; _end = b; }
        }
        else if (!_autoSilence) { _start = 0; _end = _duration; }
        if (_video) RequestFocus(_start);
    }

    void Reset()
    {
        _start = 0; _end = _duration; _autoSilence = false; _active = 0;
        if (_video) RequestFocus(0);
    }

    async void TogglePlay()
    {
        if (SoundPreview.Playing || _loadingWav) { SoundPreview.Stop(); StopTicking(); _loadingWav = false; Invalidate(); return; }
        if (_load == null) return;
        _loadingWav = true;
        Invalidate();
        try
        {
            double len = Math.Min(_end - _start, 300);
            var wav = await _load.Wav(_start, len);
            if (Hwnd == IntPtr.Zero || !_loadingWav) return;
            _loadingWav = false;
            SoundPreview.Play(wav, len);
            StartTicking(30);
        }
        catch (Exception e) { _loadingWav = false; Log.Write("trim play: " + e.Message); }
        Invalidate();
    }

    protected override void Tick()
    {
        if (!SoundPreview.Playing) StopTicking();
        Invalidate();
    }
}

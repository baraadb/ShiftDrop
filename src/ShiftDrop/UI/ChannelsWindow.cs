using ShiftDrop.Core;
using ShiftDrop.Core.Ops;
using ShiftDrop.Native;

namespace ShiftDrop.UI;

/// <summary>Mono or stereo, with a level for the left and right sides, and a preview of the result.</summary>
public sealed class ChannelsWindow : EditorWindow
{
    public sealed record Loaders(Func<int, int, Task<float[]>> Peaks, Func<string, Task<byte[]>> Wav);

    readonly string _name;
    readonly double _duration;
    readonly Loaders? _load;
    readonly float[]?[] _peaks = new float[]?[2];
    bool _mono = true;
    readonly int[] _level = [100, 100];
    bool _loadingWav;

    ChannelsWindow(string name, double duration, Loaders? load) { _name = name; _duration = duration; _load = load; }

    /// <summary>Returns (mono, left %, right %), or null when cancelled.</summary>
    public static (bool Mono, int Left, int Right)? Ask(string name, double duration, Loaders load)
    {
        var w = new ChannelsWindow(name, duration, load);
        if (!w.RunModal(L.T("Audio channels", "قنوات الصوت"), 640, 440)) return null;
        return (w._mono, w._level[0], w._level[1]);
    }

    public static void RenderPreview(string path, float[] left, float[] right, float scale, bool dark, bool arabic)
    {
        L.Arabic = arabic;
        var w = new ChannelsWindow("Interview.wav", 64, null) { _mono = false };
        w._peaks[0] = left; w._peaks[1] = right; w._level[1] = 140;
        w.RenderToPng(path, 640, 440, scale, dark, arabic);
    }

    protected override void Opened() => _ = LoadAsync();

    protected override void Closing() => SoundPreview.Stop();

    async Task LoadAsync()
    {
        if (_load == null) return;
        try
        {
            for (int ch = 0; ch < 2 && Hwnd != IntPtr.Zero; ch++)
            {
                _peaks[ch] = await _load.Peaks(ch, 140);
                Invalidate();
            }
        }
        catch (Exception e) { Log.Write("channels preview: " + e.Message); }
    }

    protected override void Paint(Canvas c)
    {
        float s = S, pad = 24 * s;
        Header(c, L.T("Audio channels", "قنوات الصوت"), _name);
        float y = 84 * s;
        c.Text(L.T("Each side of the sound", "كل جهة من الصوت"), pad, y, ClientW - 2 * pad, 18 * s, 12 * s, Sub, align: Rtl ? Align.Far : Align.Near);
        y += 26 * s;

        string[] names = [L.T("Left", "اليسار"), L.T("Right", "اليمين")];
        for (int ch = 0; ch < 2; ch++)
        {
            float rowH = 74 * s;
            c.RoundRect(pad, y, ClientW - 2 * pad, rowH, 12 * s, Panel, PanelLine, 1);
            float labelW = 70 * s, valueW = 54 * s, sliderW = 170 * s;
            var label = Mirror(new RectF(pad + 14 * s, y, labelW, rowH));
            c.Text(names[ch], label.X, label.Y, label.W, label.H, 13 * s, Ink, bold: true, align: Rtl ? Align.Far : Align.Near);
            // waveform at this level
            var wave = Mirror(new RectF(pad + 14 * s + labelW, y + 10 * s, ClientW - 2 * pad - 28 * s - labelW - sliderW - valueW - 24 * s, rowH - 20 * s));
            if (_peaks[ch] is { Length: > 0 } peaks)
            {
                float step = wave.W / peaks.Length, bar = Math.Max(1 * s, step * 0.6f), mid = wave.Y + wave.H / 2;
                float gain = _level[ch] / 100f;
                for (int i = 0; i < peaks.Length; i++)
                {
                    float hgt = Math.Clamp(MathF.Pow(peaks[i] * gain, 0.7f), 0, 1) * wave.H * 0.95f;
                    c.RoundRect(wave.X + i * step, mid - Math.Max(2 * s, hgt) / 2, bar, Math.Max(2 * s, hgt), bar / 2, _level[ch] == 0 ? Track : Accent);
                }
            }
            else c.Text(L.T("Reading…", "جارٍ القراءة…"), wave.X, wave.Y, wave.W, wave.H, 11.5f * s, Sub);
            var slider = Mirror(new RectF(ClientW - pad - 14 * s - valueW - sliderW, y, sliderW, rowH));
            int which = ch;
            Slider(c, slider, $"level{ch}", _level[ch] / 200f, v => _level[which] = (int)Math.Round(v * 40) * 5);
            var value = Mirror(new RectF(ClientW - pad - 14 * s - valueW, y, valueW, rowH));
            c.Text($"{_level[ch]}%", value.X, value.Y, value.W, value.H, 12.5f * s, Ink, bold: true, align: Rtl ? Align.Near : Align.Far, rtl: false);
            y += rowH + 10 * s;
        }

        y += 8 * s;
        Segments(c, Mirror(new RectF(pad, y, 240 * s, 38 * s)), "mode", [L.T("Mono", "أحادي"), L.T("Stereo", "ستيريو")], _mono ? 0 : 1, i => _mono = i == 0);
        bool playing = SoundPreview.Playing || _loadingWav;
        Button(c, Mirror(new RectF(ClientW - pad - 190 * s, y, 190 * s, 38 * s)), "play",
            playing ? L.T("Stop", "إيقاف") : L.T("Play result", "تشغيل النتيجة"), false, TogglePlay, playing ? "stop" : "play");

        float fy = ClientH - 62 * s;
        c.Line(0, fy, ClientW, fy, PanelLine, 1);
        Button(c, Mirror(new RectF(ClientW - pad - 220 * s, fy + 14 * s, 104 * s, 36 * s)), "cancel", L.T("Cancel", "إلغاء"), false, () => Close(false));
        Button(c, Mirror(new RectF(ClientW - pad - 108 * s, fy + 14 * s, 108 * s, 36 * s)), "ok", L.T("Apply", "تطبيق"), true, () => Close(true));
    }

    async void TogglePlay()
    {
        if (SoundPreview.Playing || _loadingWav) { SoundPreview.Stop(); StopTicking(); _loadingWav = false; Invalidate(); return; }
        if (_load == null) return;
        _loadingWav = true;
        Invalidate();
        try
        {
            var wav = await _load.Wav(MediaPreview.ChannelFilter(_mono, _level[0], _level[1]));
            if (Hwnd == IntPtr.Zero || !_loadingWav) return;
            _loadingWav = false;
            SoundPreview.Play(wav, Math.Min(_duration, 20));
            StartTicking(100);
        }
        catch (Exception e) { _loadingWav = false; Log.Write("channels play: " + e.Message); }
        Invalidate();
    }

    protected override void Tick()
    {
        if (!SoundPreview.Playing) { StopTicking(); Invalidate(); }
    }
}

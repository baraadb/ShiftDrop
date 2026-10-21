using System.Diagnostics;
using ShiftDrop.Core;
using ShiftDrop.Core.Engines;
using ShiftDrop.Native;

namespace ShiftDrop.UI;

/// <summary>Small progress cards in the corner of the screen, one per job, plus short tips.</summary>
public sealed class Hud : LayeredWindow
{
    const float CardW = 340, Pad = 14, GapY = 10, Radius = 12;
    const nuint TimerTick = 1, TimerRedraw = 2;

    sealed class Card
    {
        public Job? Job;
        public string Title = "", Body = "";
        public bool Error;
        public DateTime? Until;
        public RectF CloseBox, LinkBox, Bounds;
    }

    readonly List<Card> _cards = [];
    Theme _theme = Theme.Light;
    float _s = 1;
    long _lastRender;
    POINT _anchor;
    bool _redrawPending;
    readonly IntPtr _hand = W.LoadCursorW(IntPtr.Zero, W.IDC_HAND);
    readonly IntPtr _arrow = W.LoadCursorW(IntPtr.Zero, W.IDC_ARROW);

    public Hud()
    {
        Create("ShiftDrop.Hud", W.WS_EX_LAYERED | W.WS_EX_TOPMOST | W.WS_EX_TOOLWINDOW | W.WS_EX_NOACTIVATE, W.WS_POPUP);
    }

    public void Track(Job job)
    {
        _anchor = W.Cursor();
        if (_cards.All(c => c.Job != job)) _cards.Add(new Card { Job = job });
        Refresh(force: true);
    }

    public void Info(string title, string body, bool error = false, double seconds = 8)
    {
        _anchor = W.Cursor();
        _cards.Add(new Card { Title = title, Body = body, Error = error, Until = DateTime.Now.AddSeconds(seconds) });
        Refresh(force: true);
    }

    /// <summary>Called whenever a tracked job changes (progress, finish).</summary>
    public void Update(Job job)
    {
        var card = _cards.FirstOrDefault(c => c.Job == job);
        if (card == null) return;
        if (job.Finished && card.Until == null)
        {
            card.Until = DateTime.Now.AddSeconds(job.State switch { JobState.Done => 7, JobState.Canceled => 3, _ => 20 });
            Refresh(force: true);
            return;
        }
        Refresh(force: false);
    }

    void Refresh(bool force)
    {
        if (!force && Environment.TickCount64 - _lastRender < 40)
        {
            if (!_redrawPending) { _redrawPending = true; W.SetTimer(Hwnd, TimerRedraw, 45, IntPtr.Zero); }
            return;
        }
        Render();
    }

    void Render()
    {
        _redrawPending = false;
        _lastRender = Environment.TickCount64;
        var now = DateTime.Now;
        _cards.RemoveAll(c => c.Until is DateTime u && u < now);
        if (_cards.Count == 0)
        {
            W.KillTimer(Hwnd, TimerTick);
            if (Visible) HideWindow();
            return;
        }
        _theme = Theme.Current;
        _s = W.ScaleAt(_anchor);
        Canvas.Scale = _s;
        float s = _s;
        var heights = _cards.Select(Height).ToList();
        float totalH = heights.Sum() + GapY * (_cards.Count - 1) + Pad * 2;
        int w = (int)Math.Ceiling((CardW + Pad * 2) * s), h = (int)Math.Ceiling(totalH * s);
        Canvas.Ensure(w, h);
        Canvas.Clear();
        bool rtl = L.Arabic;
        float y = Pad;
        bool animate = false;
        for (int i = 0; i < _cards.Count; i++)
        {
            DrawCard(_cards[i], Pad, y, heights[i], rtl);
            if (_cards[i].Job is { State: JobState.Running or JobState.Queued }) animate = true;
            y += heights[i] + GapY;
        }
        var work = W.MonitorAt(_anchor).Work;
        int x = rtl ? work.Left : work.Right - w;
        int top = work.Bottom - h;
        Present(x, top);
        if (!Visible) ShowTopmost(false);
        W.SetTimer(Hwnd, TimerTick, animate ? 33u : 500u, IntPtr.Zero);
    }

    static float Height(Card c) =>
        c.Job == null ? 92 : c.Job.State == JobState.Failed ? 96 : 76;

    void DrawCard(Card card, float x0, float y0, float hgt, bool rtl)
    {
        var c = Canvas; var th = _theme; float s = _s;
        float x = x0 * s, y = y0 * s, w = CardW * s, h = hgt * s;
        card.Bounds = new RectF(x, y, w, h);
        c.RoundRect(x, y + 2 * s, w, h, Radius * s, th.Shadow);
        c.RoundRect(x, y, w, h, Radius * s, th.Card, th.CardLine, 1 * s);

        float inner = 16 * s, closeSize = 22 * s;
        float textX = rtl ? x + inner + closeSize + 6 * s : x + inner;
        float textW = w - inner * 2 - closeSize - 6 * s;
        var align = rtl ? Align.Far : Align.Near;

        // close / cancel button
        float bx = rtl ? x + 10 * s : x + w - 10 * s - closeSize, by = y + 10 * s;
        card.CloseBox = new RectF(bx, by, closeSize, closeSize);
        c.Text("×", bx, by, closeSize, closeSize, 17 * s, th.CardSub);

        var job = card.Job;
        if (job == null)
        {
            c.Text(card.Title, textX, y + 12 * s, textW, 20 * s, 13.5f * s, card.Error ? th.Danger : th.CardText, bold: true, align: align, rtl: Dir(card.Title));
            c.Text(card.Body, x + inner, y + 34 * s, w - inner * 2, h - 42 * s, 12 * s, th.CardSub, align: align, valign: Align.Near, wrap: true, rtl: Dir(card.Body));
            card.LinkBox = default;
            return;
        }

        c.Text(job.Title, textX, y + 12 * s, textW, 20 * s, 13.5f * s, th.CardText, bold: true, align: align);
        c.Text(job.Subtitle, textX, y + 33 * s, textW, 18 * s, 12 * s, th.CardSub, align: align, rtl: Dir(job.Subtitle));
        float barY = y + 58 * s, barX = x + inner, barW = w - inner * 2;
        card.LinkBox = default;
        switch (job.State)
        {
            case JobState.Queued or JobState.Running:
                c.RoundRect(barX, barY, barW, 4 * s, 2 * s, th.Track);
                if (job.Progress >= 0)
                    c.RoundRect(rtl ? barX + barW * (1 - (float)job.Progress) : barX, barY, Math.Max(4 * s, barW * (float)job.Progress), 4 * s, 2 * s, th.Accent);
                else
                {
                    float t = (Environment.TickCount64 % 1200) / 1200f;
                    float seg = barW * 0.28f, pos = (barW + seg) * t - seg;
                    float a = Math.Max(0, pos), b = Math.Min(barW, pos + seg);
                    if (b > a) c.RoundRect(rtl ? barX + barW - b : barX + a, barY, b - a, 4 * s, 2 * s, th.Accent);
                }
                break;
            case JobState.Done:
            {
                var text = job.Outputs.Count > 0 ? L.T("Done · Show in folder", "تم · عرض في المجلد") : L.T("Done", "تم");
                float tw = Math.Min(barW, c.MeasureWidth(text, 12.5f * s, true) + 4 * s);
                float lx = rtl ? barX + barW - tw : barX;
                card.LinkBox = new RectF(lx, barY - 8 * s, tw, 20 * s);
                c.Text(text, lx, barY - 8 * s, tw, 20 * s, 12.5f * s, th.Accent, bold: true, align: align);
                break;
            }
            case JobState.Canceled:
                c.Text(L.T("Cancelled", "أُلغي"), barX, barY - 8 * s, barW, 20 * s, 12.5f * s, th.CardSub, align: align);
                break;
            case JobState.Failed:
                var err = job.Error ?? L.T("Failed", "فشل");
                c.Text(err, barX, barY - 6 * s, barW, 36 * s, 12 * s, th.Danger, align: align, valign: Align.Near, wrap: true, rtl: Dir(err));
                break;
        }
    }

    /// <summary>Arabic text runs right to left; paths, file names and engine errors keep left to right.</summary>
    static bool Dir(string text) => Documents.IsRtl(text);

    // ---------------- input ----------------

    static bool In(RectF r, float x, float y) => r.W > 0 && x >= r.X && x <= r.X + r.W && y >= r.Y && y <= r.Y + r.H;

    Card? CardAt(float x, float y) => _cards.FirstOrDefault(c => In(c.Bounds, x, y));

    protected override IntPtr Proc(uint msg, IntPtr w, IntPtr l)
    {
        switch (msg)
        {
            case W.WM_TIMER:
                if ((nuint)w == TimerRedraw) W.KillTimer(Hwnd, TimerRedraw);
                Render();
                return 0;
            case W.WM_SETCURSOR:
            {
                var p = W.Cursor();
                float x = p.X - X, y = p.Y - Y;
                var card = CardAt(x, y);
                bool hot = card != null && (In(card.CloseBox, x, y) || In(card.LinkBox, x, y));
                W.SetCursor(hot ? _hand : _arrow);
                return 1;
            }
            case W.WM_LBUTTONUP:
            {
                float x = W.LoWord(l), y = W.HiWord(l);
                var card = CardAt(x, y);
                if (card == null) return 0;
                if (In(card.CloseBox, x, y))
                {
                    if (card.Job is { Finished: false } job) job.Cancel();
                    else _cards.Remove(card);
                }
                else if (In(card.LinkBox, x, y) && card.Job is { Outputs.Count: > 0 } done)
                {
                    Reveal(done.Outputs[0]);
                    _cards.Remove(card);
                }
                else if (card.Job == null || card.Job.Finished) _cards.Remove(card);
                Render();
                return 0;
            }
        }
        return base.Proc(msg, w, l);
    }

    public static void Reveal(string path)
    {
        try
        {
            // paths can't contain quotes on Windows, so this quoting is safe
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
        }
        catch (Exception e) { Log.Write("reveal: " + e.Message); }
    }
}

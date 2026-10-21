using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ShiftDrop.Core.Engines;

public sealed record Cue(TimeSpan Start, TimeSpan End, string Text);

public static partial class Subtitles
{
    public static List<Cue> Parse(string text)
    {
        var cues = new List<Cue>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var m = TimeLineRx().Match(lines[i]);
            if (!m.Success) continue;
            var start = ParseTime(m.Groups[1].Value);
            var end = ParseTime(m.Groups[2].Value);
            var sb = new StringBuilder();
            int j = i + 1;
            for (; j < lines.Length && lines[j].Trim().Length > 0; j++)
            {
                if (TimeLineRx().IsMatch(lines[j])) break;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(lines[j].TrimEnd());
            }
            cues.Add(new Cue(start, end, sb.ToString()));
            i = j - 1;
        }
        return cues;
    }

    static TimeSpan ParseTime(string s)
    {
        s = s.Replace(',', '.');
        var parts = s.Split(':');
        double h = 0, m, sec;
        if (parts.Length == 3)
        {
            h = double.Parse(parts[0], CultureInfo.InvariantCulture);
            m = double.Parse(parts[1], CultureInfo.InvariantCulture);
            sec = double.Parse(parts[2], CultureInfo.InvariantCulture);
        }
        else
        {
            m = double.Parse(parts[0], CultureInfo.InvariantCulture);
            sec = double.Parse(parts[1], CultureInfo.InvariantCulture);
        }
        return TimeSpan.FromMilliseconds(Math.Round(((h * 60 + m) * 60 + sec) * 1000));
    }

    static string Fmt(TimeSpan t, char sep)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}{sep}{t.Milliseconds:000}";
    }

    public static string ToSrt(IReadOnlyList<Cue> cues)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < cues.Count; i++)
        {
            sb.Append(i + 1).Append('\n')
              .Append(Fmt(cues[i].Start, ',')).Append(" --> ").Append(Fmt(cues[i].End, ',')).Append('\n')
              .Append(StripVttTags(cues[i].Text)).Append("\n\n");
        }
        return sb.ToString();
    }

    public static string ToVtt(IReadOnlyList<Cue> cues)
    {
        var sb = new StringBuilder("WEBVTT\n\n");
        foreach (var c in cues)
            sb.Append(Fmt(c.Start, '.')).Append(" --> ").Append(Fmt(c.End, '.')).Append('\n').Append(c.Text).Append("\n\n");
        return sb.ToString();
    }

    public static string ToText(IReadOnlyList<Cue> cues) =>
        string.Join("\n", cues.Select(c => StripTags(c.Text).Replace('\n', ' ').Trim()).Where(t => t.Length > 0)) + "\n";

    public static List<Cue> Shift(IReadOnlyList<Cue> cues, TimeSpan by) =>
        cues.Select(c => c with
        {
            Start = Max0(c.Start + by),
            End = Max0(c.End + by),
        }).Where(c => c.End > TimeSpan.Zero).ToList();

    static TimeSpan Max0(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t;

    static string StripVttTags(string s) => VttTagRx().Replace(s, "");
    static string StripTags(string s) => AnyTagRx().Replace(s, "");

    [GeneratedRegex(@"((?:\d{1,3}:)?\d{1,2}:\d{2}[.,]\d{1,3})\s*-->\s*((?:\d{1,3}:)?\d{1,2}:\d{2}[.,]\d{1,3})")]
    private static partial Regex TimeLineRx();
    [GeneratedRegex(@"</?(c|v|lang|ruby|rt)(\.[^ >]*)?[^>]*>|<\d{1,2}:\d{2}[^>]*>")]
    private static partial Regex VttTagRx();
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTagRx();
}

using ShiftDrop.Core;
using ShiftDrop.Native;

namespace ShiftDrop.UI;

/// <summary>
/// ShiftDrop's own line icons, drawn with GDI+ on a 24-unit grid (no icon font needed, same on Windows 10 and 11),
/// and the colour family of every operation.
/// </summary>
public static class Icons
{
    /// <summary>Colour families: what kind of file an operation produces or works on.</summary>
    public static uint Family(Operation op)
    {
        var key = op.TargetExt ?? op.Id;
        if (op.Group == OpGroup.Action) return 0xFF3B82F6; // the app's blue
        return Family(key, op.Id);
    }

    static uint Family(string ext, string id) => ext switch
    {
        "jpg" or "png" or "webp" or "avif" or "tiff" or "bmp" or "ico" or "heic" => Image,
        "mp4" or "mov" or "mkv" or "webm" or "avi" or "wmv" or "gif" => Video,
        "mp3" or "m4a" or "wav" or "flac" or "ogg" or "opus" or "aiff" or "wma" => Audio,
        "pdf" => Pdf,
        "docx" or "md" or "html" or "txt" or "rtf" or "odt" => Doc,
        "xlsx" or "ods" or "csv" or "tsv" => Sheet,
        "pptx" => Slides,
        "epub" => Book,
        "srt" or "vtt" => Subtitle,
        "zip" or "tar" or "tgz" => Archive,
        _ => id.Split('.')[0] switch
        {
            "img" => Image, "vid" or "gif" => Video, "aud" => Audio, "pdf" => Pdf, "doc" => Doc,
            "sheet" => Sheet, "sub" => Subtitle, "arc" => Archive, "book" => Book, _ => 0xFF2563EB,
        },
    };

    public const uint Image = 0xFF0EA5E9, Video = 0xFF8B5CF6, Audio = 0xFFEC4899, Pdf = 0xFFEF4444, Doc = 0xFF2563EB,
        Sheet = 0xFF16A34A, Slides = 0xFFF97316, Book = 0xFF6366F1, Subtitle = 0xFF14B8A6, Archive = 0xFFD97706;

    /// <summary>Icon name for an operation (tools and actions; conversions show their format instead).</summary>
    public static string For(Operation op)
    {
        var id = op.Id;
        int dot = id.IndexOf('.');
        var name = dot >= 0 ? id[(dot + 1)..] : id;
        return name switch
        {
            "crop" => "crop", "compress" => "compress", "half" => "half", "rotate" => "rotate",
            "gray" => "contrast", "strip" => "tag", "onepdf" => "doc", "trim" => "scissors", "mute" => "mute",
            "fast" => "fast", "slow" => "slow", "snapshot" => "camera", "join" or "merge" => "link",
            "split" => "split", "normalize" => "wave", "mono" => "speaker", "extract" => id.StartsWith("arc") ? "box" : "pages",
            "shift" => "clock", "share" => "share", "copypath" => "clipboard", "terminal" => "terminal",
            "copyto" => "copy", "moveto" => "folder", "properties" => "info", "zip" => "zip", "float" => "pin",
            "totable" => "table", "toslides" => "slides", "channels" => "speaker",
            _ => "dot",
        };
    }

    /// <summary>Draws an icon centred at (cx, cy), <paramref name="size"/> pixels wide.</summary>
    public static void Draw(Canvas c, string name, float cx, float cy, float size, uint color)
    {
        float k = size / 24f, ox = cx - size / 2, oy = cy - size / 2, w = 1.8f * k;
        float X(double v) => ox + (float)v * k;
        float Y(double v) => oy + (float)v * k;
        void L(double x1, double y1, double x2, double y2) => c.Line(X(x1), Y(y1), X(x2), Y(y2), color, w);
        void P(params double[] p) { for (int i = 0; i + 3 < p.Length; i += 2) L(p[i], p[i + 1], p[i + 2], p[i + 3]); }
        void R(double x, double y, double rw, double rh, double r) => c.RoundRect(X(x), Y(y), (float)rw * k, (float)rh * k, (float)r * k, 0, color, w);
        void O(double x, double y, double r) => c.StrokeCircle(X(x), Y(y), (float)r * k, color, w);
        void Dot(double x, double y, double r) => c.FillCircle(X(x), Y(y), (float)r * k, color);
        void A(double x, double y, double r, float start, float sweep) => c.Arc(X(x), Y(y), (float)r * k, start, sweep, color, w);

        switch (name)
        {
            case "crop": P(6, 2, 6, 18, 22, 18); P(2, 6, 18, 6, 18, 22); break;
            case "wand": L(4, 20, 14, 10); L(14, 10, 16.5, 12.5); P(17, 3, 17, 7); P(15, 5, 19, 5); P(20.5, 9.5, 20.5, 13.5); P(18.5, 11.5, 22.5, 11.5); P(9, 3, 9, 6); P(7.5, 4.5, 10.5, 4.5); break;
            case "compress": P(3, 3, 9.5, 9.5); P(9.5, 4.5, 9.5, 9.5, 4.5, 9.5); P(21, 21, 14.5, 14.5); P(14.5, 19.5, 14.5, 14.5, 19.5, 14.5); break;
            case "half": R(3, 3, 18, 18, 2.5f); R(3, 11, 10, 10, 2); break;
            case "rotate": A(12, 13, 7.5f, -50, 290); P(16.5, 2.5, 16.8, 7.5, 21.5, 6.5); break;
            case "contrast": O(12, 12, 8.5f); c.FillPie(X(12), Y(12), 8.5f * k, -90, 180, color); break;
            case "tag": P(3, 12, 11.5, 3.5, 20.5, 3.5, 20.5, 12.5, 12, 21, 3, 12); Dot(16, 8, 1.6f); break;
            case "doc": P(6, 2, 14, 2, 19, 7, 19, 22, 6, 22, 6, 2); P(14, 2, 14, 7, 19, 7); L(9, 12.5, 16, 12.5); L(9, 16.5, 16, 16.5); break;
            case "scissors": O(6, 6, 3); O(6, 18, 3); L(8.4, 7.8, 20, 17); L(8.4, 16.2, 20, 7); break;
            case "mute": P(3, 9, 7, 9, 12, 4.5, 12, 19.5, 7, 15, 3, 15, 3, 9); L(16, 9, 21, 15); L(21, 9, 16, 15); break;
            case "speaker": P(3, 9, 7, 9, 12, 4.5, 12, 19.5, 7, 15, 3, 15, 3, 9); A(13, 12, 5, -45, 90); break;
            case "fast": P(3, 6, 11, 12, 3, 18, 3, 6); P(12, 6, 20, 12, 12, 18, 12, 6); break;
            case "slow": P(7, 6, 15, 12, 7, 18, 7, 6); L(18.5, 6, 18.5, 18); break;
            case "camera": R(2.5f, 7, 19, 13.5f, 3); O(12, 13.5f, 3.8f); P(8, 7, 9.5, 4, 14.5, 4, 16, 7); break;
            case "link": R(2, 8, 12, 8, 4); R(10, 8, 12, 8, 4); break;
            case "split": L(12, 3, 12, 21); P(9, 12, 3, 12); P(5.8, 9.2, 3, 12, 5.8, 14.8); P(15, 12, 21, 12); P(18.2, 9.2, 21, 12, 18.2, 14.8); break;
            case "wave": L(4, 10, 4, 14); L(8, 6, 8, 18); L(12, 9, 12, 15); L(16, 4, 16, 20); L(20, 10, 20, 14); break;
            case "pages": R(3, 3, 12, 18, 2); P(11, 12, 21, 12); P(18, 9, 21, 12, 18, 15); break;
            case "clock": O(12, 12, 9); P(12, 7, 12, 12, 15.5, 14); break;
            case "box": P(3, 9, 3, 21, 21, 21, 21, 9); P(2, 9, 22, 9); P(5, 9, 7, 4, 17, 4, 19, 9); P(12, 18, 12, 12); P(9.5, 14.5, 12, 12, 14.5, 14.5); break;
            case "share": O(18, 5, 2.6f); O(6, 12, 2.6f); O(18, 19, 2.6f); L(8.3, 10.8, 15.7, 6.3); L(8.3, 13.2, 15.7, 17.7); break;
            case "clipboard": R(5, 4, 14, 18, 2.5f); R(9, 2, 6, 4, 1.5f); L(8.5, 11, 15.5, 11); L(8.5, 15, 13.5, 15); break;
            case "terminal": R(2, 4, 20, 16, 3); P(6, 9, 9, 12, 6, 15); L(11, 15, 17, 15); break;
            case "copy": R(8, 8, 13, 13, 2.5f); P(16, 5, 16, 3, 3, 3, 3, 16, 5, 16); break;
            case "folder": P(2, 5, 9, 5, 11, 7.5, 22, 7.5, 22, 19.5, 2, 19.5, 2, 5); P(8, 13.5, 16, 13.5); P(13.2, 10.7, 16, 13.5, 13.2, 16.3); break;
            case "info": O(12, 12, 9); L(12, 11, 12, 16.5); Dot(12, 7.6f, 1.2f); break;
            case "zip": R(5, 2, 14, 20, 2.5f); L(11, 5, 13, 5); L(11, 8, 13, 8); L(11, 11, 13, 11); R(10.2f, 13.5f, 3.6f, 4.5f, 1.2f); break;
            case "pin": P(9, 3, 15, 3); P(10, 3, 10, 9, 7, 13, 17, 13, 14, 9, 14, 3); L(12, 13, 12, 21); break;
            case "table": R(3, 4, 18, 16, 2.5f); L(3, 9.5, 21, 9.5); L(3, 14.5, 21, 14.5); L(9.5, 4, 9.5, 20); break;
            case "slides": R(2.5f, 4, 19, 13, 2.5f); L(12, 17, 12, 21); L(8, 21, 16, 21); P(10, 8, 15, 10.5, 10, 13, 10, 8); break;
            default: Dot(12, 12, 3); break;
        }
    }
}

namespace ShiftDrop.Core;

/// <summary>Families of files the wheel knows how to offer formats and tools for.</summary>
public enum FileKind
{
    Unknown,
    Folder,
    Image,
    Gif,
    Video,
    Audio,
    Pdf,
    Text,
    Markdown,
    Docx,
    Subtitle,
    Archive,
    Rtf,
    Html,
    Epub,
    Sheet,
}

public static class FileKinds
{
    static readonly Dictionary<string, FileKind> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jpg"] = FileKind.Image, ["jpeg"] = FileKind.Image, ["jfif"] = FileKind.Image,
        ["png"] = FileKind.Image, ["webp"] = FileKind.Image, ["heic"] = FileKind.Image,
        ["heif"] = FileKind.Image, ["avif"] = FileKind.Image, ["tif"] = FileKind.Image,
        ["tiff"] = FileKind.Image, ["bmp"] = FileKind.Image, ["ico"] = FileKind.Image,
        ["svg"] = FileKind.Image, // input only (FFmpeg with librsvg)
        ["gif"] = FileKind.Gif,
        ["mp4"] = FileKind.Video, ["mov"] = FileKind.Video, ["mkv"] = FileKind.Video,
        ["webm"] = FileKind.Video, ["avi"] = FileKind.Video, ["wmv"] = FileKind.Video,
        ["m4v"] = FileKind.Video, ["flv"] = FileKind.Video, ["mpg"] = FileKind.Video,
        ["mpeg"] = FileKind.Video, ["ts"] = FileKind.Video, ["mts"] = FileKind.Video,
        ["m2ts"] = FileKind.Video, ["3gp"] = FileKind.Video,
        ["mp3"] = FileKind.Audio, ["m4a"] = FileKind.Audio, ["wav"] = FileKind.Audio,
        ["flac"] = FileKind.Audio, ["ogg"] = FileKind.Audio, ["oga"] = FileKind.Audio,
        ["opus"] = FileKind.Audio, ["aiff"] = FileKind.Audio, ["aif"] = FileKind.Audio,
        ["wma"] = FileKind.Audio, ["aac"] = FileKind.Audio,
        ["pdf"] = FileKind.Pdf,
        ["txt"] = FileKind.Text,
        ["md"] = FileKind.Markdown, ["markdown"] = FileKind.Markdown,
        ["docx"] = FileKind.Docx,
        ["rtf"] = FileKind.Rtf, ["html"] = FileKind.Html, ["htm"] = FileKind.Html, ["epub"] = FileKind.Epub,
        ["xlsx"] = FileKind.Sheet, ["xlsm"] = FileKind.Sheet, ["ods"] = FileKind.Sheet, ["csv"] = FileKind.Sheet, ["tsv"] = FileKind.Sheet,
        ["srt"] = FileKind.Subtitle, ["vtt"] = FileKind.Subtitle,
        ["zip"] = FileKind.Archive, ["tar"] = FileKind.Archive, ["tgz"] = FileKind.Archive,
        ["gz"] = FileKind.Archive, ["7z"] = FileKind.Archive, ["rar"] = FileKind.Archive,
    };

    /// <summary>Lower-case extension without the dot; ".tar.gz" counts as "tgz".</summary>
    public static string Ext(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        if (name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) return "tgz";
        return Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
    }

    /// <summary>File name without its extension (".tar.gz" removed as one).</summary>
    public static string BaseName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        if (name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) return name[..^7];
        var b = Path.GetFileNameWithoutExtension(name);
        return string.IsNullOrEmpty(b) ? name : b;
    }

    public static FileKind Of(string path)
    {
        if (Directory.Exists(path)) return FileKind.Folder;
        return Map.GetValueOrDefault(Ext(path), FileKind.Unknown);
    }

    public static bool IsJpeg(string path) => Ext(path) is "jpg" or "jpeg" or "jfif";
}

/// <summary>Bilingual text. The UI flips the whole app with <see cref="Arabic"/>.</summary>
public static class L
{
    public static bool Arabic { get; set; }
    public static string T(string en, string ar) => Arabic ? ar : en;
}

public sealed class EngineException(string message) : Exception(message);

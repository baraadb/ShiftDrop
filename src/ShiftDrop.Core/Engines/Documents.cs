using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ShiftDrop.Core.Engines;

public enum BlockKind { Heading1, Heading2, Heading3, Paragraph, Bullet, Numbered, Code, PageBreak }

/// <summary>One paragraph-level piece of a document. Inline Markdown marks are kept in <see cref="Text"/>.</summary>
public sealed record Block(BlockKind Kind, string Text, int Number = 0)
{
    public bool Rtl => Documents.IsRtl(Text);
    public string Plain => Kind == BlockKind.Code ? Text : Documents.StripInline(Text);
}

/// <summary>Draws a document to PDF. On Windows this prints through "Microsoft Print to PDF" so text stays real text.</summary>
public interface IDocRenderer
{
    void RenderPdf(IReadOnlyList<Block> blocks, string title, string outputPath, CancellationToken ct);
}

public static partial class Documents
{
    static Documents() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static bool IsRtl(string s)
    {
        foreach (var ch in s)
        {
            if ((ch >= 0x0590 && ch <= 0x08FF) || (ch >= 0xFB1D && ch <= 0xFDFF) || (ch >= 0xFE70 && ch <= 0xFEFF)) return true;
            if (char.IsLetter(ch)) return false;
        }
        return false;
    }

    /// <summary>Reads text: BOM first, then strict UTF-8, then the system's legacy code page (1256 for Arabic Windows).</summary>
    public static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { }
        // Legacy Arabic files are common even on English Windows: prefer 1256 when it reads as Arabic.
        try
        {
            var arabic = Encoding.GetEncoding(1256).GetString(bytes);
            int high = 0, letters = 0;
            foreach (var ch in arabic)
            {
                if (ch < 0x80) continue;
                high++;
                if (ch >= 0x0600 && ch <= 0x06FF) letters++;
            }
            if (high > 0 && letters >= high * 0.6) return arabic;
        }
        catch { }
        int cp = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
        if (cp <= 0 || cp == 65001) cp = 1252;
        try { return Encoding.GetEncoding(cp).GetString(bytes); }
        catch { return Encoding.Latin1.GetString(bytes); }
    }

    public static void WriteText(string path, string text) =>
        File.WriteAllText(path, text, new UTF8Encoding(false));

    // ---------------- readers ----------------

    public static List<Block> FromText(string text)
    {
        var list = new List<Block>();
        foreach (var line in Lines(text)) list.Add(new Block(BlockKind.Paragraph, EscapeMd(line)));
        return list;
    }

    static IEnumerable<string> Lines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n');

    /// <summary>Plain text must not be read as Markdown later; escape the marks we interpret.</summary>
    static string EscapeMd(string s) => s.Replace("\\", "\\\\").Replace("*", "\\*").Replace("_", "\\_").Replace("`", "\\`").Replace("[", "\\[");

    public static List<Block> FromMarkdown(string md)
    {
        var list = new List<Block>();
        var para = new StringBuilder();
        bool inCode = false;
        var code = new StringBuilder();
        void Flush()
        {
            if (para.Length > 0) { list.Add(new Block(BlockKind.Paragraph, para.ToString())); para.Clear(); }
        }
        foreach (var raw in Lines(md))
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```"))
            {
                if (inCode) { list.Add(new Block(BlockKind.Code, code.ToString().TrimEnd('\n'))); code.Clear(); inCode = false; }
                else { Flush(); inCode = true; }
                continue;
            }
            if (inCode) { code.Append(raw).Append('\n'); continue; }
            if (line.Length == 0) { Flush(); continue; }
            var t = line.TrimStart();
            var h = HeadingRx().Match(t);
            if (h.Success)
            {
                Flush();
                int level = h.Groups[1].Value.Length;
                var kind = level <= 1 ? BlockKind.Heading1 : level == 2 ? BlockKind.Heading2 : BlockKind.Heading3;
                list.Add(new Block(kind, h.Groups[2].Value.Trim().TrimEnd('#').Trim()));
                continue;
            }
            var b = BulletRx().Match(t);
            if (b.Success) { Flush(); list.Add(new Block(BlockKind.Bullet, b.Groups[1].Value)); continue; }
            var n = NumberRx().Match(t);
            if (n.Success) { Flush(); list.Add(new Block(BlockKind.Numbered, n.Groups[2].Value, int.Parse(n.Groups[1].Value, CultureInfo.InvariantCulture))); continue; }
            if (t is "---" or "***" or "___") { Flush(); continue; }
            if (para.Length > 0) para.Append(' ');
            para.Append(t);
        }
        if (inCode) list.Add(new Block(BlockKind.Code, code.ToString().TrimEnd('\n')));
        Flush();
        return list;
    }

    public static List<Block> FromDocx(string path)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml") ?? throw new EngineException(L.T("This is not a Word document", "هذا ليس ملف Word"));
        XDocument doc;
        using (var s = entry.Open())
            doc = XDocument.Load(XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }));
        var list = new List<Block>();
        var body = doc.Root?.Element(w + "body");
        if (body == null) return list;
        foreach (var el in body.Elements())
        {
            if (el.Name == w + "tbl")
            {
                foreach (var row in el.Descendants(w + "tr"))
                {
                    var cells = row.Elements(w + "tc").Select(c => string.Join(" ", c.Descendants(w + "p").Select(p => ParaText(p, w))).Trim());
                    list.Add(new Block(BlockKind.Paragraph, EscapeMd(string.Join("  |  ", cells))));
                }
                continue;
            }
            if (el.Name != w + "p") continue;
            var text = ParaText(el, w);
            var style = el.Element(w + "pPr")?.Element(w + "pStyle")?.Attribute(w + "val")?.Value ?? "";
            bool list_ = el.Element(w + "pPr")?.Element(w + "numPr") != null;
            bool pageBreak = el.Descendants(w + "br").Any(b => (string?)b.Attribute(w + "type") == "page");
            var kind = style switch
            {
                _ when style.StartsWith("Title", StringComparison.OrdinalIgnoreCase) => BlockKind.Heading1,
                _ when Regex.IsMatch(style, "^(Heading|heading)\\s*1$") => BlockKind.Heading1,
                _ when Regex.IsMatch(style, "^(Heading|heading)\\s*2$") => BlockKind.Heading2,
                _ when Regex.IsMatch(style, "^(Heading|heading)\\s*[3-9]$") => BlockKind.Heading3,
                _ when list_ || style.StartsWith("List", StringComparison.OrdinalIgnoreCase) => BlockKind.Bullet,
                _ => BlockKind.Paragraph,
            };
            list.Add(new Block(kind, EscapeMd(text)));
            if (pageBreak) list.Add(new Block(BlockKind.PageBreak, ""));
        }
        return list;
    }

    static string ParaText(XElement p, XNamespace w)
    {
        var sb = new StringBuilder();
        foreach (var n in p.Descendants())
        {
            if (n.Name == w + "t") sb.Append(n.Value);
            else if (n.Name == w + "tab") sb.Append('\t');
            else if (n.Name == w + "br" && (string?)n.Attribute(w + "type") != "page") sb.Append(' ');
        }
        return sb.ToString();
    }

    public static List<Block> FromPdfPages(IReadOnlyList<string> pages)
    {
        var list = new List<Block>();
        for (int i = 0; i < pages.Count; i++)
        {
            if (i > 0) list.Add(new Block(BlockKind.PageBreak, ""));
            foreach (var line in Lines(pages[i]))
                list.Add(new Block(BlockKind.Paragraph, EscapeMd(line.TrimEnd())));
        }
        return list;
    }

    // ---------------- writers ----------------

    public static string ToText(IReadOnlyList<Block> blocks)
    {
        var sb = new StringBuilder();
        foreach (var b in blocks)
        {
            switch (b.Kind)
            {
                case BlockKind.PageBreak: sb.Append('\n'); break;
                case BlockKind.Bullet: sb.Append("• ").Append(b.Plain).Append('\n'); break;
                case BlockKind.Numbered: sb.Append(b.Number).Append(". ").Append(b.Plain).Append('\n'); break;
                case BlockKind.Heading1 or BlockKind.Heading2 or BlockKind.Heading3:
                    if (sb.Length > 0 && !sb.ToString().EndsWith("\n\n")) sb.Append('\n');
                    sb.Append(b.Plain).Append("\n\n"); break;
                default: sb.Append(b.Plain).Append('\n'); break;
            }
        }
        return sb.ToString();
    }

    public static string ToMarkdown(IReadOnlyList<Block> blocks)
    {
        var sb = new StringBuilder();
        BlockKind? prev = null;
        foreach (var b in blocks)
        {
            bool listItem = b.Kind is BlockKind.Bullet or BlockKind.Numbered;
            bool prevList = prev is BlockKind.Bullet or BlockKind.Numbered;
            if (sb.Length > 0 && !(listItem && prevList)) sb.Append('\n');
            switch (b.Kind)
            {
                case BlockKind.Heading1: sb.Append("# ").Append(b.Text).Append('\n'); break;
                case BlockKind.Heading2: sb.Append("## ").Append(b.Text).Append('\n'); break;
                case BlockKind.Heading3: sb.Append("### ").Append(b.Text).Append('\n'); break;
                case BlockKind.Bullet: sb.Append("- ").Append(b.Text).Append('\n'); break;
                case BlockKind.Numbered: sb.Append(b.Number).Append(". ").Append(b.Text).Append('\n'); break;
                case BlockKind.Code: sb.Append("```\n").Append(b.Text).Append("\n```\n"); break;
                case BlockKind.PageBreak: sb.Append("---\n"); break;
                default: sb.Append(b.Text).Append('\n'); break;
            }
            prev = b.Kind;
        }
        return sb.ToString();
    }

    public static string ToHtml(IReadOnlyList<Block> blocks, string title)
    {
        bool rtlDoc = blocks.Count(b => b.Rtl) > blocks.Count(b => !b.Rtl && b.Text.Length > 0);
        var sb = new StringBuilder();
        sb.Append("<!doctype html>\n<html lang=\"").Append(rtlDoc ? "ar" : "en").Append("\" dir=\"").Append(rtlDoc ? "rtl" : "ltr").Append("\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n<title>")
          .Append(WebUtility.HtmlEncode(title)).Append("</title>\n<style>\nbody{max-width:760px;margin:40px auto;padding:0 20px;font-family:\"Segoe UI\",Tahoma,Arial,sans-serif;line-height:1.6;color:#1f2933}\npre{background:#f4f5f7;padding:12px;overflow:auto;direction:ltr;text-align:left}\ncode{font-family:Consolas,monospace}\n</style>\n</head>\n<body>\n");
        string? openList = null;
        void CloseList() { if (openList != null) { sb.Append("</").Append(openList).Append(">\n"); openList = null; } }
        foreach (var b in blocks)
        {
            string dir = b.Text.Length == 0 ? "" : b.Rtl ? " dir=\"rtl\"" : " dir=\"ltr\"";
            if (b.Kind is BlockKind.Bullet or BlockKind.Numbered)
            {
                var tag = b.Kind == BlockKind.Bullet ? "ul" : "ol";
                if (openList != tag) { CloseList(); sb.Append('<').Append(tag).Append(">\n"); openList = tag; }
                sb.Append("<li").Append(dir).Append('>').Append(InlineHtml(b.Text)).Append("</li>\n");
                continue;
            }
            CloseList();
            switch (b.Kind)
            {
                case BlockKind.Heading1: sb.Append("<h1").Append(dir).Append('>').Append(InlineHtml(b.Text)).Append("</h1>\n"); break;
                case BlockKind.Heading2: sb.Append("<h2").Append(dir).Append('>').Append(InlineHtml(b.Text)).Append("</h2>\n"); break;
                case BlockKind.Heading3: sb.Append("<h3").Append(dir).Append('>').Append(InlineHtml(b.Text)).Append("</h3>\n"); break;
                case BlockKind.Code: sb.Append("<pre><code>").Append(WebUtility.HtmlEncode(b.Text)).Append("</code></pre>\n"); break;
                case BlockKind.PageBreak: sb.Append("<hr>\n"); break;
                default:
                    if (b.Text.Length == 0) sb.Append("<p>&nbsp;</p>\n");
                    else sb.Append("<p").Append(dir).Append('>').Append(InlineHtml(b.Text)).Append("</p>\n");
                    break;
            }
        }
        CloseList();
        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    public static void ToDocx(IReadOnlyList<Block> blocks, string path)
    {
        const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using var fs = new FileStream(path, FileMode.CreateNew);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        void Put(string name, string xml)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var s = new StreamWriter(e.Open(), new UTF8Encoding(false));
            s.Write(xml);
        }
        Put("[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/><Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/></Types>");
        Put("_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
        Put("word/_rels/document.xml.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
        Put("word/styles.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:styles xmlns:w=\"{W}\">" +
            "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:cs=\"Arial\"/><w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after=\"120\" w:line=\"276\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
            Heading(1, 36) + Heading(2, 30) + Heading(3, 26) +
            "<w:style w:type=\"paragraph\" w:styleId=\"Code\"><w:name w:val=\"Code\"/><w:basedOn w:val=\"Normal\"/><w:rPr><w:rFonts w:ascii=\"Consolas\" w:hAnsi=\"Consolas\"/><w:sz w:val=\"19\"/></w:rPr></w:style>" +
            "</w:styles>");
        var body = new StringBuilder();
        foreach (var b in blocks)
        {
            if (b.Kind == BlockKind.PageBreak) { body.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>"); continue; }
            var lines = b.Kind == BlockKind.Code ? b.Text.Split('\n') : [b.Plain];
            foreach (var line in lines)
            {
                bool rtl = IsRtl(line);
                var style = b.Kind switch
                {
                    BlockKind.Heading1 => "Heading1",
                    BlockKind.Heading2 => "Heading2",
                    BlockKind.Heading3 => "Heading3",
                    BlockKind.Code => "Code",
                    _ => null,
                };
                var text = b.Kind switch
                {
                    BlockKind.Bullet => "• " + line,
                    BlockKind.Numbered => b.Number + ". " + line,
                    _ => line,
                };
                body.Append("<w:p><w:pPr>");
                if (style != null) body.Append("<w:pStyle w:val=\"").Append(style).Append("\"/>");
                if (rtl) body.Append("<w:bidi/>");
                if (b.Kind is BlockKind.Bullet or BlockKind.Numbered) body.Append("<w:ind w:start=\"360\"/>");
                body.Append("</w:pPr><w:r>");
                if (rtl) body.Append("<w:rPr><w:rtl/></w:rPr>");
                body.Append("<w:t xml:space=\"preserve\">").Append(XmlEscape(text)).Append("</w:t></w:r></w:p>");
            }
        }
        Put("word/document.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<w:document xmlns:w=\"{W}\"><w:body>{body}<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1134\" w:right=\"1134\" w:bottom=\"1134\" w:left=\"1134\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/></w:sectPr></w:body></w:document>");
    }

    static string Heading(int level, int halfPoints) =>
        $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{level}\"><w:name w:val=\"heading {level}\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before=\"240\" w:after=\"120\"/><w:outlineLvl w:val=\"{level - 1}\"/></w:pPr><w:rPr><w:b/><w:bCs/><w:sz w:val=\"{halfPoints}\"/><w:szCs w:val=\"{halfPoints}\"/></w:rPr></w:style>";

    static string XmlEscape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == '&') sb.Append("&amp;");
            else if (c == '<') sb.Append("&lt;");
            else if (c == '>') sb.Append("&gt;");
            else if (c == '"') sb.Append("&quot;");
            else if (c < 0x20 && c != '\t' && c != '\n' && c != '\r') continue; // not allowed in XML
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // ---------------- inline marks ----------------

    /// <summary>Removes Markdown inline marks (bold, italic, code, links) and backslash escapes.</summary>
    public static string StripInline(string s)
    {
        s = LinkRx().Replace(s, "$1");
        s = BoldRx().Replace(s, "$2");
        s = ItalicRx().Replace(s, "$2");
        s = CodeRx().Replace(s, "$1");
        return EscRx().Replace(s, "$1");
    }

    static string InlineHtml(string s)
    {
        // protect escaped characters, encode, then apply marks
        var protectedChars = new List<string>();
        s = EscRx().Replace(s, m => { protectedChars.Add(m.Groups[1].Value); return "\u0001" + (protectedChars.Count - 1) + "\u0002"; });
        s = WebUtility.HtmlEncode(s);
        s = CodeRx().Replace(s, "<code>$1</code>");
        s = LinkRx().Replace(s, m => $"<a href=\"{SafeUrl(m.Groups[2].Value)}\">{m.Groups[1].Value}</a>");
        s = BoldRx().Replace(s, "<strong>$2</strong>");
        s = ItalicRx().Replace(s, "<em>$2</em>");
        s = Regex.Replace(s, "\u0001(\\d+)\u0002", m => WebUtility.HtmlEncode(protectedChars[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)]));
        return s;
    }

    static string SafeUrl(string u)
    {
        var d = WebUtility.HtmlDecode(u).Trim();
        if (d.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || d.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            d.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || d.StartsWith('#') || d.StartsWith('/'))
            return WebUtility.HtmlEncode(d);
        return "#";
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")] private static partial Regex HeadingRx();
    [GeneratedRegex(@"^[-*+]\s+(.*)$")] private static partial Regex BulletRx();
    [GeneratedRegex(@"^(\d{1,4})[.)]\s+(.*)$")] private static partial Regex NumberRx();
    [GeneratedRegex(@"(?<!\\)\[([^\]]+)\]\(([^)\s]+)(?:\s+""[^""]*"")?\)")] private static partial Regex LinkRx();
    [GeneratedRegex(@"(?<!\\)(\*\*|__)(?=\S)(.+?)(?<=[^\s\\])\1")] private static partial Regex BoldRx();
    [GeneratedRegex(@"(?<![\w*\\])(\*|_)(?=\S)(.+?)(?<=[^\s\\])\1(?![\w*])")] private static partial Regex ItalicRx();
    [GeneratedRegex(@"(?<!\\)`([^`]+)`")] private static partial Regex CodeRx();
    [GeneratedRegex(@"\\([\\`*_\[\]#+\-.!()>])")] private static partial Regex EscRx();
}

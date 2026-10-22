using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ShiftDrop.Core.Engines;

/// <summary>RTF and HTML in, EPUB in and out.</summary>
public static partial class Documents
{
    // ---------------- RTF ----------------

    /// <summary>Reads the text of an RTF file: paragraphs, tabs, Unicode and code-page characters. Formatting is dropped.</summary>
    public static List<Block> FromRtf(string path)
    {
        var raw = File.ReadAllBytes(path);
        var src = Encoding.Latin1.GetString(raw); // RTF itself is 7-bit; keep bytes as chars
        if (!src.TrimStart().StartsWith("{\\rtf")) throw new EngineException(L.T("This is not an RTF document", "هذا ليس ملف RTF"));
        var text = new StringBuilder();
        var bytes = new List<byte>();
        var codePage = 1252;
        int uc = 1, skip = 0;
        // group stack: whether the group's text is ignored (font table, pictures, ...)
        var ignore = new Stack<bool>();
        bool ignoring = false;
        int i = 0;

        void FlushBytes()
        {
            if (bytes.Count == 0) return;
            try { text.Append(Encoding.GetEncoding(codePage).GetString(bytes.ToArray())); }
            catch { text.Append(Encoding.Latin1.GetString(bytes.ToArray())); }
            bytes.Clear();
        }
        void Put(string s) { if (!ignoring) { FlushBytes(); text.Append(s); } }

        while (i < src.Length)
        {
            char ch = src[i];
            if (ch == '{') { FlushBytes(); ignore.Push(ignoring); i++; continue; }
            if (ch == '}') { FlushBytes(); ignoring = ignore.Count > 0 && ignore.Pop(); i++; continue; }
            if (ch is '\r' or '\n') { i++; continue; }
            if (ch != '\\')
            {
                if (skip > 0) skip--;
                else if (!ignoring) { FlushBytes(); text.Append(ch); }
                i++;
                continue;
            }
            // control symbol or word
            i++;
            if (i >= src.Length) break;
            char c = src[i];
            if (c == '\'')
            {
                if (i + 2 < src.Length && byte.TryParse(src.AsSpan(i + 1, 2), NumberStyles.HexNumber, null, out var b))
                {
                    if (skip > 0) skip--;
                    else if (!ignoring) bytes.Add(b);
                }
                i += 3;
                continue;
            }
            if (c is '\\' or '{' or '}') { if (skip > 0) skip--; else Put(c.ToString()); i++; continue; }
            if (c == '~') { Put(" "); i++; continue; }
            if (c == '-' || c == '_') { if (c == '_') Put("-"); i++; continue; }
            if (c == '*') { ignoring = true; i++; continue; }
            if (!char.IsLetter(c)) { i++; continue; }
            int start = i;
            while (i < src.Length && char.IsLetter(src[i])) i++;
            var word = src[start..i];
            int? arg = null;
            int ns = i;
            if (i < src.Length && (src[i] == '-' || char.IsDigit(src[i])))
            {
                i++;
                while (i < src.Length && char.IsDigit(src[i])) i++;
                if (int.TryParse(src.AsSpan(ns, i - ns), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)) arg = n;
            }
            if (i < src.Length && src[i] == ' ') i++;
            switch (word)
            {
                case "par" or "line" or "row" or "sect" or "page": Put("\n"); break;
                case "tab" or "cell": Put("\t"); break;
                case "emdash": Put("—"); break;
                case "endash": Put("–"); break;
                case "bullet": Put("•"); break;
                case "lquote": Put("‘"); break;
                case "rquote": Put("’"); break;
                case "ldblquote": Put("“"); break;
                case "rdblquote": Put("”"); break;
                case "ansicpg" when arg is > 0: codePage = arg.Value; break;
                case "uc" when arg is >= 0: uc = arg.Value; break;
                case "u" when arg != null:
                    if (!ignoring) { FlushBytes(); text.Append((char)(arg.Value < 0 ? arg.Value + 65536 : arg.Value)); }
                    skip = uc;
                    break;
                case "fonttbl" or "colortbl" or "stylesheet" or "info" or "pict" or "header" or "footer" or "headerl" or "headerr"
                    or "footerl" or "footerr" or "footnote" or "object" or "listtable" or "listoverridetable" or "rsidtbl"
                    or "generator" or "themedata" or "colorschememapping" or "datastore" or "latentstyles" or "xmlnstbl" or "fldinst":
                    ignoring = true;
                    break;
            }
        }
        FlushBytes();
        var blocks = new List<Block>();
        foreach (var line in text.ToString().Replace("\r", "").Split('\n'))
            blocks.Add(new Block(BlockKind.Paragraph, EscapeMd(line.TrimEnd())));
        while (blocks.Count > 0 && blocks[^1].Text.Length == 0) blocks.RemoveAt(blocks.Count - 1);
        return blocks;
    }

    // ---------------- HTML ----------------

    /// <summary>Turns (X)HTML into blocks: headings, paragraphs, lists, code and tables. Styling is dropped.</summary>
    public static List<Block> FromHtml(string html)
    {
        var list = new List<Block>();
        html = CommentRx().Replace(html, "");
        html = DropRx().Replace(html, "");
        var body = BodyRx().Match(html);
        if (body.Success) html = body.Groups[1].Value;

        var text = new StringBuilder();
        BlockKind kind = BlockKind.Paragraph;
        int number = 0;
        var listStack = new Stack<(bool Ordered, int Count)>();
        List<string[]>? table = null;
        List<string>? row = null;
        StringBuilder? cell = null;
        bool pre = false;

        void Flush()
        {
            var t = WhitespaceRx().Replace(text.ToString(), " ").Trim();
            text.Clear();
            if (t.Length == 0) { kind = BlockKind.Paragraph; return; }
            list.Add(new Block(kind, EscapeMd(t), kind == BlockKind.Numbered ? number : 0));
            kind = BlockKind.Paragraph;
        }

        foreach (Match m in TokenRx().Matches(html))
        {
            if (m.Groups["text"].Success)
            {
                var t = WebUtility.HtmlDecode(m.Groups["text"].Value);
                if (cell != null) cell.Append(t);
                else if (pre) text.Append(t);
                else text.Append(t);
                continue;
            }
            var name = m.Groups["name"].Value.ToLowerInvariant();
            bool close = m.Groups["close"].Success;
            switch (name)
            {
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    Flush();
                    if (!close) kind = name switch { "h1" => BlockKind.Heading1, "h2" => BlockKind.Heading2, _ => BlockKind.Heading3 };
                    break;
                case "p" or "div" or "section" or "article" or "blockquote" or "header" or "footer" or "figure" or "figcaption" or "dd" or "dt":
                    if (cell == null) Flush();
                    break;
                case "br":
                    if (cell != null) cell.Append('\n'); else if (pre) text.Append('\n'); else Flush();
                    break;
                case "ul" or "ol":
                    Flush();
                    if (!close) listStack.Push((name == "ol", 0));
                    else if (listStack.Count > 0) listStack.Pop();
                    break;
                case "li":
                    Flush();
                    if (!close && listStack.Count > 0)
                    {
                        var (ordered, count) = listStack.Pop();
                        listStack.Push((ordered, count + 1));
                        kind = ordered ? BlockKind.Numbered : BlockKind.Bullet;
                        number = count + 1;
                    }
                    else if (!close) kind = BlockKind.Bullet;
                    break;
                case "pre":
                    if (!close) { Flush(); pre = true; }
                    else
                    {
                        pre = false;
                        var code = text.ToString().Trim('\n');
                        text.Clear();
                        if (code.Length > 0) list.Add(new Block(BlockKind.Code, code));
                    }
                    break;
                case "table":
                    if (!close) { Flush(); table = []; }
                    else if (table != null)
                    {
                        if (table.Count > 0)
                        {
                            int cols = table.Max(r => r.Length);
                            list.Add(Block.Table(table.Select(r => r.Concat(Enumerable.Repeat("", cols - r.Length)).ToArray()).ToList()));
                        }
                        table = null;
                    }
                    break;
                case "tr":
                    if (table == null) break;
                    if (!close) row = [];
                    else if (row != null) { if (row.Count > 0) table.Add(row.ToArray()); row = null; }
                    break;
                case "td" or "th":
                    if (row == null) break;
                    if (!close) cell = new StringBuilder();
                    else if (cell != null) { row.Add(WhitespaceRx().Replace(cell.ToString().Replace("\n", "\u0001"), " ").Replace("\u0001", "\n").Trim()); cell = null; }
                    break;
                case "hr":
                    Flush();
                    break;
            }
        }
        Flush();
        return list;
    }

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)] private static partial Regex CommentRx();
    [GeneratedRegex(@"<(script|style|head|nav|svg|math)\b[^>]*>.*?</\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)] private static partial Regex DropRx();
    [GeneratedRegex(@"<body\b[^>]*>(.*)</body\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)] private static partial Regex BodyRx();
    [GeneratedRegex(@"<(?<close>/)?(?<name>[a-zA-Z][a-zA-Z0-9]*)\b[^>]*>|(?<text>[^<]+)")] private static partial Regex TokenRx();
    [GeneratedRegex(@"[ \t\r\n ]+")] private static partial Regex WhitespaceRx();

    // ---------------- EPUB ----------------

    /// <summary>Reads an EPUB's chapters in reading order.</summary>
    public static List<Block> FromEpub(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        XDocument Load(string name)
        {
            var e = zip.GetEntry(name) ?? throw new EngineException(L.T("This EPUB is damaged", "ملف EPUB هذا تالف"));
            using var s = e.Open();
            return XDocument.Load(XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }));
        }
        var container = Load("META-INF/container.xml");
        var opfPath = container.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value
            ?? throw new EngineException(L.T("This EPUB is damaged", "ملف EPUB هذا تالف"));
        var opf = Load(opfPath);
        var baseDir = opfPath.Contains('/') ? opfPath[..(opfPath.LastIndexOf('/') + 1)] : "";
        var manifest = opf.Descendants().Where(e => e.Name.LocalName == "item")
            .ToDictionary(e => (string?)e.Attribute("id") ?? "", e => (string?)e.Attribute("href") ?? "");
        var blocks = new List<Block>();
        foreach (var itemref in opf.Descendants().Where(e => e.Name.LocalName == "itemref"))
        {
            if (!manifest.TryGetValue((string?)itemref.Attribute("idref") ?? "", out var href)) continue;
            var entry = zip.GetEntry(baseDir + Uri.UnescapeDataString(href.Split('#')[0]));
            if (entry == null) continue;
            string html;
            using (var r = new StreamReader(entry.Open(), Encoding.UTF8)) html = r.ReadToEnd();
            var chapter = FromHtml(html);
            if (chapter.Count == 0) continue;
            if (blocks.Count > 0) blocks.Add(new Block(BlockKind.PageBreak, ""));
            blocks.AddRange(chapter);
        }
        return blocks;
    }

    /// <summary>Writes an EPUB 3 book; each top-level heading starts a chapter.</summary>
    public static void ToEpub(IReadOnlyList<Block> blocks, string title, string path)
    {
        bool rtl = blocks.Count(b => b.Rtl) > blocks.Count(b => !b.Rtl && (b.Text.Length > 0 || b.Kind == BlockKind.Table));
        string lang = rtl ? "ar" : "en";

        // split into chapters at Heading1 (or page breaks when there are no headings)
        var chapters = new List<List<Block>> { new() };
        bool hasH1 = blocks.Any(b => b.Kind == BlockKind.Heading1);
        foreach (var b in blocks)
        {
            bool split = hasH1 ? b.Kind == BlockKind.Heading1 : b.Kind == BlockKind.PageBreak;
            if (split && chapters[^1].Count > 0) chapters.Add([]);
            if (b.Kind != BlockKind.PageBreak) chapters[^1].Add(b);
        }
        chapters.RemoveAll(c => c.Count == 0);
        if (chapters.Count == 0) chapters.Add([new Block(BlockKind.Paragraph, "")]);

        string Name(List<Block> ch, int i) => ch.FirstOrDefault(b => b.Kind is BlockKind.Heading1 or BlockKind.Heading2)?.Plain is { Length: > 0 } h
            ? h : chapters.Count == 1 ? title : L.T($"Part {i + 1}", $"الجزء {i + 1}");

        using var fs = new FileStream(path, FileMode.CreateNew);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        void Put(string name, string content, bool store = false)
        {
            var e = zip.CreateEntry(name, store ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
            using var s = new StreamWriter(e.Open(), new UTF8Encoding(false));
            s.Write(content);
        }
        Put("mimetype", "application/epub+zip", store: true); // must be first and uncompressed
        Put("META-INF/container.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\"><rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>");
        Put("OEBPS/style.css", "body{font-family:serif;line-height:1.6;margin:0 4%}\nh1,h2,h3{font-family:sans-serif;line-height:1.25}\npre{white-space:pre-wrap;font-family:monospace;font-size:.9em;text-align:left}\ntable{border-collapse:collapse;margin:1em 0}\nth,td{border:1px solid #999;padding:.2em .5em;text-align:start;vertical-align:top}\n");

        string Xhtml(string head, string body) =>
            $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE html>\n<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" xml:lang=\"{lang}\" lang=\"{lang}\" dir=\"{(rtl ? "rtl" : "ltr")}\">\n<head><meta charset=\"utf-8\"/><title>{XmlEscape(head)}</title><link rel=\"stylesheet\" type=\"text/css\" href=\"style.css\"/></head>\n<body>\n{body}</body>\n</html>\n";

        for (int i = 0; i < chapters.Count; i++)
        {
            var html = ToHtml(chapters[i], title);
            var inner = BodyRx().Match(html).Groups[1].Value;
            inner = inner.Replace("<br>", "<br/>").Replace("<hr>", "<hr/>").Replace("&nbsp;", "&#160;");
            Put($"OEBPS/chapter{i + 1}.xhtml", Xhtml(Name(chapters[i], i), inner));
        }
        var nav = new StringBuilder();
        for (int i = 0; i < chapters.Count; i++)
            nav.Append($"<li><a href=\"chapter{i + 1}.xhtml\">{XmlEscape(Name(chapters[i], i))}</a></li>\n");
        Put("OEBPS/nav.xhtml", Xhtml(title, $"<nav epub:type=\"toc\" id=\"toc\"><h1>{XmlEscape(L.T("Contents", "المحتويات"))}</h1>\n<ol>\n{nav}</ol></nav>\n"));

        var items = new StringBuilder();
        var spine = new StringBuilder();
        for (int i = 0; i < chapters.Count; i++)
        {
            items.Append($"<item id=\"c{i + 1}\" href=\"chapter{i + 1}.xhtml\" media-type=\"application/xhtml+xml\"/>\n");
            spine.Append($"<itemref idref=\"c{i + 1}\"/>\n");
        }
        var id = "urn:uuid:" + Guid.NewGuid();
        var modified = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        Put("OEBPS/content.opf",
            $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"bookid\" xml:lang=\"{lang}\">\n" +
            $"<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:identifier id=\"bookid\">{id}</dc:identifier><dc:title>{XmlEscape(title)}</dc:title><dc:language>{lang}</dc:language><meta property=\"dcterms:modified\">{modified}</meta></metadata>\n" +
            $"<manifest><item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/><item id=\"css\" href=\"style.css\" media-type=\"text/css\"/>\n{items}</manifest>\n" +
            $"<spine{(rtl ? " page-progression-direction=\"rtl\"" : "")}>\n{spine}</spine>\n</package>\n");
    }
}

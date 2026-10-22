using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace ShiftDrop.Core.Engines;

/// <summary>One worksheet: a name and rows of cell text.</summary>
public sealed record Sheet(string Name, List<string[]> Rows);

/// <summary>
/// Spreadsheets without Office: reads and writes XLSX, ODS, CSV and TSV (values only;
/// formulas come in as their last calculated values, formatting is not kept).
/// </summary>
public static partial class Sheets
{
    const int MaxRepeat = 1000; // ODS "repeat this cell 16384 times" padding is cut here

    // ---------------- reading ----------------

    public static List<Sheet> Read(string path)
    {
        var sheets = FileKinds.Ext(path) switch
        {
            "xlsx" or "xlsm" => ReadXlsx(path),
            "ods" => ReadOds(path),
            "tsv" => [new Sheet(FileKinds.BaseName(path), ReadDelimited(Documents.ReadText(path), '\t'))],
            _ => [new Sheet(FileKinds.BaseName(path), ReadDelimited(Documents.ReadText(path), null))],
        };
        foreach (var s in sheets) Trim(s.Rows);
        sheets.RemoveAll(s => s.Rows.Count == 0);
        if (sheets.Count == 0) throw new EngineException(L.T("This spreadsheet is empty", "هذا الجدول فارغ"));
        return sheets;
    }

    /// <summary>Drops empty rows at the end and empty columns on the right; makes every row the same width.</summary>
    static void Trim(List<string[]> rows)
    {
        while (rows.Count > 0 && rows[^1].All(string.IsNullOrEmpty)) rows.RemoveAt(rows.Count - 1);
        int cols = 0;
        foreach (var r in rows)
            for (int i = r.Length - 1; i >= 0; i--)
                if (!string.IsNullOrEmpty(r[i])) { cols = Math.Max(cols, i + 1); break; }
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Length != cols) rows[i] = rows[i].Take(cols).Concat(Enumerable.Repeat("", Math.Max(0, cols - rows[i].Length))).ToArray();
    }

    /// <summary>CSV/TSV with quotes; the separator is guessed (comma, semicolon or tab) when not given.</summary>
    public static List<string[]> ReadDelimited(string text, char? separator)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        char sep = separator ?? Guess(text);
        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false, any = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else cell.Append(c);
                continue;
            }
            if (c == '"' && cell.Length == 0) { quoted = true; any = true; continue; }
            if (c == sep) { row.Add(cell.ToString()); cell.Clear(); any = true; continue; }
            if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear();
                rows.Add(row.ToArray()); row.Clear(); any = false;
                continue;
            }
            cell.Append(c); any = true;
        }
        if (any || cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row.ToArray()); }
        return rows;
    }

    static char Guess(string text)
    {
        var first = text.Split('\n', 6).Take(5).ToArray();
        int Count(char ch) => first.Sum(l => l.Count(x => x == ch));
        int comma = Count(','), semi = Count(';'), tab = Count('\t');
        if (tab > comma && tab >= semi) return '\t';
        return semi > comma ? ';' : ',';
    }

    static XmlReader Xml(Stream s) => XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = false });

    static List<Sheet> ReadXlsx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        // shared strings
        var shared = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") is { } ss)
        {
            using var r = Xml(ss.Open());
            while (r.Read())
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "si")
                {
                    var sb = new StringBuilder();
                    using var sub = r.ReadSubtree();
                    bool inPhonetic = false;
                    while (sub.Read())
                    {
                        if (sub.NodeType == XmlNodeType.Element && sub.LocalName == "rPh") inPhonetic = true;
                        else if (sub.NodeType == XmlNodeType.EndElement && sub.LocalName == "rPh") inPhonetic = false;
                        else if (!inPhonetic && sub.NodeType == XmlNodeType.Element && sub.LocalName == "t") sb.Append(sub.ReadElementContentAsString());
                    }
                    shared.Add(sb.ToString());
                }
        }

        // which cell styles are dates
        var dateStyles = new HashSet<int>();
        if (zip.GetEntry("xl/styles.xml") is { } st)
        {
            var custom = new Dictionary<int, string>();
            var xfs = new List<int>();
            using var r = Xml(st.Open());
            bool inCellXfs = false;
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "numFmt" &&
                    int.TryParse(r.GetAttribute("numFmtId"), out var id)) custom[id] = r.GetAttribute("formatCode") ?? "";
                else if (r.NodeType == XmlNodeType.Element && r.LocalName == "cellXfs") inCellXfs = true;
                else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "cellXfs") inCellXfs = false;
                else if (inCellXfs && r.NodeType == XmlNodeType.Element && r.LocalName == "xf")
                    xfs.Add(int.TryParse(r.GetAttribute("numFmtId"), out var f) ? f : 0);
            }
            for (int i = 0; i < xfs.Count; i++)
            {
                int f = xfs[i];
                if (f is >= 14 and <= 22 or >= 45 and <= 47 || custom.TryGetValue(f, out var code) && IsDateFormat(code))
                    dateStyles.Add(i);
            }
        }

        // sheets in workbook order
        var rels = new Dictionary<string, string>();
        if (zip.GetEntry("xl/_rels/workbook.xml.rels") is { } re)
        {
            using var r = Xml(re.Open());
            while (r.Read())
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "Relationship")
                {
                    var target = r.GetAttribute("Target") ?? "";
                    target = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                    rels[r.GetAttribute("Id") ?? ""] = target;
                }
        }
        var list = new List<Sheet>();
        var wb = zip.GetEntry("xl/workbook.xml") ?? throw new EngineException(L.T("This is not an Excel file", "هذا ليس ملف Excel"));
        var order = new List<(string Name, string Target)>();
        using (var r = Xml(wb.Open()))
            while (r.Read())
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "sheet" && r.NamespaceURI == Main)
                {
                    var rid = r.GetAttribute("id", Rel) ?? "";
                    if (rels.TryGetValue(rid, out var t)) order.Add((r.GetAttribute("name") ?? $"Sheet{order.Count + 1}", t));
                }
        foreach (var (name, target) in order)
        {
            var entry = zip.GetEntry(target);
            if (entry == null) continue;
            list.Add(new Sheet(name, ReadXlsxSheet(entry, shared, dateStyles)));
        }
        return list;
    }

    static bool IsDateFormat(string code)
    {
        var plain = QuotedRx().Replace(code, "");
        plain = BracketRx().Replace(plain, "");
        return DatePartRx().IsMatch(plain);
    }

    static List<string[]> ReadXlsxSheet(ZipArchiveEntry entry, List<string> shared, HashSet<int> dateStyles)
    {
        var rows = new SortedDictionary<int, Dictionary<int, string>>();
        using var r = Xml(entry.Open());
        int rowIndex = 0;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element) continue;
            if (r.LocalName == "row")
            {
                rowIndex = int.TryParse(r.GetAttribute("r"), out var ri) ? ri : rowIndex + 1;
                continue;
            }
            if (r.LocalName != "c") continue;
            var refAttr = r.GetAttribute("r");
            var type = r.GetAttribute("t") ?? "n";
            int style = int.TryParse(r.GetAttribute("s"), out var sv) ? sv : 0;
            int col = refAttr != null ? ColumnIndex(refAttr) : (rows.TryGetValue(rowIndex, out var existing) ? existing.Count : 0);
            string? value = null, inline = null;
            if (!r.IsEmptyElement)
            {
                using var sub = r.ReadSubtree();
                while (sub.Read())
                {
                    if (sub.NodeType != XmlNodeType.Element) continue;
                    if (sub.LocalName == "v") value = sub.ReadElementContentAsString();
                    else if (sub.LocalName == "t") inline = (inline ?? "") + sub.ReadElementContentAsString();
                }
            }
            string text = type switch
            {
                "s" => int.TryParse(value, out var si) && si >= 0 && si < shared.Count ? shared[si] : "",
                "inlineStr" => inline ?? "",
                "b" => value == "1" ? "TRUE" : "FALSE",
                "str" or "e" => value ?? "",
                _ => FormatNumber(value, dateStyles.Contains(style)),
            };
            if (!rows.TryGetValue(rowIndex, out var cells)) rows[rowIndex] = cells = [];
            cells[col] = text;
        }
        var result = new List<string[]>();
        int expected = 1;
        foreach (var (index, cells) in rows)
        {
            // keep blank rows between data rows (up to a point)
            for (; expected < index && expected > 0 && index - expected < 1000; expected++) result.Add([]);
            expected = index + 1;
            int width = cells.Count == 0 ? 0 : cells.Keys.Max() + 1;
            var row = new string[Math.Min(width, 16384)];
            for (int i = 0; i < row.Length; i++) row[i] = cells.GetValueOrDefault(i, "");
            result.Add(row);
        }
        return result;
    }

    static string FormatNumber(string? value, bool date)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return value;
        if (date && d is > -657435 and < 2958466)
        {
            var dt = DateTime.FromOADate(d);
            return dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : d < 1 ? dt.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        // Excel stores 0.1 as 0.10000000000000001; show the short form
        return d.ToString("R", CultureInfo.InvariantCulture) is var s && s.Length > 17 ? d.ToString("G15", CultureInfo.InvariantCulture) : s;
    }

    /// <summary>"BC12" → 54 (zero-based column).</summary>
    static int ColumnIndex(string cellRef)
    {
        int col = 0;
        foreach (var ch in cellRef)
        {
            if (ch is >= 'A' and <= 'Z') col = col * 26 + (ch - 'A' + 1);
            else if (ch is >= 'a' and <= 'z') col = col * 26 + (ch - 'a' + 1);
            else break;
        }
        return Math.Max(0, col - 1);
    }

    static string ColumnName(int index)
    {
        var sb = new StringBuilder();
        for (index++; index > 0; index = (index - 1) / 26) sb.Insert(0, (char)('A' + (index - 1) % 26));
        return sb.ToString();
    }

    static List<Sheet> ReadOds(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("content.xml") ?? throw new EngineException(L.T("This is not an ODS spreadsheet", "هذا ليس ملف ODS"));
        const string T = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
        const string O = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
        var sheets = new List<Sheet>();
        using var r = Xml(entry.Open());
        List<string[]>? rows = null;
        List<string>? row = null;
        int rowRepeat = 1;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element && r.NamespaceURI == T)
            {
                switch (r.LocalName)
                {
                    case "table":
                        rows = [];
                        sheets.Add(new Sheet(r.GetAttribute("name", T) ?? $"Sheet{sheets.Count + 1}", rows));
                        break;
                    case "table-row":
                        rowRepeat = Math.Clamp(int.TryParse(r.GetAttribute("number-rows-repeated", T), out var rr) ? rr : 1, 1, MaxRepeat);
                        row = [];
                        if (r.IsEmptyElement) { AddRow(); }
                        break;
                    case "table-cell" or "covered-table-cell":
                    {
                        int repeat = Math.Clamp(int.TryParse(r.GetAttribute("number-columns-repeated", T), out var cr) ? cr : 1, 1, MaxRepeat);
                        var type = r.GetAttribute("value-type", O);
                        string text;
                        if (type is "float" or "percentage" or "currency") text = r.GetAttribute("value", O) ?? "";
                        else if (type == "date") text = (r.GetAttribute("date-value", O) ?? "").Replace("T00:00:00", "").Replace('T', ' ');
                        else if (type == "time") text = r.GetAttribute("time-value", O) ?? "";
                        else if (type == "boolean") text = r.GetAttribute("boolean-value", O) == "true" ? "TRUE" : "FALSE";
                        else text = "";
                        if (!r.IsEmptyElement)
                        {
                            var shown = CellText(r);
                            if (type is null or "string" || text.Length == 0) text = shown;
                        }
                        if (row != null) for (int i = 0; i < repeat; i++) row.Add(text);
                        break;
                    }
                }
            }
            else if (r.NodeType == XmlNodeType.EndElement && r.NamespaceURI == T && r.LocalName == "table-row") AddRow();
        }
        return sheets;

        void AddRow()
        {
            if (rows == null || row == null) return;
            var arr = row.ToArray();
            // trailing empty cells from repeats don't count
            int last = Array.FindLastIndex(arr, c => c.Length > 0);
            arr = arr.Take(last + 1).ToArray();
            // a huge repeat of empty rows at the end is just padding
            int times = arr.Length == 0 ? Math.Min(rowRepeat, 50) : rowRepeat;
            for (int i = 0; i < times; i++) rows.Add(arr);
            row = null;
        }
    }

    /// <summary>The text inside a table cell: paragraphs joined by new lines, text:s spaces and tabs expanded.</summary>
    static string CellText(XmlReader r)
    {
        const string Tx = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
        var sb = new StringBuilder();
        int paragraphs = 0;
        using var sub = r.ReadSubtree();
        while (sub.Read())
        {
            if (sub.NodeType == XmlNodeType.Element && sub.NamespaceURI == Tx)
            {
                if (sub.LocalName is "p" or "h") { if (paragraphs++ > 0) sb.Append('\n'); }
                else if (sub.LocalName == "s") sb.Append(' ', int.TryParse(sub.GetAttribute("c", Tx), out var n) ? Math.Clamp(n, 1, 100) : 1);
                else if (sub.LocalName == "tab") sb.Append('\t');
                else if (sub.LocalName == "line-break") sb.Append('\n');
            }
            else if (sub.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace) sb.Append(sub.Value);
        }
        return sb.ToString();
    }

    // ---------------- writing ----------------

    /// <summary>Writes one file per sheet for CSV/TSV (when there are several), or one workbook for XLSX/ODS.</summary>
    public static List<string> Write(IReadOnlyList<Sheet> sheets, string ext, Func<string, string> outPath, string baseName)
    {
        var written = new List<string>();
        switch (ext)
        {
            case "xlsx": { var p = outPath($"{baseName}.xlsx"); WriteXlsx(sheets, p); written.Add(p); break; }
            case "ods": { var p = outPath($"{baseName}.ods"); WriteOds(sheets, p); written.Add(p); break; }
            case "csv" or "tsv":
                foreach (var s in sheets)
                {
                    var name = sheets.Count == 1 ? $"{baseName}.{ext}" : $"{baseName} - {s.Name}.{ext}";
                    var p = outPath(name);
                    WriteDelimited(s.Rows, p, ext == "tsv" ? '\t' : ',');
                    written.Add(p);
                }
                break;
            default: throw new EngineException("Unsupported spreadsheet format: " + ext);
        }
        return written;
    }

    public static void WriteDelimited(IReadOnlyList<string[]> rows, string path, char sep)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            for (int i = 0; i < row.Length; i++)
            {
                if (i > 0) sb.Append(sep);
                var c = row[i] ?? "";
                if (sep == '\t') sb.Append(c.Replace('\t', ' ').Replace("\r", "").Replace('\n', ' '));
                else if (c.IndexOfAny([sep, '"', '\n', '\r']) >= 0 || c.StartsWith(' ') || c.EndsWith(' ')) sb.Append('"').Append(c.Replace("\"", "\"\"")).Append('"');
                else sb.Append(c);
            }
            sb.Append("\r\n");
        }
        // a BOM so Excel opens Arabic (and any non-English) text correctly
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    /// <summary>A cell that should be stored as a number: plain decimals, and thousands-grouped ones ("1,250.75").</summary>
    public static bool TryNumber(string s, out double value)
    {
        value = 0;
        s = s.Trim();
        if (s.Length is 0 or > 20) return false;
        // keep leading-zero codes (phone numbers, postcodes) as text
        if (s.Length > 1 && s[0] == '0' && s[1] != '.') return false;
        if (s.StartsWith("-0") && s.Length > 2 && s[2] != '.') return false;
        if (PlainNumberRx().IsMatch(s)) return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        if (GroupedNumberRx().IsMatch(s)) return double.TryParse(s.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        return false;
    }

    static string SafeSheetName(string name, HashSet<string> used)
    {
        var n = InvalidSheetCharsRx().Replace(name, " ").Trim().Trim('\'');
        if (n.Length == 0) n = "Sheet";
        if (n.Length > 31) n = n[..31];
        var unique = n;
        for (int i = 2; !used.Add(unique.ToLowerInvariant()); i++)
        {
            var suffix = $" ({i})";
            unique = (n.Length + suffix.Length > 31 ? n[..(31 - suffix.Length)] : n) + suffix;
        }
        return unique;
    }

    public static void WriteXlsx(IReadOnlyList<Sheet> sheets, string path)
    {
        using var fs = new FileStream(path, FileMode.CreateNew);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        void Put(string name, string xml)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(xml);
        }
        const string Head = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";
        var used = new HashSet<string>();
        var names = sheets.Select(s => SafeSheetName(s.Name, used)).ToList();

        var types = new StringBuilder(Head + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
        for (int i = 0; i < sheets.Count; i++)
            types.Append($"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        types.Append("</Types>");
        Put("[Content_Types].xml", types.ToString());
        Put("_rels/.rels", Head + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");

        var wb = new StringBuilder(Head + "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
        var wbRels = new StringBuilder(Head + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (int i = 0; i < sheets.Count; i++)
        {
            wb.Append($"<sheet name=\"{Documents.XmlEscape(names[i])}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
            wbRels.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
        }
        wbRels.Append($"<Relationship Id=\"rId{sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
        wb.Append("</sheets></workbook>");
        Put("xl/workbook.xml", wb.ToString());
        Put("xl/_rels/workbook.xml.rels", wbRels.ToString());
        // style 0: normal; style 1: wrapped text (cells with line breaks)
        Put("xl/styles.xml", Head + "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment wrapText=\"1\" vertical=\"top\"/></xf></cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");

        for (int si = 0; si < sheets.Count; si++)
        {
            var rows = sheets[si].Rows;
            bool rtl = Documents.IsRtl(string.Concat(rows.Take(20).SelectMany(r => r).Select(c => c + " ")));
            int cols = rows.Count == 0 ? 0 : rows.Max(r => r.Length);
            var sb = new StringBuilder(Head + "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sb.Append("<sheetViews><sheetView workbookViewId=\"0\"").Append(rtl ? " rightToLeft=\"1\"" : "").Append("/></sheetViews>");
            if (cols > 0)
            {
                sb.Append("<cols>");
                for (int c = 0; c < cols; c++)
                {
                    int len = rows.Take(500).Max(r => c < r.Length ? r[c].Split('\n').Max(l => l.Length) : 0);
                    double width = Math.Clamp(len * 1.15 + 2, 8, 60);
                    sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{width.ToString("0.##", CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
                }
                sb.Append("</cols>");
            }
            sb.Append("<sheetData>");
            for (int r = 0; r < rows.Count; r++)
            {
                sb.Append($"<row r=\"{r + 1}\">");
                for (int c = 0; c < rows[r].Length; c++)
                {
                    var v = rows[r][c];
                    if (string.IsNullOrEmpty(v)) continue;
                    var cellRef = ColumnName(c) + (r + 1);
                    if (TryNumber(v, out var num))
                        sb.Append($"<c r=\"{cellRef}\"><v>{num.ToString("R", CultureInfo.InvariantCulture)}</v></c>");
                    else
                        sb.Append($"<c r=\"{cellRef}\" t=\"inlineStr\"{(v.Contains('\n') ? " s=\"1\"" : "")}><is><t xml:space=\"preserve\">{Documents.XmlEscape(v.Length > 32767 ? v[..32767] : v)}</t></is></c>");
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData></worksheet>");
            Put($"xl/worksheets/sheet{si + 1}.xml", sb.ToString());
        }
    }

    public static void WriteOds(IReadOnlyList<Sheet> sheets, string path)
    {
        using var fs = new FileStream(path, FileMode.CreateNew);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        void Put(string name, string content, bool store = false)
        {
            var e = zip.CreateEntry(name, store ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(content);
        }
        Put("mimetype", "application/vnd.oasis.opendocument.spreadsheet", store: true); // first, uncompressed
        Put("META-INF/manifest.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<manifest:manifest xmlns:manifest=\"urn:oasis:names:tc:opendocument:xmlns:manifest:1.0\" manifest:version=\"1.2\"><manifest:file-entry manifest:full-path=\"/\" manifest:version=\"1.2\" manifest:media-type=\"application/vnd.oasis.opendocument.spreadsheet\"/><manifest:file-entry manifest:full-path=\"content.xml\" manifest:media-type=\"text/xml\"/></manifest:manifest>");
        var used = new HashSet<string>();
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<office:document-content xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" office:version=\"1.2\"><office:body><office:spreadsheet>");
        foreach (var s in sheets)
        {
            sb.Append($"<table:table table:name=\"{Documents.XmlEscape(SafeSheetName(s.Name, used))}\">");
            int cols = s.Rows.Count == 0 ? 1 : Math.Max(1, s.Rows.Max(r => r.Length));
            sb.Append($"<table:table-column table:number-columns-repeated=\"{cols}\"/>");
            foreach (var row in s.Rows)
            {
                sb.Append("<table:table-row>");
                if (row.Length == 0) sb.Append("<table:table-cell/>");
                foreach (var v in row)
                {
                    if (string.IsNullOrEmpty(v)) { sb.Append("<table:table-cell/>"); continue; }
                    var paras = string.Concat(v.Split('\n').Select(l => $"<text:p>{Documents.XmlEscape(l)}</text:p>"));
                    if (TryNumber(v, out var num))
                        sb.Append($"<table:table-cell office:value-type=\"float\" office:value=\"{num.ToString("R", CultureInfo.InvariantCulture)}\">{paras}</table:table-cell>");
                    else
                        sb.Append($"<table:table-cell office:value-type=\"string\">{paras}</table:table-cell>");
                }
                sb.Append("</table:table-row>");
            }
            sb.Append("</table:table>");
        }
        sb.Append("</office:spreadsheet></office:body></office:document-content>");
        Put("content.xml", sb.ToString());
    }

    /// <summary>Sheets as document blocks (a heading per sheet when there are several), for PDF and HTML.</summary>
    public static List<Block> ToBlocks(IReadOnlyList<Sheet> sheets)
    {
        var blocks = new List<Block>();
        foreach (var s in sheets)
        {
            if (blocks.Count > 0) blocks.Add(new Block(BlockKind.PageBreak, ""));
            if (sheets.Count > 1) blocks.Add(new Block(BlockKind.Heading2, Documents.EscapeMd(s.Name)));
            if (s.Rows.Count > 0) blocks.Add(Block.Table(s.Rows));
        }
        return blocks;
    }

    [GeneratedRegex("\"[^\"]*\"")] private static partial Regex QuotedRx();
    [GeneratedRegex(@"\[[^\]]*\]")] private static partial Regex BracketRx();
    [GeneratedRegex("[dDyY]|(?<![A-Za-z])[mM]{1,5}(?![A-Za-z])|[hH]")] private static partial Regex DatePartRx();
    [GeneratedRegex(@"^-?(\d+(\.\d+)?|\.\d+)([eE][-+]?\d{1,3})?$")] private static partial Regex PlainNumberRx();
    [GeneratedRegex(@"^-?\d{1,3}(,\d{3})+(\.\d+)?$")] private static partial Regex GroupedNumberRx();
    [GeneratedRegex(@"[\[\]:*?/\\]")] private static partial Regex InvalidSheetCharsRx();
}

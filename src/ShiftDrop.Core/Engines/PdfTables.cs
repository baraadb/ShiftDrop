namespace ShiftDrop.Core.Engines;

/// <summary>
/// Rebuilds rows and columns from where text sits on PDF pages. Works well for invoices,
/// statements and reports made by programs; scanned pages have no text to find.
/// </summary>
public static class PdfTables
{
    public static List<string[]> Rows(IReadOnlyList<List<Pdfium.Word>> pages)
    {
        var all = new List<string[]>();
        foreach (var words in pages)
        {
            if (words.Count == 0) continue;
            var rows = PageRows(words);
            if (all.Count > 0 && rows.Count > 0) all.Add([]);
            all.AddRange(rows);
        }
        int cols = all.Count == 0 ? 0 : all.Max(r => r.Length);
        return all.Select(r => r.Length == cols ? r : r.Concat(Enumerable.Repeat("", cols - r.Length)).ToArray()).ToList();
    }

    sealed record Cell(string Text, double Left, double Right);

    static List<string[]> PageRows(List<Pdfium.Word> words)
    {
        double h = Median(words.Select(w => w.Height));
        if (h <= 0) h = 10;

        // 1. lines, top to bottom
        var lines = new List<List<Pdfium.Word>>();
        foreach (var w in words.OrderByDescending(w => w.Middle))
        {
            var line = lines.LastOrDefault();
            if (line != null && Math.Abs(line.Average(x => x.Middle) - w.Middle) < h * 0.5) line.Add(w);
            else lines.Add([w]);
        }

        // 2. cells: words on a line that sit close together belong to one cell
        var cellLines = new List<List<Cell>>();
        foreach (var line in lines)
        {
            var cells = new List<Cell>();
            foreach (var w in line.OrderBy(w => w.Left))
            {
                var last = cells.LastOrDefault();
                if (last != null && w.Left - last.Right < h * 1.0)
                    cells[^1] = new Cell(last.Text + " " + w.Text, last.Left, Math.Max(last.Right, w.Right));
                else cells.Add(new Cell(w.Text, w.Left, w.Right));
            }
            cellLines.Add(cells);
        }

        // 3. columns: x-ranges that overlap from line to line (lines with one cell, like titles, don't shape columns)
        var bands = new List<(double L, double R)>();
        foreach (var cells in cellLines.Where(c => c.Count >= 2))
            foreach (var c in cells) bands.Add((c.Left, c.Right));
        bands = Merge(bands, h * 0.3);
        if (bands.Count == 0) bands = [(double.MinValue, double.MaxValue)];

        // 4. rows
        bool rtl = Documents.IsRtl(string.Concat(words.Take(60).Select(w => w.Text + " ")));
        var rows = new List<string[]>();
        foreach (var cells in cellLines)
        {
            var row = new string[bands.Count];
            for (int i = 0; i < row.Length; i++) row[i] = "";
            foreach (var c in cells)
            {
                int best = 0;
                double bestOverlap = double.MinValue;
                for (int i = 0; i < bands.Count; i++)
                {
                    double overlap = Math.Min(c.Right, bands[i].R) - Math.Max(c.Left, bands[i].L);
                    if (overlap > bestOverlap) { bestOverlap = overlap; best = i; }
                }
                row[best] = row[best].Length == 0 ? c.Text : row[best] + " " + c.Text;
            }
            if (rtl) Array.Reverse(row); // the first column of an Arabic table is on the right
            rows.Add(row);
        }
        return rows;
    }

    static List<(double L, double R)> Merge(List<(double L, double R)> ranges, double slack)
    {
        var merged = new List<(double L, double R)>();
        foreach (var r in ranges.OrderBy(r => r.L))
        {
            if (merged.Count > 0 && r.L <= merged[^1].R + slack)
                merged[^1] = (merged[^1].L, Math.Max(merged[^1].R, r.R));
            else merged.Add(r);
        }
        return merged;
    }

    static double Median(IEnumerable<double> values)
    {
        var v = values.Where(x => x > 0).OrderBy(x => x).ToList();
        return v.Count == 0 ? 0 : v[v.Count / 2];
    }
}

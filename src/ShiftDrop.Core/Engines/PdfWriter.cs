using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace ShiftDrop.Core.Engines;

/// <summary>One page = one picture. JPEG data is embedded as is; other pixels are Flate-compressed RGB (+ alpha mask).</summary>
public sealed class PdfImagePage
{
    public int Width, Height;
    public byte[] Data = [];
    public bool IsJpeg;
    public int JpegComponents = 3;
    public byte[]? AlphaFlate;

    public static PdfImagePage FromJpeg(byte[] jpeg)
    {
        var (w, h, comps) = JpegInfo(jpeg) ?? throw new EngineException("Unreadable JPEG");
        return new PdfImagePage { Width = w, Height = h, Data = jpeg, IsJpeg = true, JpegComponents = comps };
    }

    /// <summary>From straight RGBA pixels.</summary>
    public static PdfImagePage FromRgba(byte[] rgba, int w, int h)
    {
        var rgb = new byte[w * h * 3];
        var a = new byte[w * h];
        bool anyAlpha = false;
        for (int i = 0, j = 0, k = 0; i < w * h; i++, j += 4, k += 3)
        {
            rgb[k] = rgba[j]; rgb[k + 1] = rgba[j + 1]; rgb[k + 2] = rgba[j + 2];
            a[i] = rgba[j + 3];
            if (a[i] != 255) anyAlpha = true;
        }
        return new PdfImagePage
        {
            Width = w, Height = h, Data = Deflate(rgb),
            AlphaFlate = anyAlpha ? Deflate(a) : null,
        };
    }

    public static byte[] Deflate(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(raw);
        return ms.ToArray();
    }

    /// <summary>Width, height and component count from the JPEG frame header.</summary>
    public static (int W, int H, int Comps)? JpegInfo(byte[] d)
    {
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) return null;
        int i = 2;
        while (i + 9 < d.Length)
        {
            if (d[i] != 0xFF) { i++; continue; }
            byte m = d[i + 1];
            if (m == 0xFF) { i++; continue; }
            if (m == 0xD8 || m == 0x01 || (m >= 0xD0 && m <= 0xD7)) { i += 2; continue; }
            int len = (d[i + 2] << 8) | d[i + 3];
            bool sof = m >= 0xC0 && m <= 0xCF && m != 0xC4 && m != 0xC8 && m != 0xCC;
            if (sof)
            {
                int h = (d[i + 5] << 8) | d[i + 6];
                int w = (d[i + 7] << 8) | d[i + 8];
                int c = d[i + 9];
                return (w, h, c);
            }
            i += 2 + len;
        }
        return null;
    }
}

public static class PdfWriter
{
    /// <summary>Writes a PDF with one picture per page. Pages are A4-sized at most, keeping the picture's shape.</summary>
    public static void Write(Stream s, IReadOnlyList<PdfImagePage> pages)
    {
        var offsets = new List<long>();
        var w = new BinaryWriter(s, Encoding.ASCII, leaveOpen: true);
        void Raw(string t) => w.Write(Encoding.Latin1.GetBytes(t));
        int next = 3; // 1 = catalog, 2 = pages
        var pageIds = new List<int>();
        var plan = new List<(int Page, int Content, int Img, int? Mask)>();
        foreach (var p in pages)
        {
            int page = next++, content = next++, img = next++;
            int? mask = p.AlphaFlate != null ? next++ : null;
            plan.Add((page, content, img, mask));
            pageIds.Add(page);
        }
        int total = next - 1;
        var off = new long[total + 1];

        Raw("%PDF-1.7\n%âãÏÓ\n");
        void Obj(int id) { w.Flush(); off[id] = s.Position; Raw($"{id} 0 obj\n"); }
        void End() => Raw("endobj\n");

        Obj(1); Raw("<< /Type /Catalog /Pages 2 0 R >>\n"); End();
        Obj(2);
        Raw("<< /Type /Pages /Count " + pages.Count + " /Kids [" +
            string.Join(" ", pageIds.Select(id => id + " 0 R")) + "] >>\n");
        End();

        var inv = CultureInfo.InvariantCulture;
        for (int i = 0; i < pages.Count; i++)
        {
            var p = pages[i];
            var (pid, cid, iid, mid) = plan[i];
            // 96 px per inch, then shrink to fit within A4 (595 x 842 pt) when larger
            double pw = p.Width * 0.75, ph = p.Height * 0.75;
            double fit = Math.Min(1, Math.Min(842 / Math.Max(pw, ph), 595 / Math.Min(pw, ph)));
            pw = Math.Max(1, pw * fit); ph = Math.Max(1, ph * fit);
            string sw = pw.ToString("0.###", inv), sh = ph.ToString("0.###", inv);

            Obj(pid);
            Raw($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {sw} {sh}] /Resources << /XObject << /Im0 {iid} 0 R >> >> /Contents {cid} 0 R >>\n");
            End();

            var content = Encoding.ASCII.GetBytes($"q {sw} 0 0 {sh} 0 0 cm /Im0 Do Q\n");
            Obj(cid); Raw($"<< /Length {content.Length} >>\nstream\n"); w.Write(content); Raw("\nendstream\n"); End();

            Obj(iid);
            if (p.IsJpeg)
            {
                var cs = p.JpegComponents == 1 ? "/DeviceGray" : p.JpegComponents == 4 ? "/DeviceCMYK" : "/DeviceRGB";
                var decode = p.JpegComponents == 4 ? " /Decode [1 0 1 0 1 0 1 0]" : "";
                Raw($"<< /Type /XObject /Subtype /Image /Width {p.Width} /Height {p.Height} /ColorSpace {cs} /BitsPerComponent 8 /Filter /DCTDecode{decode} /Length {p.Data.Length} >>\nstream\n");
            }
            else
            {
                var smask = mid is int m ? $" /SMask {m} 0 R" : "";
                Raw($"<< /Type /XObject /Subtype /Image /Width {p.Width} /Height {p.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode{smask} /Length {p.Data.Length} >>\nstream\n");
            }
            w.Write(p.Data);
            Raw("\nendstream\n");
            End();

            if (mid is int maskId)
            {
                Obj(maskId);
                Raw($"<< /Type /XObject /Subtype /Image /Width {p.Width} /Height {p.Height} /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode /Length {p.AlphaFlate!.Length} >>\nstream\n");
                w.Write(p.AlphaFlate);
                Raw("\nendstream\n");
                End();
            }
        }

        w.Flush();
        long xref = s.Position;
        Raw($"xref\n0 {total + 1}\n0000000000 65535 f \n");
        for (int id = 1; id <= total; id++) Raw(off[id].ToString("D10", inv) + " 00000 n \n");
        Raw($"trailer\n<< /Size {total + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        w.Flush();
    }
}

using System.Buffers.Binary;

namespace ShiftDrop.Core.Engines;

/// <summary>Lossless metadata removal for JPEG and PNG: the picture data is copied byte for byte.</summary>
public static class Metadata
{
    /// <summary>Drops EXIF/XMP (APP1), Photoshop/IPTC (APP13), comments and other APPn, keeping JFIF, ICC colour and Adobe colour info.</summary>
    public static byte[] StripJpeg(byte[] d)
    {
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) throw new EngineException("Not a JPEG file");
        using var o = new MemoryStream(d.Length);
        o.Write(d, 0, 2);
        int i = 2;
        while (i + 4 <= d.Length)
        {
            if (d[i] != 0xFF) throw new EngineException("Damaged JPEG file");
            byte m = d[i + 1];
            if (m == 0xFF) { i++; continue; }
            if (m == 0xDA) { o.Write(d, i, d.Length - i); break; } // start of scan: copy the rest as is
            if (m == 0xD8 || m == 0x01 || (m >= 0xD0 && m <= 0xD7)) { o.Write(d, i, 2); i += 2; continue; }
            int len = (d[i + 2] << 8) | d[i + 3];
            if (len < 2 || i + 2 + len > d.Length) throw new EngineException("Damaged JPEG file");
            bool keep = true;
            if (m == 0xFE) keep = false;                                    // COM
            else if (m >= 0xE1 && m <= 0xEF)                                 // APP1..APP15
            {
                keep = (m == 0xE2 && HasPrefix(d, i + 4, "ICC_PROFILE"u8)) ||
                       (m == 0xEE && HasPrefix(d, i + 4, "Adobe"u8));
            }
            if (keep) o.Write(d, i, 2 + len);
            i += 2 + len;
        }
        return o.ToArray();
    }

    static bool HasPrefix(byte[] d, int at, ReadOnlySpan<byte> p) =>
        at + p.Length <= d.Length && d.AsSpan(at, p.Length).SequenceEqual(p);

    /// <summary>Drops text, time and EXIF chunks from a PNG.</summary>
    public static byte[] StripPng(byte[] d)
    {
        ReadOnlySpan<byte> sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (d.Length < 8 || !d.AsSpan(0, 8).SequenceEqual(sig)) throw new EngineException("Not a PNG file");
        using var o = new MemoryStream(d.Length);
        o.Write(d, 0, 8);
        int i = 8;
        while (i + 12 <= d.Length)
        {
            int len = (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(i));
            if (len < 0 || i + 12 + len > d.Length) throw new EngineException("Damaged PNG file");
            var type = System.Text.Encoding.ASCII.GetString(d, i + 4, 4);
            bool drop = type is "tEXt" or "zTXt" or "iTXt" or "tIME" or "eXIf";
            if (!drop) o.Write(d, i, 12 + len);
            i += 12 + len;
            if (type == "IEND") break;
        }
        return o.ToArray();
    }
}

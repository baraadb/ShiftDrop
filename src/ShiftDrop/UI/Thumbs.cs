using System.Runtime.InteropServices;
using ShiftDrop.Core;
using ShiftDrop.Native;

namespace ShiftDrop.UI;

/// <summary>
/// Explorer's own thumbnail (or icon) for a file, as a GDI+ bitmap, fetched off the UI thread.
/// </summary>
public static unsafe partial class Thumbs
{
    static readonly Dictionary<string, (IntPtr Bmp, IntPtr Mem)> Cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly Queue<string> Order = new();
    const int Keep = 12;

    /// <summary>A cached bitmap, or null while it's being made (then <paramref name="ready"/> runs on the UI thread).</summary>
    public static IntPtr Get(string path, int size, Action ready)
    {
        var key = $"{path}|{size}";
        lock (Cache)
            if (Cache.TryGetValue(key, out var hit)) return hit.Bmp;
        var t = new Thread(() =>
        {
            var bmp = Load(path, size);
            App.Post(() =>
            {
                lock (Cache)
                {
                    if (Cache.ContainsKey(key)) { Free(bmp); return; }
                    Cache[key] = bmp;
                    Order.Enqueue(key);
                    while (Order.Count > Keep && Order.TryDequeue(out var old))
                        if (Cache.Remove(old, out var o)) Free(o);
                }
                if (bmp.Bmp != IntPtr.Zero) ready();
            });
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return IntPtr.Zero;
    }

    /// <summary>Loads a thumbnail right away (preview renderer only).</summary>
    public static void Preload(string path, int size)
    {
        var bmp = Load(path, size);
        lock (Cache) Cache[$"{path}|{size}"] = bmp;
    }

    static void Free((IntPtr Bmp, IntPtr Mem) t)
    {
        if (t.Bmp != IntPtr.Zero) Gdip.GdipDisposeImage(t.Bmp);
        if (t.Mem != IntPtr.Zero) Marshal.FreeHGlobal(t.Mem);
    }

    static (IntPtr Bmp, IntPtr Mem) Load(string path, int size)
    {
        IntPtr factory = IntPtr.Zero, hbm = IntPtr.Zero;
        try
        {
            CoInitializeEx(IntPtr.Zero, 2 /*COINIT_APARTMENTTHREADED*/);
            var iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"); // IShellItemImageFactory
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, in iid, out factory) != 0 || factory == IntPtr.Zero) return default;
            var vtbl = *(IntPtr**)factory;
            var getImage = (delegate* unmanaged[Stdcall]<IntPtr, SIZE, int, IntPtr*, int>)vtbl[3];
            if (getImage(factory, new SIZE(size, size), 0x1 /*BIGGERSIZEOK*/, &hbm) != 0 || hbm == IntPtr.Zero) return default;
            return ToGdip(hbm);
        }
        catch (Exception e) { Log.Write("thumbnail: " + e.Message); return default; }
        finally
        {
            if (hbm != IntPtr.Zero) W.DeleteObject(hbm);
            if (factory != IntPtr.Zero) Marshal.Release(factory);
            CoUninitialize();
        }
    }

    /// <summary>Copies a 32-bit HBITMAP (premultiplied alpha, or none) into a GDI+ PARGB bitmap that owns its own memory.</summary>
    static (IntPtr Bmp, IntPtr Mem) ToGdip(IntPtr hbm)
    {
        BITMAP info;
        if (GetObjectW(hbm, sizeof(BITMAP), &info) == 0 || info.Width <= 0) return default;
        int w = info.Width, h = Math.Abs(info.Height);
        var bmi = new BITMAPINFOHEADER { Size = 40, Width = w, Height = -h, Planes = 1, BitCount = 32 };
        var pixels = new byte[w * h * 4];
        var dc = W.GetDC(IntPtr.Zero);
        try { fixed (byte* p = pixels) if (GetDIBits(dc, hbm, 0, (uint)h, p, ref bmi, 0) == 0) return default; }
        finally { W.ReleaseDC(IntPtr.Zero, dc); }
        bool anyAlpha = false;
        for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) { anyAlpha = true; break; }
        if (!anyAlpha) for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        // GDI+ reads the pixels from this memory for as long as the bitmap lives
        var mem = Marshal.AllocHGlobal(pixels.Length);
        Marshal.Copy(pixels, 0, mem, pixels.Length);
        if (Gdip.GdipCreateBitmapFromScan0(w, h, w * 4, Gdip.PixelFormat32bppPARGB, mem, out var bmp) != 0)
        {
            Marshal.FreeHGlobal(mem);
            return default;
        }
        return (bmp, mem);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAP { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public IntPtr Bits; }

    [LibraryImport("ole32.dll")] private static partial int CoInitializeEx(IntPtr reserved, uint init);
    [LibraryImport("ole32.dll")] private static partial void CoUninitialize();
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHCreateItemFromParsingName(string path, IntPtr bindCtx, in Guid iid, out IntPtr item);
    [LibraryImport("gdi32.dll")] private static partial int GetObjectW(IntPtr obj, int size, void* buffer);
    [LibraryImport("gdi32.dll")] private static partial int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, void* bits, ref BITMAPINFOHEADER info, uint usage);
}

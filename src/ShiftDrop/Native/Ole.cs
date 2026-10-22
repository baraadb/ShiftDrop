using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace ShiftDrop.Native;

[StructLayout(LayoutKind.Sequential)] public struct POINTL { public int X, Y; }

[StructLayout(LayoutKind.Sequential)]
public struct FORMATETC { public ushort Format; public IntPtr Ptd; public uint Aspect; public int Index; public uint Tymed; }

[StructLayout(LayoutKind.Sequential)]
public struct STGMEDIUM { public uint Tymed; public IntPtr Handle; public IntPtr UnkForRelease; }

/// <summary>The OLE drop-target interface, implemented by the wheel. Data objects arrive as raw pointers.</summary>
[GeneratedComInterface]
[Guid("00000122-0000-0000-C000-000000000046")]
public partial interface IDropTarget
{
    [PreserveSig] int DragEnter(nint dataObject, uint keyState, POINTL pt, ref uint effect);
    [PreserveSig] int DragOver(uint keyState, POINTL pt, ref uint effect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int Drop(nint dataObject, uint keyState, POINTL pt, ref uint effect);
}

public interface IDropHandler
{
    void DragEnter(IReadOnlyList<string> files, POINT pt, uint keys);
    bool DragOver(POINT pt, uint keys);
    void DragLeave();
    bool Drop(POINT pt, uint keys);
}

[GeneratedComClass]
public sealed partial class DropTarget(IntPtr hwnd, IDropHandler handler) : IDropTarget
{
    public const uint DROPEFFECT_NONE = 0, DROPEFFECT_COPY = 1;
    public const uint MK_LBUTTON = 1, MK_RBUTTON = 2, MK_SHIFT = 4, MK_CONTROL = 8, MK_ALT = 0x20;
    bool _hasFiles;

    public int DragEnter(nint dataObject, uint keyState, POINTL pt, ref uint effect)
    {
        try
        {
            var files = ReadFiles(dataObject);
            _hasFiles = files.Count > 0;
            handler.DragEnter(files, new POINT(pt.X, pt.Y), keyState);
            effect = _hasFiles && handler.DragOver(new POINT(pt.X, pt.Y), keyState) ? (effect & DROPEFFECT_COPY) : DROPEFFECT_NONE;
            DragImage.Enter(hwnd, dataObject, new POINT(pt.X, pt.Y), effect);
        }
        catch (Exception e) { Core.Log.Write("DragEnter: " + e); effect = DROPEFFECT_NONE; }
        return 0;
    }

    public int DragOver(uint keyState, POINTL pt, ref uint effect)
    {
        try
        {
            effect = _hasFiles && handler.DragOver(new POINT(pt.X, pt.Y), keyState) ? (effect & DROPEFFECT_COPY) : DROPEFFECT_NONE;
            DragImage.Over(new POINT(pt.X, pt.Y), effect);
        }
        catch (Exception e) { Core.Log.Write("DragOver: " + e); effect = DROPEFFECT_NONE; }
        return 0;
    }

    public int DragLeave()
    {
        try { DragImage.Leave(); handler.DragLeave(); } catch (Exception e) { Core.Log.Write("DragLeave: " + e); }
        return 0;
    }

    public int Drop(nint dataObject, uint keyState, POINTL pt, ref uint effect)
    {
        try
        {
            effect = _hasFiles && handler.Drop(new POINT(pt.X, pt.Y), keyState) ? (effect & DROPEFFECT_COPY) : DROPEFFECT_NONE;
            DragImage.Drop(dataObject, new POINT(pt.X, pt.Y), effect);
        }
        catch (Exception e) { Core.Log.Write("Drop: " + e); effect = DROPEFFECT_NONE; }
        return 0;
    }

    /// <summary>Reads CF_HDROP through IDataObject::GetData (vtable slot 3) without keeping a reference.</summary>
    static unsafe List<string> ReadFiles(nint dataObject)
    {
        var list = new List<string>();
        if (dataObject == 0) return list;
        var fmt = new FORMATETC { Format = 15 /*CF_HDROP*/, Aspect = 1, Index = -1, Tymed = 1 /*TYMED_HGLOBAL*/ };
        var medium = default(STGMEDIUM);
        var vtbl = *(void***)dataObject;
        var getData = (delegate* unmanaged[Stdcall]<nint, FORMATETC*, STGMEDIUM*, int>)vtbl[3];
        if (getData(dataObject, &fmt, &medium) != 0) return list;
        try
        {
            uint count = W.DragQueryFileW(medium.Handle, 0xFFFFFFFF, null, 0);
            for (uint i = 0; i < count && i < 5000; i++)
            {
                uint len = W.DragQueryFileW(medium.Handle, i, null, 0);
                var buf = new char[len + 1];
                fixed (char* b = buf)
                {
                    uint got = W.DragQueryFileW(medium.Handle, i, b, len + 1);
                    if (got > 0) list.Add(new string(b, 0, (int)got));
                }
            }
        }
        finally { W.ReleaseStgMedium(&medium); }
        return list;
    }

    /// <summary>Registers this target for a window. Keeps the COM wrapper alive for the app's lifetime.</summary>
    public static IntPtr Register(IntPtr hwnd, DropTarget target)
    {
        DragImage.Init();
        var cw = new StrategyBasedComWrappers();
        var unknown = cw.GetOrCreateComInterfaceForObject(target, CreateComInterfaceFlags.None);
        var iid = typeof(IDropTarget).GUID;
        Marshal.QueryInterface(unknown, in iid, out var pTarget);
        int hr = W.RegisterDragDrop(hwnd, pTarget);
        if (hr != 0) Core.Log.Write($"RegisterDragDrop failed: 0x{hr:X8}");
        return pTarget;
    }
}

/// <summary>
/// The shell's drag-image helper: keeps Explorer's thumbnail of the dragged files drawn on top of
/// the wheel. All calls go through the raw vtable, so no references are kept.
/// </summary>
static unsafe class DragImage
{
    static nint _helper;

    /// <summary>
    /// Off: Explorer's drag thumbnail is hidden while the pointer is over the wheel, so it can't cover
    /// the hub; the wheel shows its own preview of the file there instead.
    /// </summary>
    const bool ShowOverWheel = false;

    public static void Init()
    {
        if (!ShowOverWheel || _helper != 0) return;
        var clsid = new Guid("4657278A-411B-11D2-839A-00C04FD918D0"); // CLSID_DragDropHelper
        var iid = new Guid("4657278B-411B-11D2-839A-00C04FD918D0");   // IID_IDropTargetHelper
        if (W.CoCreateInstance(in clsid, 0, 1 /*CLSCTX_INPROC_SERVER*/, in iid, out var p) == 0) _helper = p;
    }

    static void** Vtbl => *(void***)_helper;

    public static void Enter(IntPtr hwnd, nint data, POINT pt, uint effect)
    {
        if (_helper == 0 || data == 0) return;
        ((delegate* unmanaged[Stdcall]<nint, IntPtr, nint, POINT*, uint, int>)Vtbl[3])(_helper, hwnd, data, &pt, effect);
    }

    public static void Leave()
    {
        if (_helper == 0) return;
        ((delegate* unmanaged[Stdcall]<nint, int>)Vtbl[4])(_helper);
    }

    public static void Over(POINT pt, uint effect)
    {
        if (_helper == 0) return;
        ((delegate* unmanaged[Stdcall]<nint, POINT*, uint, int>)Vtbl[5])(_helper, &pt, effect);
    }

    public static void Drop(nint data, POINT pt, uint effect)
    {
        if (_helper == 0 || data == 0) return;
        ((delegate* unmanaged[Stdcall]<nint, nint, POINT*, uint, int>)Vtbl[6])(_helper, data, &pt, effect);
    }
}

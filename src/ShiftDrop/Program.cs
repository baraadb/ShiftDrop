using ShiftDrop.Native;

namespace ShiftDrop;

public static unsafe class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--preview") return Preview.Run(Path.GetFullPath(args[1]));
        var files = args.Where(a => !a.StartsWith("--") && (File.Exists(a) || Directory.Exists(a))).Select(Path.GetFullPath).ToList();
        using var mutex = new Mutex(true, @"Local\ShiftDrop.Running", out bool first);
        if (!first)
        {
            // already running: hand the files (or the request) to the running copy
            nint kind = args.Contains("--quit") ? App.CopyQuit : files.Count > 0 ? App.CopyFiles : App.CopyShowTip;
            Forward(kind, string.Join('\n', files));
            return 0;
        }
        if (args.Contains("--quit")) return 0;
        try
        {
            return App.Run(files);
        }
        catch (Exception e)
        {
            Core.Log.Write("fatal: " + e);
            W.MessageBoxW(IntPtr.Zero, e.Message, "ShiftDrop", 0x10);
            return 1;
        }
    }

    static void Forward(nint kind, string text)
    {
        for (int i = 0; i < 20; i++)
        {
            var hwnd = W.FindWindowW(App.MainClass, null);
            if (hwnd != IntPtr.Zero)
            {
                W.AllowSetForegroundWindow(W.ASFW_ANY);
                fixed (char* p = text)
                {
                    var cds = new COPYDATASTRUCT { Data = kind, CbData = (uint)(text.Length * 2), LpData = (IntPtr)p };
                    W.SendMessageW(hwnd, W.WM_COPYDATA, 0, (IntPtr)(&cds));
                }
                return;
            }
            Thread.Sleep(100); // the first copy may still be starting
        }
    }
}

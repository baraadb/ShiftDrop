using System.Diagnostics;
using System.Runtime.InteropServices;
using ShiftDrop.Core;
using ShiftDrop.Native;

namespace ShiftDrop;

/// <summary>The file actions on the third wheel (Alt). They work for any file or folder.</summary>
public static unsafe class AppActions
{
    static readonly FileKind[] Any = Enum.GetValues<FileKind>();

    public static void Register(App app)
    {
        Catalog.Add(Action("act.copypath", "Copy path", "نسخ المسار", files =>
        {
            W.SetClipboardText(app.Hwnd, string.Join(Environment.NewLine, files));
            app.Hud.Info(L.T("Copied", "تم النسخ"), files.Count == 1 ? files[0] : L.T($"{files.Count} paths", $"{files.Count} مسارات"), seconds: 3);
        }));
        Catalog.Add(Action("act.terminal", "Terminal here", "الطرفية هنا", files => OpenTerminal(FolderOf(files[0]))));
        Catalog.Add(Action("act.copyto", "Copy to…", "نسخ إلى…", files => Transfer(app, files, move: false)));
        Catalog.Add(Action("act.moveto", "Move to…", "نقل إلى…", files => Transfer(app, files, move: true)));
        Catalog.Add(Action("act.properties", "Properties", "الخصائص", files => Properties(files[0])));
    }

    static Operation Action(string id, string en, string ar, Action<IReadOnlyList<string>> run) => new()
    {
        Id = id, Group = OpGroup.Action, En = en, Ar = ar, Kinds = Any,
        Immediate = run, Run = _ => Task.CompletedTask,
    };

    static string FolderOf(string path) =>
        Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    static void OpenTerminal(string dir)
    {
        try
        {
            // Windows Terminal when installed, else PowerShell
            var wt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
            if (File.Exists(wt))
            {
                var psi = new ProcessStartInfo(wt) { UseShellExecute = false, WorkingDirectory = dir };
                psi.ArgumentList.Add("-d");
                psi.ArgumentList.Add(dir);
                Process.Start(psi);
                return;
            }
        }
        catch (Exception e) { Log.Write("wt: " + e.Message); }
        Process.Start(new ProcessStartInfo("powershell.exe") { UseShellExecute = true, WorkingDirectory = dir });
    }

    static void Properties(string path)
    {
        fixed (char* verb = "properties")
        fixed (char* file = path)
        {
            var info = new SHELLEXECUTEINFOW
            {
                CbSize = (uint)sizeof(SHELLEXECUTEINFOW),
                Mask = 0x0000000C, // SEE_MASK_INVOKEIDLIST
                Verb = (IntPtr)verb,
                File = (IntPtr)file,
                Show = W.SW_SHOW,
            };
            W.ShellExecuteExW(ref info);
        }
    }

    public static string? PickFolder(string title)
    {
        var display = Marshal.AllocHGlobal(520 * 2);
        try
        {
            fixed (char* t = title)
            {
                var bi = new BROWSEINFOW
                {
                    Title = (IntPtr)t,
                    DisplayName = display,
                    Flags = 0x0001 | 0x0040, // BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE
                };
                var pidl = W.SHBrowseForFolderW(ref bi);
                if (pidl == IntPtr.Zero) return null;
                try
                {
                    char* buf = stackalloc char[1024];
                    return W.SHGetPathFromIDListW(pidl, buf) != 0 ? new string(buf) : null;
                }
                finally { W.CoTaskMemFree(pidl); }
            }
        }
        finally { Marshal.FreeHGlobal(display); }
    }

    static void Transfer(App app, IReadOnlyList<string> files, bool move)
    {
        var dest = PickFolder(move ? L.T("Move to", "نقل إلى") : L.T("Copy to", "نسخ إلى"));
        if (dest == null) return;
        Task.Run(() =>
        {
            int ok = 0; string? error = null;
            foreach (var f in files)
            {
                try
                {
                    var name = Path.GetFileName(f.TrimEnd('\\', '/'));
                    if (string.Equals(Path.GetFullPath(Path.GetDirectoryName(f) ?? ""), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase) && move)
                        continue; // already there
                    var target = FreeName(dest, name, Directory.Exists(f));
                    if (Directory.Exists(f))
                    {
                        if (move && SameVolume(f, dest)) Directory.Move(f, target);
                        else { CopyDir(f, target); if (move) Directory.Delete(f, true); }
                    }
                    else if (move) File.Move(f, target, overwrite: false);
                    else File.Copy(f, target, overwrite: false);
                    ok++;
                }
                catch (Exception e) { error = e.Message; Log.Write("transfer: " + e); }
            }
            App.Post(() =>
            {
                if (error != null) app.Hud.Info(L.T("Some files were not moved", "لم تُنقل بعض الملفات"), error, error: true);
                else app.Hud.Info(move ? L.T("Moved", "تم النقل") : L.T("Copied", "تم النسخ"),
                    L.T($"{ok} item(s) to ", $"{ok} عنصر إلى ") + dest, seconds: 5);
            });
        });
    }

    static bool SameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var d in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(dst, Path.GetRelativePath(src, d)));
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(f, Path.Combine(dst, Path.GetRelativePath(src, f)), overwrite: false);
    }

    static string FreeName(string dir, string name, bool isDir)
    {
        for (int n = 1; n < 1000; n++)
        {
            var candidate = n == 1 ? name
                : isDir ? $"{name} {n}"
                : $"{Path.GetFileNameWithoutExtension(name)} {n}{Path.GetExtension(name)}";
            var full = Path.Combine(dir, candidate);
            if (!File.Exists(full) && !Directory.Exists(full)) return full;
        }
        throw new IOException("No free name");
    }
}

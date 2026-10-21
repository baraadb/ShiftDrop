using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;

namespace ShiftDrop.Core.Engines;

public static class Archives
{
    /// <summary>Packs files and folders into a ZIP. Folders keep their name as the top entry.</summary>
    public static void Zip(IReadOnlyList<string> items, string output, CancellationToken ct, IProgress<double>? progress = null)
    {
        var files = Expand(items).ToList();
        long total = Math.Max(1, files.Sum(f => SafeLength(f.Full)));
        long done = 0;
        using var fs = new FileStream(output, FileMode.CreateNew);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (full, entryName, isDir) in files)
        {
            ct.ThrowIfCancellationRequested();
            if (isDir) { zip.CreateEntry(entryName.TrimEnd('/') + "/"); continue; }
            var e = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            e.LastWriteTime = File.GetLastWriteTime(full);
            using var src = File.OpenRead(full);
            using var dst = e.Open();
            src.CopyTo(dst);
            done += src.Length;
            progress?.Report((double)done / total);
        }
    }

    public static void Tar(IReadOnlyList<string> items, string output, bool gzip, CancellationToken ct)
    {
        using var fs = new FileStream(output, FileMode.CreateNew);
        using Stream outer = gzip ? new GZipStream(fs, CompressionLevel.Optimal) : fs;
        using var w = new TarWriter(outer, TarEntryFormat.Pax, leaveOpen: false);
        foreach (var (full, entryName, isDir) in Expand(items))
        {
            ct.ThrowIfCancellationRequested();
            if (isDir) { w.WriteEntry(new PaxTarEntry(TarEntryType.Directory, entryName.TrimEnd('/') + "/")); continue; }
            w.WriteEntry(full, entryName);
        }
    }

    static long SafeLength(string f) { try { return new FileInfo(f).Length; } catch { return 0; } }

    static IEnumerable<(string Full, string Entry, bool IsDir)> Expand(IReadOnlyList<string> items)
    {
        foreach (var item in items)
        {
            if (Directory.Exists(item))
            {
                var root = Path.GetDirectoryName(Path.GetFullPath(item).TrimEnd('\\', '/')) ?? "";
                yield return (item, Rel(root, item), true);
                foreach (var d in Directory.EnumerateDirectories(item, "*", SearchOption.AllDirectories))
                    yield return (d, Rel(root, d), true);
                foreach (var f in Directory.EnumerateFiles(item, "*", SearchOption.AllDirectories))
                    yield return (f, Rel(root, f), false);
            }
            else yield return (item, Path.GetFileName(item), false);
        }
    }

    static string Rel(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    /// <summary>Extracts into <paramref name="destDir"/> (created). Entries that escape the folder are refused by .NET.</summary>
    public static void Extract(string archive, string destDir, string? sevenZip, CancellationToken ct)
    {
        Directory.CreateDirectory(destDir);
        switch (FileKinds.Ext(archive))
        {
            case "zip":
                ZipFile.ExtractToDirectory(archive, destDir, overwriteFiles: false);
                break;
            case "tar":
                TarFile.ExtractToDirectory(archive, destDir, overwriteFiles: false);
                break;
            case "tgz":
                using (var fs = File.OpenRead(archive))
                using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                    TarFile.ExtractToDirectory(gz, destDir, overwriteFiles: false);
                break;
            case "gz":
                using (var fs = File.OpenRead(archive))
                using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                using (var outFs = new FileStream(Path.Combine(destDir, FileKinds.BaseName(archive)), FileMode.CreateNew))
                    gz.CopyTo(outFs);
                break;
            default:
                if (sevenZip == null || !File.Exists(sevenZip))
                    throw new EngineException(L.T("Install 7-Zip to open RAR and 7z archives", "ثبّت برنامج 7-Zip لفتح ملفات RAR و7z"));
                Run7z(sevenZip, archive, destDir, ct);
                break;
        }
        // reject anything that escaped (defensive; the .NET extractors already refuse it)
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destDir)) + Path.DirectorySeparatorChar;
        foreach (var f in Directory.EnumerateFileSystemEntries(destDir, "*", SearchOption.AllDirectories))
            if (!Path.GetFullPath(f).StartsWith(full, StringComparison.OrdinalIgnoreCase))
                throw new EngineException("Unsafe path in archive");
    }

    static void Run7z(string exe, string archive, string dest, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "x", "-y", "-aos", "-o" + dest, "--", archive }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
        p.StandardOutput.ReadToEnd();
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        ct.ThrowIfCancellationRequested();
        if (p.ExitCode > 1) throw new EngineException(string.IsNullOrWhiteSpace(err) ? "7-Zip could not extract the archive" : err.Trim());
    }

    /// <summary>The single top-level folder inside an extraction, if that's all there is.</summary>
    public static string[] TopLevel(string dir) => Directory.GetFileSystemEntries(dir);
}

namespace ShiftDrop.Core;

public enum JobState { Queued, Running, Done, Failed, Canceled }

public sealed class Job
{
    static int _next;
    public int Id { get; } = Interlocked.Increment(ref _next);
    public required Operation Op { get; init; }
    public required IReadOnlyList<string> Inputs { get; init; }
    public IReadOnlyDictionary<string, string> Options { get; init; } = new Dictionary<string, string>();

    public string Title => Op.Title(Inputs.Count);
    public string Subtitle => Inputs.Count == 1
        ? Path.GetFileName(Inputs[0].TrimEnd('\\', '/'))
        : L.T($"{Inputs.Count} files", $"{Inputs.Count} ملفات");

    /// <summary>0..1, or negative while unknown.</summary>
    public double Progress { get; internal set; } = -1;
    public JobState State { get; internal set; } = JobState.Queued;
    public string? Error { get; internal set; }
    public List<string> Outputs { get; } = [];
    public DateTime FinishedAt { get; internal set; }

    internal readonly CancellationTokenSource Cts = new();
    public void Cancel() { try { Cts.Cancel(); } catch { } }
    public bool Finished => State is JobState.Done or JobState.Failed or JobState.Canceled;
}

/// <summary>
/// Runs jobs in the background. Outputs are written to a hidden staging folder next to the
/// destination, checked, then moved in under a free name: nothing is ever overwritten, and a
/// failed or cancelled job leaves nothing behind.
/// </summary>
public sealed class JobQueue(Services services)
{
    readonly SemaphoreSlim _heavy = new(1);
    readonly SemaphoreSlim _light = new(2);

    /// <summary>Raised from worker threads whenever a job changes.</summary>
    public event Action<Job>? Changed;

    /// <summary>Where outputs go for a given first input. Default: next to it.</summary>
    public Func<string, string> DestinationFor { get; set; } = input =>
        Path.GetDirectoryName(Path.GetFullPath(input.TrimEnd('\\', '/'))) ?? Environment.CurrentDirectory;

    /// <summary>Used when the source folder can't be written to.</summary>
    public Func<string> Fallback { get; set; } = () =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public Job Enqueue(Operation op, IReadOnlyList<string> inputs, IReadOnlyDictionary<string, string>? options = null)
    {
        var job = new Job { Op = op, Inputs = inputs, Options = options ?? new Dictionary<string, string>() };
        Changed?.Invoke(job);
        _ = Task.Run(() => RunAsync(job));
        return job;
    }

    /// <summary>Runs a job to the end on the calling task (used by tests).</summary>
    public async Task<Job> RunNowAsync(Operation op, IReadOnlyList<string> inputs, IReadOnlyDictionary<string, string>? options = null)
    {
        var job = new Job { Op = op, Inputs = inputs, Options = options ?? new Dictionary<string, string>() };
        await RunAsync(job);
        return job;
    }

    async Task RunAsync(Job job)
    {
        var lane = job.Op.Heavy ? _heavy : _light;
        try { await lane.WaitAsync(job.Cts.Token); }
        catch (OperationCanceledException) { Finish(job, JobState.Canceled); return; }
        try
        {
            job.State = JobState.Running;
            job.Progress = -1;
            Changed?.Invoke(job);
            List<IReadOnlyList<string>> units = job.Op.Combine ? [job.Inputs] : [.. job.Inputs.Select(i => (IReadOnlyList<string>)new[] { i })];
            var errors = new List<string>();
            for (int u = 0; u < units.Count; u++)
            {
                if (job.Cts.IsCancellationRequested) break;
                try
                {
                    await RunUnit(job, units[u], u, units.Count);
                }
                catch (OperationCanceledException) when (job.Cts.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    var msg = ex is EngineException ? ex.Message : ex.GetType().Name + ": " + ex.Message;
                    errors.Add(units.Count > 1 ? $"{Path.GetFileName(units[u][0])}: {msg}" : msg);
                    Log.Write($"job {job.Op.Id} failed on {units[u][0]}: {ex}");
                }
            }
            if (job.Cts.IsCancellationRequested) Finish(job, JobState.Canceled);
            else if (errors.Count > 0)
            {
                job.Error = errors.Count == 1 ? errors[0]
                    : L.T($"{errors.Count} of {units.Count} failed. ", $"فشل {errors.Count} من {units.Count}. ") + errors[0];
                Finish(job, JobState.Failed);
            }
            else Finish(job, JobState.Done);
        }
        finally { lane.Release(); }
    }

    void Finish(Job job, JobState state)
    {
        job.State = state;
        job.FinishedAt = DateTime.Now;
        if (state == JobState.Done) job.Progress = 1;
        Changed?.Invoke(job);
    }

    async Task RunUnit(Job job, IReadOnlyList<string> inputs, int index, int count)
    {
        var stamps = inputs.Select(Stamp.Of).ToList();
        var dest = DestinationFor(inputs[0]);
        string staging;
        try { staging = MakeStaging(dest); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            dest = Fallback();
            Directory.CreateDirectory(dest);
            staging = MakeStaging(dest);
        }
        var outDir = Path.Combine(staging, "out");
        var tmpDir = Path.Combine(staging, "tmp");
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(tmpDir);
        try
        {
            var reporter = new SyncProgress(p =>
            {
                job.Progress = (index + Math.Clamp(p, 0, 1)) / count;
                Changed?.Invoke(job);
            });
            // SVG pictures become PNG first, so every picture tool works on them
            var work = inputs;
            if (inputs.Any(Engines.Svg.IsSvg))
            {
                var list = new List<string>();
                foreach (var i in inputs)
                    list.Add(Engines.Svg.IsSvg(i) ? await Engines.Svg.ToPngAsync(services.ResvgPath, i, tmpDir, job.Cts.Token) : i);
                work = list;
            }
            var ctx = new OpContext
            {
                Inputs = work, OutDir = outDir, TempDir = tmpDir, Services = services,
                Progress = reporter, Token = job.Cts.Token, Options = job.Options,
            };
            await job.Op.Run(ctx);
            job.Cts.Token.ThrowIfCancellationRequested();

            for (int i = 0; i < inputs.Count; i++)
                if (!stamps[i].Same(Stamp.Of(inputs[i])))
                    throw new EngineException(L.T("The file changed while it was being processed; nothing was saved",
                                                  "تغيّر الملف أثناء المعالجة، فلم يُحفظ شيء"));

            var produced = Directory.GetFileSystemEntries(outDir);
            if (produced.Length == 0) throw new EngineException(L.T("Nothing was produced", "لم يُنتج أي ملف"));
            foreach (var p in produced)
                if (File.Exists(p) && new FileInfo(p).Length == 0)
                    throw new EngineException(L.T("The result was empty", "النتيجة فارغة"));
            foreach (var p in produced) job.Outputs.Add(Commit(p, dest));
            job.Progress = (index + 1.0) / count;
            Changed?.Invoke(job);
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { }
        }
    }

    static string MakeStaging(string dest)
    {
        var dir = Path.Combine(dest, ".shiftdrop-" + Guid.NewGuid().ToString("N")[..8]);
        var info = Directory.CreateDirectory(dir);
        try { info.Attributes |= FileAttributes.Hidden; } catch { }
        return dir;
    }

    /// <summary>Moves a finished file or folder in under a name nobody uses ("photo.jpg", "photo 2.jpg", ...).</summary>
    public static string Commit(string source, string destDir)
    {
        bool isDir = Directory.Exists(source);
        var name = Path.GetFileName(source);
        for (int n = 1; n < 1000; n++)
        {
            var target = Path.Combine(destDir, n == 1 ? name : Numbered(name, n, isDir));
            if (File.Exists(target) || Directory.Exists(target)) continue;
            try
            {
                if (isDir) Directory.Move(source, target);
                else File.Move(source, target, overwrite: false);
                return target;
            }
            catch (IOException) when (File.Exists(target) || Directory.Exists(target))
            {
                // someone took the name between the check and the move: try the next one
            }
        }
        throw new EngineException("No free file name");
    }

    static string Numbered(string name, int n, bool isDir)
    {
        if (isDir) return $"{name} {n}";
        if (name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) return $"{name[..^7]} {n}.tar.gz";
        var ext = Path.GetExtension(name);
        return $"{Path.GetFileNameWithoutExtension(name)} {n}{ext}";
    }

    sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    readonly record struct Stamp(long Length, DateTime Written, bool Exists)
    {
        public static Stamp Of(string path)
        {
            try
            {
                if (Directory.Exists(path)) return new Stamp(-1, DateTime.MinValue, true);
                var f = new FileInfo(path);
                return f.Exists ? new Stamp(f.Length, f.LastWriteTimeUtc, true) : new Stamp(0, default, false);
            }
            catch { return default; }
        }
        public bool Same(Stamp other) => this == other;
    }
}

/// <summary>Tiny append-only log in %LOCALAPPDATA%\ShiftDrop\logs.</summary>
public static class Log
{
    static readonly object Gate = new();
    public static string? Folder { get; set; }

    public static void Write(string message)
    {
        if (Folder == null) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                var file = Path.Combine(Folder, "shiftdrop.log");
                if (File.Exists(file) && new FileInfo(file).Length > 2_000_000)
                    File.Move(file, Path.Combine(Folder, "shiftdrop.old.log"), true);
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}

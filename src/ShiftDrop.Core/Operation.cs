using ShiftDrop.Core.Engines;

namespace ShiftDrop.Core;

public enum OpGroup { Convert, Tool, Action }

[Flags]
public enum Needs { None = 0, FFmpeg = 1, Pdfium = 2, DocRenderer = 4, Background = 8 }

/// <summary>A value a tool asks for before it runs (trim times, page ranges...).</summary>
public sealed record InputField(string Key, string En, string Ar, string Default)
{
    public string Label => L.T(En, Ar);
}

/// <summary>What the engines can reach. The Windows app fills the optional parts.</summary>
public sealed class Services
{
    public required FFmpeg FFmpeg { get; init; }
    public IDocRenderer? DocRenderer { get; set; }
    public string? SevenZipPath { get; set; }

    public bool Has(Needs n) =>
        (!n.HasFlag(Needs.FFmpeg) || FFmpeg.Available) &&
        (!n.HasFlag(Needs.Pdfium) || Pdfium.Available) &&
        (!n.HasFlag(Needs.DocRenderer) || DocRenderer != null) &&
        (!n.HasFlag(Needs.Background) || Ops.MediaOps.BackgroundAvailable);
}

public sealed class OpContext
{
    /// <summary>Inputs for this run: one file, or all files for a combining tool.</summary>
    public required IReadOnlyList<string> Inputs { get; init; }
    /// <summary>Folder the engine writes finished outputs into (committed afterwards).</summary>
    public required string OutDir { get; init; }
    /// <summary>Scratch folder for intermediates; never committed.</summary>
    public required string TempDir { get; init; }
    public required Services Services { get; init; }
    public required IProgress<double> Progress { get; init; }
    public required CancellationToken Token { get; init; }
    public IReadOnlyDictionary<string, string> Options { get; init; } = new Dictionary<string, string>();

    public string Input => Inputs[0];
    public string Base => FileKinds.BaseName(Input);
    public string Out(string fileName) => Path.Combine(OutDir, Safe(fileName));
    public string Temp(string fileName) => Path.Combine(TempDir, fileName);
    public string Opt(string key, string fallback = "") =>
        Options.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;

    static string Safe(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }
}

public sealed class Operation
{
    public required string Id { get; init; }
    public required OpGroup Group { get; init; }
    /// <summary>Short label on the wheel (a format name, or a tool name).</summary>
    public required string En { get; init; }
    public required string Ar { get; init; }
    public required FileKind[] Kinds { get; init; }
    public required Func<OpContext, Task> Run { get; init; }

    /// <summary>For conversions: the extension produced (hidden when the input already has it).</summary>
    public string? TargetExt { get; init; }
    /// <summary>All inputs go into one run (merge, join, one PDF from images).</summary>
    public bool Combine { get; init; }
    public int MinInputs { get; init; } = 1;
    public int MaxInputs { get; init; } = int.MaxValue;
    public Needs Needs { get; init; }
    /// <summary>Heavy media work: runs one at a time.</summary>
    public bool Heavy { get; init; }
    public InputField[]? Fields { get; init; }
    /// <summary>Runs immediately on the UI thread, no job card (copy path, open terminal).</summary>
    public Action<IReadOnlyList<string>>? Immediate { get; init; }
    /// <summary>The app opens this editor first and passes what it returns as options ("crop").</summary>
    public string? Editor { get; init; }

    public string Label => L.T(En, Ar);

    public string Title(int count) => Group == OpGroup.Convert
        ? L.T($"Converting to {En}", $"تحويل إلى {En}")
        : Label;

    public override string ToString() => Id;
}

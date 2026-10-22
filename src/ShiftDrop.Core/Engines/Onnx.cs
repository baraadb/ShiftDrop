using System.Runtime.InteropServices;
using System.Text;

namespace ShiftDrop.Core.Engines;

/// <summary>
/// The few ONNX Runtime calls ShiftDrop needs, through its plain C API (MIT licensed,
/// bundled as onnxruntime.dll). No NuGet package: the OrtApi table is read directly.
/// </summary>
public static unsafe class Onnx
{
    // Positions in the OrtApi function table (append-only since 1.0, checked against 1.23 headers).
    const int GetErrorMessage = 2, CreateEnv = 3, CreateSession = 7, Run = 9, CreateSessionOptions = 10,
        SetGraphOptimizationLevel = 23, SetIntraOpNumThreads = 24, SessionGetInputName = 36, SessionGetOutputName = 37,
        CreateTensorWithDataAsOrtValue = 49, GetTensorMutableData = 51, GetDimensionsCount = 61, GetDimensions = 62,
        GetTensorTypeAndShape = 65, CreateCpuMemoryInfo = 69, AllocatorFree = 76, GetAllocatorWithDefaultOptions = 78,
        ReleaseEnv = 92, ReleaseStatus = 93, ReleaseMemoryInfo = 94, ReleaseSession = 95, ReleaseValue = 96,
        ReleaseTensorTypeAndShapeInfo = 99, ReleaseSessionOptions = 100;
    const uint ApiVersion = 16; // any onnxruntime 1.16 or newer

    public static readonly List<string> SearchDirs = [];
    static readonly object Gate = new();
    static IntPtr* _api;
    static IntPtr _env;
    static bool? _available;

    static string LibName => OperatingSystem.IsWindows() ? "onnxruntime.dll"
        : OperatingSystem.IsMacOS() ? "libonnxruntime.dylib" : "libonnxruntime.so";

    static IEnumerable<string> Dirs()
    {
        var env = Environment.GetEnvironmentVariable("SHIFTDROP_ONNX_DIR");
        if (!string.IsNullOrEmpty(env)) yield return env;
        foreach (var d in SearchDirs) yield return d;
        yield return Path.Combine(AppContext.BaseDirectory, "tools");
        yield return AppContext.BaseDirectory;
    }

    /// <summary>Finds a file next to the runtime (the background model lives there too).</summary>
    public static string? FindFile(string name) =>
        Dirs().Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);

    /// <summary>The runtime file is there (cheap; doesn't load it).</summary>
    public static bool Installed => FindFile(LibName) != null;

    public static bool Available
    {
        get
        {
            lock (Gate)
            {
                if (_available is bool b) return b;
                try { Init(); _available = true; }
                catch (Exception e) { Log.Write("onnx: " + e.Message); _available = false; }
                return _available.Value;
            }
        }
    }

    static void Init()
    {
        if (_api != null) return;
        var path = FindFile(LibName) ?? throw new EngineException("onnxruntime not found");
        var lib = NativeLibrary.Load(path);
        var getBase = (delegate* unmanaged<IntPtr*>)NativeLibrary.GetExport(lib, "OrtGetApiBase");
        var apiBase = getBase();
        var getApi = (delegate* unmanaged<uint, IntPtr*>)apiBase[0];
        var api = getApi(ApiVersion);
        if (api == null) throw new EngineException("onnxruntime is too old");
        _api = api;
        IntPtr env;
        fixed (byte* id = "ShiftDrop"u8)
            Check(((delegate* unmanaged<int, byte*, IntPtr*, IntPtr>)_api[CreateEnv])(3 /* errors only */, id, &env));
        _env = env;
    }

    static void Check(IntPtr status)
    {
        if (status == IntPtr.Zero) return;
        var msg = Marshal.PtrToStringUTF8(((delegate* unmanaged<IntPtr, IntPtr>)_api[GetErrorMessage])(status)) ?? "onnxruntime error";
        ((delegate* unmanaged<IntPtr, void>)_api[ReleaseStatus])(status);
        throw new EngineException(msg);
    }

    static void Release(int slot, IntPtr p)
    {
        if (p != IntPtr.Zero) ((delegate* unmanaged<IntPtr, void>)_api[slot])(p);
    }

    /// <summary>A loaded model with one float input and (at least) one float output.</summary>
    public sealed class Session : IDisposable
    {
        IntPtr _session;
        readonly string _input, _output;

        public Session(string modelPath)
        {
            if (!Available) throw new EngineException("onnxruntime is not available");
            IntPtr opts;
            Check(((delegate* unmanaged<IntPtr*, IntPtr>)_api[CreateSessionOptions])(&opts));
            try
            {
                Check(((delegate* unmanaged<IntPtr, int, IntPtr>)_api[SetIntraOpNumThreads])(opts, Math.Clamp(Environment.ProcessorCount / 2, 1, 8)));
                Check(((delegate* unmanaged<IntPtr, int, IntPtr>)_api[SetGraphOptimizationLevel])(opts, 99));
                IntPtr s;
                if (OperatingSystem.IsWindows())
                {
                    fixed (char* p = modelPath)
                        Check(((delegate* unmanaged<IntPtr, char*, IntPtr, IntPtr*, IntPtr>)_api[CreateSession])(_env, p, opts, &s));
                }
                else
                {
                    var bytes = Encoding.UTF8.GetBytes(modelPath + "\0");
                    fixed (byte* p = bytes)
                        Check(((delegate* unmanaged<IntPtr, byte*, IntPtr, IntPtr*, IntPtr>)_api[CreateSession])(_env, p, opts, &s));
                }
                _session = s;
            }
            finally { Release(ReleaseSessionOptions, opts); }
            _input = Name(SessionGetInputName, 0);
            _output = Name(SessionGetOutputName, 0);
        }

        string Name(int slot, nuint index)
        {
            IntPtr alloc, name;
            Check(((delegate* unmanaged<IntPtr*, IntPtr>)_api[GetAllocatorWithDefaultOptions])(&alloc));
            Check(((delegate* unmanaged<IntPtr, nuint, IntPtr, IntPtr*, IntPtr>)_api[slot])(_session, index, alloc, &name));
            try { return Marshal.PtrToStringUTF8(name) ?? ""; }
            finally { Check(((delegate* unmanaged<IntPtr, IntPtr, IntPtr>)_api[AllocatorFree])(alloc, name)); }
        }

        /// <summary>Runs the model on a float tensor and returns the first output with its shape.</summary>
        public (float[] Data, long[] Shape) Run(float[] input, long[] shape)
        {
            lock (this)
            {
                IntPtr mem, value = IntPtr.Zero, output = IntPtr.Zero;
                Check(((delegate* unmanaged<int, int, IntPtr*, IntPtr>)_api[CreateCpuMemoryInfo])(1 /* arena */, 0, &mem));
                try
                {
                    fixed (float* data = input)
                    fixed (long* dims = shape)
                    {
                        Check(((delegate* unmanaged<IntPtr, void*, nuint, long*, nuint, int, IntPtr*, IntPtr>)_api[CreateTensorWithDataAsOrtValue])(
                            mem, data, (nuint)(input.Length * sizeof(float)), dims, (nuint)shape.Length, 1 /* float */, &value));
                        var inName = Encoding.UTF8.GetBytes(_input + "\0");
                        var outName = Encoding.UTF8.GetBytes(_output + "\0");
                        fixed (byte* inN = inName)
                        fixed (byte* outN = outName)
                        {
                            byte* inNames = inN, outNames = outN;
                            IntPtr inValue = value;
                            Check(((delegate* unmanaged<IntPtr, IntPtr, byte**, IntPtr*, nuint, byte**, nuint, IntPtr*, IntPtr>)_api[Onnx.Run])(
                                _session, IntPtr.Zero, &inNames, &inValue, 1, &outNames, 1, &output));
                        }
                    }
                    IntPtr info;
                    Check(((delegate* unmanaged<IntPtr, IntPtr*, IntPtr>)_api[GetTensorTypeAndShape])(output, &info));
                    long[] outShape;
                    try
                    {
                        nuint count;
                        Check(((delegate* unmanaged<IntPtr, nuint*, IntPtr>)_api[GetDimensionsCount])(info, &count));
                        outShape = new long[(int)count];
                        fixed (long* o = outShape)
                            Check(((delegate* unmanaged<IntPtr, long*, nuint, IntPtr>)_api[GetDimensions])(info, o, count));
                    }
                    finally { Release(ReleaseTensorTypeAndShapeInfo, info); }
                    long total = 1;
                    foreach (var d in outShape) total *= Math.Max(1, d);
                    void* outData;
                    Check(((delegate* unmanaged<IntPtr, void**, IntPtr>)_api[GetTensorMutableData])(output, &outData));
                    var result = new float[total];
                    new ReadOnlySpan<float>(outData, (int)total).CopyTo(result);
                    return (result, outShape);
                }
                finally
                {
                    Release(ReleaseValue, output);
                    Release(ReleaseValue, value);
                    Release(ReleaseMemoryInfo, mem);
                }
            }
        }

        public void Dispose()
        {
            Release(ReleaseSession, _session);
            _session = IntPtr.Zero;
        }
    }
}

using System.Runtime.InteropServices;
using ShiftDrop.Core;
#if WINDOWS10_0_17763_0_OR_GREATER
using Windows.ApplicationModel;
#endif

namespace ShiftDrop;

/// <summary>
/// The Microsoft Store version runs inside an MSIX package: the Store updates it,
/// and "Start with Windows" goes through a startup task instead of the Run key.
/// </summary>
public static partial class Packaged
{
    public static bool IsPackaged { get; } = Detect();

    static unsafe bool Detect()
    {
        try
        {
            uint length = 0;
            // APPMODEL_ERROR_NO_PACKAGE (15700) means a normal, unpackaged install
            return GetCurrentPackageFullName(ref length, null) != 15700;
        }
        catch { return false; }
    }

    [LibraryImport("kernel32.dll")]
    private static unsafe partial int GetCurrentPackageFullName(ref uint length, char* name);

    const string TaskId = "ShiftDropStartup";

    /// <summary>Last known state of the startup task (read at start-up and after each change).</summary>
    public static bool StartupEnabled { get; private set; }

    public static async Task RefreshStartupAsync()
    {
#if WINDOWS10_0_17763_0_OR_GREATER
        if (!IsPackaged || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            StartupEnabled = task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
        catch (Exception e) { Log.Write("startup task: " + e.Message); }
#else
        await Task.CompletedTask;
#endif
    }

    public static async void SetStartup(bool on)
    {
#if WINDOWS10_0_17763_0_OR_GREATER
        if (!IsPackaged || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            if (on)
            {
                var state = await task.RequestEnableAsync();
                StartupEnabled = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
                if (state == StartupTaskState.DisabledByUser)
                    App.Post(() => App.Current?.Hud.Info(L.T("Start with Windows", "التشغيل مع Windows"),
                        L.T("Windows has ShiftDrop turned off in Settings > Apps > Startup. Turn it on there.",
                            "ShiftDrop موقوف من إعدادات Windows ← التطبيقات ← بدء التشغيل. فعّله من هناك."), seconds: 10));
            }
            else
            {
                task.Disable();
                StartupEnabled = false;
            }
        }
        catch (Exception e) { Log.Write("startup task: " + e.Message); }
#else
        await Task.CompletedTask;
#endif
    }
}

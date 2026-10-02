using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace NEXUS.Services;

public static class RepairService
{
    public static Task<int> RunAsync(bool componentStore)
    {
        // Fixed commands only. Model text never enters the command line.
        var start = new ProcessStartInfo(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), componentStore ? "dism.exe" : "sfc.exe"))
        {
            UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Normal,
            Arguments = componentStore ? "/Online /Cleanup-Image /RestoreHealth" : "/scannow"
        };
        return RunProcessAsync(start);
    }
    private static async Task<int> RunProcessAsync(ProcessStartInfo start)
    {
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Команда не запущена.");
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}

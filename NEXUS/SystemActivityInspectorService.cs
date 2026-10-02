using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace NEXUS
{
    public sealed class ServiceRuntimeInfo
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string State { get; set; } = "";
        public string StartMode { get; set; } = "";
        public int ProcessId { get; set; }
        public string Path { get; set; } = "";
        public long MemoryBytes { get; set; }
    }

    public sealed class NetworkConnectionInfo
    {
        public string State { get; set; } = "";
        public string LocalAddress { get; set; } = "";
        public int LocalPort { get; set; }
        public string RemoteAddress { get; set; } = "";
        public int RemotePort { get; set; }
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = "";
        public string ProcessPath { get; set; } = "";
    }

    public sealed class SystemActivityReport
    {
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<ServiceRuntimeInfo> Services { get; } = new();
        public List<NetworkConnectionInfo> Connections { get; } = new();

        public int RunningServices => Services.Count;
        public int AutomaticRunningServices => Services.Count(item =>
            string.Equals(item.StartMode, "Auto", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.StartMode, "Automatic", StringComparison.OrdinalIgnoreCase));
        public int EstablishedConnections => Connections.Count(item =>
            string.Equals(item.State, "Established", StringComparison.OrdinalIgnoreCase));
        public int ListeningConnections => Connections.Count(item =>
            string.Equals(item.State, "Listen", StringComparison.OrdinalIgnoreCase));
    }

    public sealed class SystemActivityInspectorService
    {
        public async Task<SystemActivityReport> RunAsync()
        {
            SystemActivityReport report = new();
            await ReadServicesAsync(report);
            await ReadNetworkAsync(report);
            return report;
        }

        private static async Task ReadServicesAsync(SystemActivityReport report)
        {
            const string script = @"
Get-CimInstance Win32_Service -ErrorAction SilentlyContinue |
Where-Object {$_.State -eq 'Running'} |
Select-Object Name,DisplayName,State,StartMode,ProcessId,PathName |
ConvertTo-Json -Compress";

            using JsonDocument? json = await RunPowerShellJsonAsync(script);
            if (json == null)
                return;

            Dictionary<int, long> memoryCache = new();

            foreach (JsonElement item in EnumerateObjects(json.RootElement))
            {
                int pid = TryGetInt(item, "ProcessId");
                long memory = 0;

                if (pid > 0)
                {
                    if (!memoryCache.TryGetValue(pid, out memory))
                    {
                        try
                        {
                            using Process process = Process.GetProcessById(pid);
                            memory = process.WorkingSet64;
                        }
                        catch
                        {
                            memory = 0;
                        }

                        memoryCache[pid] = memory;
                    }
                }

                report.Services.Add(new ServiceRuntimeInfo
                {
                    Name = TryGetString(item, "Name"),
                    DisplayName = TryGetString(item, "DisplayName"),
                    State = TryGetString(item, "State"),
                    StartMode = TryGetString(item, "StartMode"),
                    ProcessId = pid,
                    Path = TryGetString(item, "PathName"),
                    MemoryBytes = memory
                });
            }
        }

        private static async Task ReadNetworkAsync(SystemActivityReport report)
        {
            const string script = @"
Get-NetTCPConnection -ErrorAction SilentlyContinue |
Where-Object {$_.State -eq 'Established' -or $_.State -eq 'Listen'} |
Select-Object -First 500 State,LocalAddress,LocalPort,RemoteAddress,RemotePort,OwningProcess |
ConvertTo-Json -Compress";

            using JsonDocument? json = await RunPowerShellJsonAsync(script);
            if (json == null)
                return;

            Dictionary<int, (string Name, string Path)> processCache = new();

            foreach (JsonElement item in EnumerateObjects(json.RootElement))
            {
                int pid = TryGetInt(item, "OwningProcess");
                string processName = "unknown";
                string processPath = "";

                if (pid > 0)
                {
                    if (!processCache.TryGetValue(pid, out var processInfo))
                    {
                        try
                        {
                            using Process process = Process.GetProcessById(pid);
                            processName = process.ProcessName;
                            try
                            {
                                processPath = process.MainModule?.FileName ?? "";
                            }
                            catch
                            {
                            }
                        }
                        catch
                        {
                        }

                        processInfo = (processName, processPath);
                        processCache[pid] = processInfo;
                    }

                    processName = processInfo.Name;
                    processPath = processInfo.Path;
                }

                report.Connections.Add(new NetworkConnectionInfo
                {
                    State = TryGetString(item, "State"),
                    LocalAddress = TryGetString(item, "LocalAddress"),
                    LocalPort = TryGetInt(item, "LocalPort"),
                    RemoteAddress = TryGetString(item, "RemoteAddress"),
                    RemotePort = TryGetInt(item, "RemotePort"),
                    ProcessId = pid,
                    ProcessName = processName,
                    ProcessPath = processPath
                });
            }
        }

        private static async Task<JsonDocument?> RunPowerShellJsonAsync(string script)
        {
            try
            {
                ProcessStartInfo startInfo = new()
                {
                    FileName = "powershell.exe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-NonInteractive");
                startInfo.ArgumentList.Add("-Command");
                startInfo.ArgumentList.Add(script);

                using Process? process = Process.Start(startInfo);
                if (process == null)
                    return null;

                string output = await process.StandardOutput.ReadToEndAsync();
                await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (string.IsNullOrWhiteSpace(output))
                    return null;

                return JsonDocument.Parse(output.Trim());
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<JsonElement> EnumerateObjects(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in root.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                        yield return item;
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                yield return root;
            }
        }

        private static string TryGetString(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement value))
                return "";

            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : value.ToString();
        }

        private static int TryGetInt(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement value))
                return 0;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
                return number;

            return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out int parsed)
                ? parsed
                : 0;
        }
    }
}

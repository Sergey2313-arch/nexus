using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NEXUS
{
    public enum CompromiseRisk
    {
        Info,
        Low,
        Medium,
        High,
        Critical
    }

    public sealed class CompromiseFinding
    {
        public CompromiseRisk Risk { get; set; }
        public string Category { get; set; } = "";
        public string Title { get; set; } = "";
        public string Details { get; set; } = "";
        public string Path { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public string Recommendation { get; set; } = "";
    }

    public sealed class CompromiseScanReport
    {
        public DateTime StartedAt { get; set; }
        public DateTime FinishedAt { get; set; }
        public bool DeepScan { get; set; }
        public int Score { get; set; } = 100;
        public string Status { get; set; } = "NO OBVIOUS COMPROMISE";
        public int FilesScanned { get; set; }
        public int ExecutablesAndScriptsScanned { get; set; }
        public int SuspiciousFiles { get; set; }
        public int PersistenceEntries { get; set; }
        public int SuspiciousPersistenceEntries { get; set; }
        public int ScheduledTasks { get; set; }
        public int SuspiciousScheduledTasks { get; set; }
        public int AutomaticServices { get; set; }
        public int SuspiciousServices { get; set; }
        public int DefenderExclusions { get; set; }
        public int SuspiciousDefenderExclusions { get; set; }
        public int ListeningPorts { get; set; }
        public bool? RdpEnabled { get; set; }
        public bool HostsModified { get; set; }
        public List<CompromiseFinding> Findings { get; } = new();
    }

    public sealed class CompromiseScannerService
    {
        private static readonly HashSet<string> InterestingExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".scr", ".com", ".msi",
            ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf"
        };

        private static readonly string[] SystemLookalikeNames =
        {
            "svchost", "lsass", "csrss", "winlogon", "services",
            "smss", "taskhostw", "explorer", "conhost", "dwm"
        };

        private static readonly string[] SuspiciousScriptMarkers =
        {
            " -enc ", " -encodedcommand ", "frombase64string", "invoke-expression",
            "iex(", "downloadstring(", "invoke-webrequest", "start-bitstransfer",
            "regsvr32 /s /n /u /i:", "rundll32 javascript:", "mshta http"
        };

        public async Task<CompromiseScanReport> RunAsync(
            bool deepScan,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CompromiseScanReport report = new()
            {
                StartedAt = DateTime.Now,
                DeepScan = deepScan
            };

            progress?.Report("Проверка подозрительных файлов...");
            await Task.Run(() => ScanFiles(report, deepScan, progress, cancellationToken), cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Проверка автозагрузки и Startup...");
            ScanRegistryAutoruns(report);
            ScanStartupFolders(report);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Проверка Scheduled Tasks...");
            await ScanScheduledTasksAsync(report);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Проверка автоматических служб...");
            await ScanServicesAsync(report);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Проверка Microsoft Defender...");
            await ScanDefenderExclusionsAsync(report);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Проверка RDP, HOSTS и сетевых слушателей...");
            ScanRdp(report);
            ScanHosts(report);
            await ScanListeningPortsAsync(report);

            FinalizeReport(report);
            report.FinishedAt = DateTime.Now;
            progress?.Report("Проверка завершена.");
            return report;
        }

        private static void ScanFiles(
            CompromiseScanReport report,
            bool deepScan,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            List<string> roots = BuildScanRoots(deepScan);
            HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
            int maxFiles = deepScan ? 12000 : 3500;

            foreach (string root in roots)
            {
                if (report.FilesScanned >= maxFiles)
                    break;

                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    continue;

                progress?.Report($"Файлы: {ShortPath(root)}");

                foreach (string file in SafeEnumerateFiles(root, deepScan ? 6 : 3))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (report.FilesScanned >= maxFiles)
                        break;

                    if (!visited.Add(file))
                        continue;

                    report.FilesScanned++;

                    string extension;
                    try { extension = Path.GetExtension(file); }
                    catch { continue; }

                    if (!InterestingExtensions.Contains(extension))
                        continue;

                    report.ExecutablesAndScriptsScanned++;
                    EvaluateFile(file, report);
                }
            }
        }

        private static List<string> BuildScanRoots(bool deepScan)
        {
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);

            List<string> roots = new()
            {
                Path.GetTempPath(),
                Path.Combine(user, "Downloads"),
                desktop,
                startup,
                commonStartup,
                Path.Combine(localAppData, "Temp")
            };

            if (deepScan)
            {
                roots.Add(appData);
                roots.Add(localAppData);
                roots.Add(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Microsoft",
                    "Windows",
                    "Start Menu",
                    "Programs",
                    "Startup"));
            }

            return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static IEnumerable<string> SafeEnumerateFiles(string root, int maxDepth)
        {
            Stack<(string Path, int Depth)> pending = new();
            pending.Push((root, 0));

            while (pending.Count > 0)
            {
                var current = pending.Pop();
                string[] files = Array.Empty<string>();
                string[] directories = Array.Empty<string>();

                try { files = Directory.GetFiles(current.Path); }
                catch { }

                foreach (string file in files)
                    yield return file;

                if (current.Depth >= maxDepth)
                    continue;

                try { directories = Directory.GetDirectories(current.Path); }
                catch { }

                foreach (string directory in directories)
                {
                    try
                    {
                        FileAttributes attributes = File.GetAttributes(directory);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                            continue;
                    }
                    catch { continue; }

                    pending.Push((directory, current.Depth + 1));
                }
            }
        }

        private static void EvaluateFile(string file, CompromiseScanReport report)
        {
            int risk = 0;
            List<string> reasons = new();
            string lower = file.ToLowerInvariant();
            string extension = Path.GetExtension(file).ToLowerInvariant();
            string baseName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();

            bool inTemp = lower.Contains("\\temp\\") || lower.StartsWith(Path.GetTempPath().ToLowerInvariant());
            bool inDownloads = lower.Contains("\\downloads\\");
            bool inStartup = lower.Contains("\\start menu\\programs\\startup\\");
            bool inAppData = lower.Contains("\\appdata\\");

            if (inTemp && IsExecutableLike(extension))
            {
                risk += 25;
                reasons.Add("исполняемый файл находится во временной папке");
            }

            if (inStartup)
            {
                risk += 25;
                reasons.Add("файл находится в папке автозагрузки");
            }

            if (inDownloads && IsScript(extension))
            {
                risk += 12;
                reasons.Add("скрипт находится в Downloads");
            }

            if (inAppData && SystemLookalikeNames.Contains(baseName, StringComparer.OrdinalIgnoreCase))
            {
                risk += 35;
                reasons.Add("имя похоже на системный процесс Windows, но файл расположен вне Windows/System32");
            }

            try
            {
                FileInfo info = new(file);
                if (info.Exists)
                {
                    if (DateTime.Now - info.CreationTime < TimeSpan.FromDays(7))
                    {
                        risk += 8;
                        reasons.Add("файл появился недавно");
                    }

                    if ((info.Attributes & FileAttributes.Hidden) != 0)
                    {
                        risk += 8;
                        reasons.Add("файл имеет атрибут Hidden");
                    }

                    if ((info.Attributes & FileAttributes.System) != 0)
                    {
                        risk += 8;
                        reasons.Add("файл имеет атрибут System");
                    }
                }
            }
            catch { }

            string fileName = Path.GetFileName(file);
            if (HasSuspiciousDoubleExtension(fileName))
            {
                risk += 18;
                reasons.Add("имя содержит маскирующее двойное расширение");
            }

            if (IsScript(extension))
            {
                string? marker = FindSuspiciousScriptMarker(file);
                if (marker != null)
                {
                    risk += 30;
                    reasons.Add($"в скрипте найден подозрительный шаблон: {marker}");
                }
            }

            bool? hasSignature = null;
            if (IsExecutableLike(extension) && risk >= 20)
            {
                hasSignature = HasEmbeddedCertificate(file);
                if (hasSignature == false)
                {
                    risk += 12;
                    reasons.Add("Authenticode-сертификат не обнаружен");
                }
            }

            if (risk < 20)
                return;

            report.SuspiciousFiles++;
            string hash = TrySha256(file);
            CompromiseRisk level = RiskFromPoints(risk);

            report.Findings.Add(new CompromiseFinding
            {
                Risk = level,
                Category = "File",
                Title = $"Файл требует проверки: {Path.GetFileName(file)}",
                Details = string.Join("; ", reasons),
                Path = file,
                Sha256 = hash,
                Recommendation = level >= CompromiseRisk.High
                    ? "Не удаляйте файл вслепую. Проверьте происхождение, свойства/подпись и запустите проверку Microsoft Defender. Если файл вам неизвестен и связан с автозапуском или сетевой активностью, отключитесь от сети и выполните полную проверку."
                    : "Проверьте происхождение файла. Один эвристический признак сам по себе не доказывает заражение."
            });
        }

        private static void ScanRegistryAutoruns(CompromiseScanReport report)
        {
            var locations = new (RegistryKey Root, string Path, string Label)[]
            {
                (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU Run"),
                (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKCU RunOnce"),
                (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKLM Run"),
                (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKLM RunOnce")
            };

            foreach (var location in locations)
            {
                try
                {
                    using RegistryKey? key = location.Root.OpenSubKey(location.Path);
                    if (key == null)
                        continue;

                    foreach (string name in key.GetValueNames())
                    {
                        report.PersistenceEntries++;
                        string command = Environment.ExpandEnvironmentVariables(key.GetValue(name)?.ToString() ?? "");
                        int points = ScorePersistenceCommand(command, out string reason);

                        if (points < 20)
                            continue;

                        report.SuspiciousPersistenceEntries++;
                        report.Findings.Add(new CompromiseFinding
                        {
                            Risk = RiskFromPoints(points),
                            Category = "Persistence",
                            Title = $"Автозагрузка требует проверки: {name}",
                            Details = $"{location.Label}: {command}. {reason}",
                            Recommendation = "Проверьте издателя и назначение программы. Не удаляйте запись, пока не убедитесь, что она действительно нежелательная."
                        });
                    }
                }
                catch { }
            }
        }

        private static void ScanStartupFolders(CompromiseScanReport report)
        {
            foreach (string path in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
            })
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                    continue;

                string[] files;
                try { files = Directory.GetFiles(path); }
                catch { continue; }

                foreach (string file in files)
                {
                    report.PersistenceEntries++;

                    string extension = Path.GetExtension(file);
                    if (!InterestingExtensions.Contains(extension) &&
                        !extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                        continue;

                    report.Findings.Add(new CompromiseFinding
                    {
                        Risk = CompromiseRisk.Info,
                        Category = "Persistence",
                        Title = $"Startup: {Path.GetFileName(file)}",
                        Details = "Объект запускается при входе пользователя в Windows.",
                        Path = file,
                        Recommendation = "Если объект вам неизвестен, проверьте его свойства и целевой файл ярлыка."
                    });
                }
            }
        }

        private static async Task ScanScheduledTasksAsync(CompromiseScanReport report)
        {
            const string script =
                "$x=Get-ScheduledTask -ErrorAction SilentlyContinue | ForEach-Object { $t=$_; foreach($a in $t.Actions){[PSCustomObject]@{TaskPath=$t.TaskPath;TaskName=$t.TaskName;Execute=$a.Execute;Arguments=$a.Arguments}}}; $x | ConvertTo-Json -Compress";

            using JsonDocument? json = await RunPowerShellJsonAsync(script);
            if (json == null)
                return;

            foreach (JsonElement item in EnumerateObjects(json.RootElement))
            {
                report.ScheduledTasks++;
                string name = TryGetString(item, "TaskName");
                string execute = TryGetString(item, "Execute");
                string arguments = TryGetString(item, "Arguments");
                string command = $"{execute} {arguments}".Trim();
                int points = ScorePersistenceCommand(command, out string reason);

                if (points < 20)
                    continue;

                report.SuspiciousScheduledTasks++;
                report.Findings.Add(new CompromiseFinding
                {
                    Risk = RiskFromPoints(points + 10),
                    Category = "Scheduled Task",
                    Title = $"Scheduled Task требует проверки: {name}",
                    Details = $"{command}. {reason}",
                    Recommendation = "Откройте Task Scheduler и проверьте автора, триггеры и путь запуска. Подозрительную задачу сначала отключайте, а не удаляйте без проверки."
                });
            }
        }

        private static async Task ScanServicesAsync(CompromiseScanReport report)
        {
            const string script =
                "Get-CimInstance Win32_Service -ErrorAction SilentlyContinue | Where-Object {$_.StartMode -eq 'Auto'} | Select-Object Name,DisplayName,State,PathName | ConvertTo-Json -Compress";

            using JsonDocument? json = await RunPowerShellJsonAsync(script);
            if (json == null)
                return;

            foreach (JsonElement item in EnumerateObjects(json.RootElement))
            {
                report.AutomaticServices++;
                string name = TryGetString(item, "Name");
                string display = TryGetString(item, "DisplayName");
                string path = TryGetString(item, "PathName");
                int points = ScorePersistenceCommand(path, out string reason);

                if (points < 20)
                    continue;

                report.SuspiciousServices++;
                report.Findings.Add(new CompromiseFinding
                {
                    Risk = RiskFromPoints(points + 10),
                    Category = "Service",
                    Title = $"Автоматическая служба требует проверки: {display}",
                    Details = $"{name}: {path}. {reason}",
                    Recommendation = "Проверьте свойства службы и издателя файла. Не отключайте системные службы без понимания их назначения."
                });
            }
        }

        private static async Task ScanDefenderExclusionsAsync(CompromiseScanReport report)
        {
            const string script =
                "$p=Get-MpPreference -ErrorAction SilentlyContinue; if($p){[PSCustomObject]@{Paths=$p.ExclusionPath;Processes=$p.ExclusionProcess;Extensions=$p.ExclusionExtension}} | ConvertTo-Json -Compress";

            using JsonDocument? json = await RunPowerShellJsonAsync(script);
            if (json == null || json.RootElement.ValueKind != JsonValueKind.Object)
                return;

            foreach (string value in ReadJsonStrings(json.RootElement, "Paths")
                .Concat(ReadJsonStrings(json.RootElement, "Processes"))
                .Concat(ReadJsonStrings(json.RootElement, "Extensions")))
            {
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                report.DefenderExclusions++;
                bool suspicious = IsWritableUserLocation(value) ||
                                  value.Contains("temp", StringComparison.OrdinalIgnoreCase) ||
                                  value.Contains("powershell", StringComparison.OrdinalIgnoreCase) ||
                                  value.Equals("exe", StringComparison.OrdinalIgnoreCase);

                if (!suspicious)
                    continue;

                report.SuspiciousDefenderExclusions++;
                report.Findings.Add(new CompromiseFinding
                {
                    Risk = CompromiseRisk.High,
                    Category = "Defender",
                    Title = "Подозрительное исключение Microsoft Defender",
                    Details = value,
                    Recommendation = "Если вы не добавляли это исключение сами и оно не принадлежит доверенному ПО, проверьте Windows Security и происхождение исключения."
                });
            }
        }

        private static void ScanRdp(CompromiseScanReport report)
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server");
                object? value = key?.GetValue("fDenyTSConnections");
                if (value == null)
                    return;

                report.RdpEnabled = Convert.ToInt32(value) == 0;
                if (report.RdpEnabled == true)
                {
                    report.Findings.Add(new CompromiseFinding
                    {
                        Risk = CompromiseRisk.Low,
                        Category = "Remote Access",
                        Title = "Remote Desktop включён",
                        Details = "Windows разрешает входящие RDP-подключения.",
                        Recommendation = "Если вы не используете удалённый рабочий стол, отключите его в параметрах Windows. Сам факт включённого RDP не означает взлом."
                    });
                }
            }
            catch { }
        }

        private static void ScanHosts(CompromiseScanReport report)
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
                if (!File.Exists(path))
                    return;

                string[] meaningful = File.ReadAllLines(path)
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0 && !line.StartsWith("#"))
                    .Where(line => !line.StartsWith("127.0.0.1 localhost", StringComparison.OrdinalIgnoreCase))
                    .Where(line => !line.StartsWith("::1 localhost", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (meaningful.Length == 0)
                    return;

                report.HostsModified = true;
                report.Findings.Add(new CompromiseFinding
                {
                    Risk = CompromiseRisk.Medium,
                    Category = "System",
                    Title = "HOSTS содержит пользовательские перенаправления",
                    Details = string.Join(" | ", meaningful.Take(10)),
                    Path = path,
                    Recommendation = "HOSTS часто изменяют легитимные блокировщики и разработчики, но вредоносное ПО тоже может перенаправлять домены. Проверьте записи вручную."
                });
            }
            catch { }
        }

        private static async Task ScanListeningPortsAsync(CompromiseScanReport report)
        {
            const string script =
                "$x=Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Select-Object -First 300 LocalAddress,LocalPort,OwningProcess; $x | ConvertTo-Json -Compress";

            using JsonDocument? json = await RunPowerShellJsonAsync(script);
            if (json == null)
                return;

            foreach (JsonElement item in EnumerateObjects(json.RootElement))
            {
                report.ListeningPorts++;
                int pid = TryGetInt(item, "OwningProcess");
                string address = TryGetString(item, "LocalAddress");
                int port = TryGetInt(item, "LocalPort");

                string processName = "unknown";
                string processPath = "";
                try
                {
                    using Process process = Process.GetProcessById(pid);
                    processName = process.ProcessName;
                    try { processPath = process.MainModule?.FileName ?? ""; } catch { }
                }
                catch { }

                if (string.IsNullOrWhiteSpace(processPath) || !IsWritableUserLocation(processPath))
                    continue;

                report.Findings.Add(new CompromiseFinding
                {
                    Risk = CompromiseRisk.Medium,
                    Category = "Network",
                    Title = $"Процесс из пользовательской папки слушает TCP-порт: {processName}",
                    Details = $"{address}:{port} • PID {pid}",
                    Path = processPath,
                    Recommendation = "Проверьте, ожидаете ли вы от этой программы входящие соединения. Для браузеров, IDE и локальных серверов это может быть нормальным."
                });
            }
        }

        private static void FinalizeReport(CompromiseScanReport report)
        {
            int deduction = 0;
            foreach (CompromiseFinding finding in report.Findings)
            {
                deduction += finding.Risk switch
                {
                    CompromiseRisk.Critical => 25,
                    CompromiseRisk.High => 14,
                    CompromiseRisk.Medium => 6,
                    CompromiseRisk.Low => 2,
                    _ => 0
                };
            }

            report.Score = Math.Clamp(100 - Math.Min(deduction, 100), 0, 100);
            int serious = report.Findings.Count(item => item.Risk >= CompromiseRisk.High);

            report.Status = serious switch
            {
                >= 3 => "POSSIBLE COMPROMISE",
                >= 1 => "REVIEW REQUIRED",
                _ when report.Findings.Any(item => item.Risk == CompromiseRisk.Medium) => "ATTENTION",
                _ => "NO OBVIOUS COMPROMISE"
            };

            if (!report.Findings.Any(item => item.Risk >= CompromiseRisk.Medium))
            {
                report.Findings.Insert(0, new CompromiseFinding
                {
                    Risk = CompromiseRisk.Info,
                    Category = "Summary",
                    Title = "Явных признаков компрометации не обнаружено",
                    Details = "Проверены типовые пользовательские каталоги, автозагрузка, Scheduled Tasks, службы, Defender exclusions, RDP, HOSTS и слушающие TCP-порты.",
                    Recommendation = "Это диагностическая эвристика, а не гарантия отсутствия вредоносного ПО. Для полной проверки используйте актуальный Microsoft Defender или другой доверенный антивирус."
                });
            }
        }

        private static int ScorePersistenceCommand(string command, out string reason)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                reason = "";
                return 0;
            }

            string value = Environment.ExpandEnvironmentVariables(command).ToLowerInvariant();
            int score = 0;
            List<string> reasons = new();

            if (IsWritableUserLocation(value) || value.Contains("\\temp\\"))
            {
                score += 20;
                reasons.Add("запуск из пользовательской/временной папки");
            }

            if (value.Contains("powershell") &&
                (value.Contains(" -enc ") || value.Contains(" -encodedcommand ") || value.Contains("frombase64string")))
            {
                score += 35;
                reasons.Add("обфусцированная/кодированная PowerShell-команда");
            }

            if (value.Contains("wscript.exe") || value.Contains("cscript.exe") || value.Contains("mshta.exe"))
            {
                score += 12;
                reasons.Add("используется script host");
            }

            if (value.Contains("rundll32") && value.Contains("javascript:"))
            {
                score += 40;
                reasons.Add("подозрительный rundll32 javascript pattern");
            }

            reason = string.Join("; ", reasons);
            return score;
        }

        private static bool IsWritableUserLocation(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string expanded = Environment.ExpandEnvironmentVariables(value).Replace('/', '\\');
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            return expanded.Contains(appData, StringComparison.OrdinalIgnoreCase) ||
                   expanded.Contains(local, StringComparison.OrdinalIgnoreCase) ||
                   expanded.Contains(Path.Combine(user, "Downloads"), StringComparison.OrdinalIgnoreCase) ||
                   expanded.Contains("\\Temp\\", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExecutableLike(string extension) =>
            extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".scr", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".com", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".msi", StringComparison.OrdinalIgnoreCase);

        private static bool IsScript(string extension) =>
            extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".vbs", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".vbe", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jse", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".wsf", StringComparison.OrdinalIgnoreCase);

        private static bool HasSuspiciousDoubleExtension(string fileName)
        {
            string lower = fileName.ToLowerInvariant();
            string[] bait = { ".pdf.", ".doc.", ".docx.", ".jpg.", ".png.", ".txt.", ".xlsx." };
            return bait.Any(lower.Contains);
        }

        private static string? FindSuspiciousScriptMarker(string path)
        {
            try
            {
                FileInfo info = new(path);
                if (info.Length > 512 * 1024)
                    return null;

                string text = File.ReadAllText(path).ToLowerInvariant();
                return SuspiciousScriptMarkers.FirstOrDefault(marker => text.Contains(marker));
            }
            catch
            {
                return null;
            }
        }

        private static bool? HasEmbeddedCertificate(string path)
        {
            try
            {
                using X509Certificate certificate = X509Certificate.CreateFromSignedFile(path);
                return certificate != null;
            }
            catch (CryptographicException)
            {
                return false;
            }
            catch
            {
                return null;
            }
        }

        private static string TrySha256(string path)
        {
            try
            {
                using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                byte[] hash = SHA256.HashData(stream);
                return Convert.ToHexString(hash);
            }
            catch
            {
                return "";
            }
        }

        private static CompromiseRisk RiskFromPoints(int points) => points switch
        {
            >= 65 => CompromiseRisk.Critical,
            >= 45 => CompromiseRisk.High,
            >= 28 => CompromiseRisk.Medium,
            >= 15 => CompromiseRisk.Low,
            _ => CompromiseRisk.Info
        };

        private static string ShortPath(string path)
        {
            if (path.Length <= 70)
                return path;
            return "…" + path[^67..];
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
                startInfo.ArgumentList.Add("-ExecutionPolicy");
                startInfo.ArgumentList.Add("Bypass");
                startInfo.ArgumentList.Add("-Command");
                startInfo.ArgumentList.Add(script);

                using Process process = new() { StartInfo = startInfo };
                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                    return null;

                return JsonDocument.Parse(output);
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<JsonElement> EnumerateObjects(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                        yield return item;
                }
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                yield return element;
            }
        }

        private static string TryGetString(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
                return "";

            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
        }

        private static int TryGetInt(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
                return 0;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
                return number;

            return int.TryParse(value.ToString(), out number) ? number : 0;
        }

        private static IEnumerable<string> ReadJsonStrings(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
                yield break;

            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    string text = item.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                        yield return text;
                }
                yield break;
            }

            string single = value.ToString();
            if (!string.IsNullOrWhiteSpace(single))
                yield return single;
        }
    }
}

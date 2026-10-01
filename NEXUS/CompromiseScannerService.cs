using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NEXUS
{
    public enum CompromiseRisk
    {
        Info = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4
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

        private static readonly HashSet<string> SystemLookalikeNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "svchost", "lsass", "csrss", "winlogon", "services",
            "smss", "taskhostw", "explorer", "conhost", "dwm"
        };

        private static readonly string[] ScriptMarkers =
        {
            " -enc ", " -encodedcommand ", "frombase64string", "invoke-expression",
            "iex(", "downloadstring(", "invoke-webrequest", "start-bitstransfer",
            "rundll32 javascript:", "mshta http"
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
            progress?.Report("Проверка автозагрузки...");
            ScanRegistryAutoruns(report);
            ScanStartupFolders(report);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Проверка Scheduled Tasks...");
            await ScanScheduledTasksAsync(report);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Проверка автоматических служб...");
            await ScanServicesAsync(report);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("Проверка исключений Microsoft Defender...");
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
            CancellationToken token)
        {
            HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
            int maxFiles = deepScan ? 12000 : 3500;

            foreach (string root in BuildScanRoots(deepScan))
            {
                if (report.FilesScanned >= maxFiles)
                    break;
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    continue;

                progress?.Report($"Файлы: {ShortPath(root)}");

                foreach (string file in SafeEnumerateFiles(root, deepScan ? 6 : 3))
                {
                    token.ThrowIfCancellationRequested();
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

        private static IEnumerable<string> BuildScanRoots(bool deepScan)
        {
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            List<string> roots = new()
            {
                Path.GetTempPath(),
                Path.Combine(user, "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                Path.Combine(localAppData, "Temp")
            };

            if (deepScan)
            {
                roots.Add(appData);
                roots.Add(localAppData);
                roots.Add(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Microsoft", "Windows", "Start Menu", "Programs", "Startup"));
            }

            return roots.Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> SafeEnumerateFiles(string root, int maxDepth)
        {
            Stack<(string Path, int Depth)> pending = new();
            pending.Push((root, 0));

            while (pending.Count > 0)
            {
                (string current, int depth) = pending.Pop();
                string[] files = Array.Empty<string>();
                string[] directories = Array.Empty<string>();

                try { files = Directory.GetFiles(current); } catch { }
                foreach (string file in files)
                    yield return file;

                if (depth >= maxDepth)
                    continue;

                try { directories = Directory.GetDirectories(current); } catch { }
                foreach (string directory in directories)
                {
                    try
                    {
                        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                            continue;
                    }
                    catch { continue; }

                    pending.Push((directory, depth + 1));
                }
            }
        }

        private static void EvaluateFile(string file, CompromiseScanReport report)
        {
            int points = 0;
            List<string> reasons = new();
            string lower = file.ToLowerInvariant();
            string extension = Path.GetExtension(file).ToLowerInvariant();
            string baseName = Path.GetFileNameWithoutExtension(file);

            bool inTemp = lower.Contains("\\temp\\") || lower.StartsWith(Path.GetTempPath().ToLowerInvariant());
            bool inDownloads = lower.Contains("\\downloads\\");
            bool inStartup = lower.Contains("\\start menu\\programs\\startup\\");
            bool inAppData = lower.Contains("\\appdata\\");

            if (inTemp && IsExecutableLike(extension))
            {
                points += 25;
                reasons.Add("исполняемый файл находится во временной папке");
            }

            if (inStartup)
            {
                points += 25;
                reasons.Add("файл находится в папке автозагрузки");
            }

            if (inDownloads && IsScript(extension))
            {
                points += 12;
                reasons.Add("скрипт находится в Downloads");
            }

            if (inAppData && SystemLookalikeNames.Contains(baseName))
            {
                points += 35;
                reasons.Add("имя похоже на системный процесс Windows, но файл расположен в AppData");
            }

            try
            {
                FileInfo info = new(file);
                if (DateTime.Now - info.CreationTime < TimeSpan.FromDays(7))
                {
                    points += 8;
                    reasons.Add("файл появился недавно");
                }
                if ((info.Attributes & FileAttributes.Hidden) != 0)
                {
                    points += 8;
                    reasons.Add("атрибут Hidden");
                }
                if ((info.Attributes & FileAttributes.System) != 0)
                {
                    points += 8;
                    reasons.Add("атрибут System");
                }
            }
            catch { }

            if (HasSuspiciousDoubleExtension(Path.GetFileName(file)))
            {
                points += 18;
                reasons.Add("маскирующее двойное расширение");
            }

            if (IsScript(extension))
            {
                string? marker = FindScriptMarker(file);
                if (marker != null)
                {
                    points += 30;
                    reasons.Add($"подозрительный шаблон в скрипте: {marker}");
                }
            }

            if (IsExecutableLike(extension) && points >= 20)
            {
                bool? signed = HasEmbeddedCertificate(file);
                if (signed == false)
                {
                    points += 12;
                    reasons.Add("Authenticode-сертификат не обнаружен");
                }
            }

            if (points < 20)
                return;

            report.SuspiciousFiles++;
            CompromiseRisk risk = RiskFromPoints(points);
            report.Findings.Add(new CompromiseFinding
            {
                Risk = risk,
                Category = "File",
                Title = $"Файл требует проверки: {Path.GetFileName(file)}",
                Details = string.Join("; ", reasons),
                Path = file,
                Sha256 = TrySha256(file),
                Recommendation = IsAtLeast(risk, CompromiseRisk.High)
                    ? "Не удаляйте файл вслепую. Проверьте происхождение и запустите Microsoft Defender. Если файл вам неизвестен и связан с автозапуском/сетью, временно отключитесь от сети и выполните полную проверку."
                    : "Проверьте происхождение файла. Один эвристический признак не доказывает заражение."
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
                    if (key == null) continue;

                    foreach (string name in key.GetValueNames())
                    {
                        report.PersistenceEntries++;
                        string command = Environment.ExpandEnvironmentVariables(key.GetValue(name)?.ToString() ?? "");
                        int points = ScorePersistenceCommand(command, out string reason);
                        if (points < 20) continue;

                        report.SuspiciousPersistenceEntries++;
                        report.Findings.Add(new CompromiseFinding
                        {
                            Risk = RiskFromPoints(points),
                            Category = "Persistence",
                            Title = $"Автозагрузка требует проверки: {name}",
                            Details = $"{location.Label}: {command}. {reason}",
                            Recommendation = "Проверьте издателя и назначение программы до отключения записи."
                        });
                    }
                }
                catch { }
            }
        }

        private static void ScanStartupFolders(CompromiseScanReport report)
        {
            foreach (string folder in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
            })
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;
                string[] files;
                try { files = Directory.GetFiles(folder); } catch { continue; }

                foreach (string file in files)
                {
                    report.PersistenceEntries++;
                    string ext = Path.GetExtension(file);
                    if (!InterestingExtensions.Contains(ext) && !ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                        continue;

                    report.Findings.Add(new CompromiseFinding
                    {
                        Risk = CompromiseRisk.Info,
                        Category = "Persistence",
                        Title = $"Startup: {Path.GetFileName(file)}",
                        Details = "Объект запускается при входе пользователя в Windows.",
                        Path = file,
                        Recommendation = "Если объект вам неизвестен, проверьте свойства и целевой файл ярлыка."
                    });
                }
            }
        }

        private static async Task ScanScheduledTasksAsync(CompromiseScanReport report)
        {
            const string script = "$x=Get-ScheduledTask -ErrorAction SilentlyContinue | ForEach-Object {$t=$_; foreach($a in $t.Actions){[PSCustomObject]@{TaskName=$t.TaskName;Execute=$a.Execute;Arguments=$a.Arguments}}}; $x | ConvertTo-Json -Compress";
            using JsonDocument? json = await RunPowerShellJsonAsync(script);
            if (json == null) return;

            foreach (JsonElement item in EnumerateObjects(json.RootElement))
            {
                report.ScheduledTasks++;
                string name = TryGetString(item, "TaskName");
                string command = $"{TryGetString(item, "Execute")} {TryGetString(item, "Arguments")}".Trim();
                int points = ScorePersistenceCommand(command, out string reason);
                if (points < 20) continue;

                report.SuspiciousScheduledTasks++;
                report.Findings.Add(new CompromiseFinding
                {
                    Risk = RiskFromPoints(points + 10),
                    Category = "Scheduled Task",
                    Title = $"Scheduled Task требует проверки: {name}",
                    Details = $"{command}. {reason}",
                    Recommendation = "Проверьте автора, триггеры и путь запуска в Task Scheduler."
                });
            }
        }

        private static async Task ScanServicesAsync(CompromiseScanReport report)
        {
            const string script = "Get-CimInstance Win32_Service -ErrorAction SilentlyContinue | Where-Object {$_.StartMode -eq 'Auto'} | Select-Object Name,DisplayName,PathName | ConvertTo-Json -Compress";
            using JsonDocument? json = await RunPowerShellJsonAsync(script);
            if (json == null) return;

            foreach (JsonElement item in EnumerateObjects(json.RootElement))
            {
                report.AutomaticServices++;
                string name = TryGetString(item, "Name");
                string display = TryGetString(item, "DisplayName");
                string path = TryGetString(item, "PathName");
                int points = ScorePersistenceCommand(path, out string reason);
                if (points < 20) continue;

                report.SuspiciousServices++;
                report.Findings.Add(new CompromiseFinding
                {
                    Risk = RiskFromPoints(points + 10),
                    Category = "Service",
                    Title = $"Автоматическая служба требует проверки: {display}",
                    Details = $"{name}: {path}. {reason}",
                    Recommendation = "Проверьте свойства службы и издателя файла. Не отключайте системные службы вслепую."
                });
            }
        }

        private static async Task ScanDefenderExclusionsAsync(CompromiseScanReport report)
        {
            const string script = "$p=Get-MpPreference -ErrorAction SilentlyContinue; $o=if($p){[PSCustomObject]@{Paths=$p.ExclusionPath;Processes=$p.ExclusionProcess;Extensions=$p.ExclusionExtension}}; $o | ConvertTo-Json -Compress";
            using JsonDocument? json = await RunPowerShellJsonAsync(script);
            if (json == null || json.RootElement.ValueKind != JsonValueKind.Object) return;

            IEnumerable<string> exclusions = ReadJsonStrings(json.RootElement, "Paths")
                .Concat(ReadJsonStrings(json.RootElement, "Processes"))
                .Concat(ReadJsonStrings(json.RootElement, "Extensions"));

            foreach (string value in exclusions)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                report.DefenderExclusions++;

                bool suspicious = IsWritableUserLocation(value) ||
                                  value.Contains("temp", StringComparison.OrdinalIgnoreCase) ||
                                  value.Contains("powershell", StringComparison.OrdinalIgnoreCase) ||
                                  value.Equals("exe", StringComparison.OrdinalIgnoreCase);
                if (!suspicious) continue;

                report.SuspiciousDefenderExclusions++;
                report.Findings.Add(new CompromiseFinding
                {
                    Risk = CompromiseRisk.High,
                    Category = "Defender",
                    Title = "Подозрительное исключение Microsoft Defender",
                    Details = value,
                    Recommendation = "Если вы не добавляли это исключение сами, проверьте Windows Security и происхождение записи."
                });
            }
        }

        private static void ScanRdp(CompromiseScanReport report)
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server");
                object? value = key?.GetValue("fDenyTSConnections");
                if (value == null) return;

                report.RdpEnabled = Convert.ToInt32(value) == 0;
                if (report.RdpEnabled == true)
                {
                    report.Findings.Add(new CompromiseFinding
                    {
                        Risk = CompromiseRisk.Low,
                        Category = "Remote Access",
                        Title = "Remote Desktop включён",
                        Details = "Windows разрешает входящие RDP-подключения.",
                        Recommendation = "Если вы RDP не используете, отключите его. Сам факт включения не означает взлом."
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
                if (!File.Exists(path)) return;

                string[] entries = File.ReadAllLines(path)
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0 && !line.StartsWith("#"))
                    .Where(line => !line.StartsWith("127.0.0.1 localhost", StringComparison.OrdinalIgnoreCase))
                    .Where(line => !line.StartsWith("::1 localhost", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (entries.Length == 0) return;

                report.HostsModified = true;
                report.Findings.Add(new CompromiseFinding
                {
                    Risk = CompromiseRisk.Medium,
                    Category = "System",
                    Title = "HOSTS содержит пользовательские перенаправления",
                    Details = string.Join(" | ", entries.Take(10)),
                    Path = path,
                    Recommendation = "Это может быть легитимно, но вредоносное ПО тоже меняет HOSTS. Проверьте записи вручную."
                });
            }
            catch { }
        }

        private static async Task ScanListeningPortsAsync(CompromiseScanReport report)
        {
            const string script = "$x=Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Select-Object -First 300 LocalAddress,LocalPort,OwningProcess; $x | ConvertTo-Json -Compress";
            using JsonDocument? json = await RunPowerShellJsonAsync(script);
            if (json == null) return;

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
                    Recommendation = "Проверьте, ожидаете ли вы от этой программы входящие соединения. Для IDE и локальных серверов это может быть нормальным."
                });
            }
        }

        private static void FinalizeReport(CompromiseScanReport report)
        {
            int deduction = report.Findings.Sum(finding => finding.Risk switch
            {
                CompromiseRisk.Critical => 25,
                CompromiseRisk.High => 14,
                CompromiseRisk.Medium => 6,
                CompromiseRisk.Low => 2,
                _ => 0
            });

            report.Score = Math.Clamp(100 - Math.Min(deduction, 100), 0, 100);
            int serious = report.Findings.Count(item => IsAtLeast(item.Risk, CompromiseRisk.High));

            report.Status = serious switch
            {
                >= 3 => "POSSIBLE COMPROMISE",
                >= 1 => "REVIEW REQUIRED",
                _ when report.Findings.Any(item => item.Risk == CompromiseRisk.Medium) => "ATTENTION",
                _ => "NO OBVIOUS COMPROMISE"
            };

            if (!report.Findings.Any(item => IsAtLeast(item.Risk, CompromiseRisk.Medium)))
            {
                report.Findings.Insert(0, new CompromiseFinding
                {
                    Risk = CompromiseRisk.Info,
                    Category = "Summary",
                    Title = "Явных признаков компрометации не обнаружено",
                    Details = "Проверены ключевые пользовательские каталоги, автозагрузка, Scheduled Tasks, службы, Defender exclusions, RDP, HOSTS и слушающие TCP-порты.",
                    Recommendation = "Это эвристическая диагностика, а не гарантия отсутствия вредоносного ПО. Для полной проверки используйте актуальный Microsoft Defender."
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
                reasons.Add("кодированная PowerShell-команда");
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
            if (string.IsNullOrWhiteSpace(value)) return false;
            string expanded = Environment.ExpandEnvironmentVariables(value).Replace('/', '\\');
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            return expanded.Contains(appData, StringComparison.OrdinalIgnoreCase) ||
                   expanded.Contains(local, StringComparison.OrdinalIgnoreCase) ||
                   expanded.Contains(Path.Combine(user, "Downloads"), StringComparison.OrdinalIgnoreCase) ||
                   expanded.Contains("\\Temp\\", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExecutableLike(string ext) =>
            ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".scr", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".com", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".msi", StringComparison.OrdinalIgnoreCase);

        private static bool IsScript(string ext) =>
            ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".vbs", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".vbe", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".jse", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".wsf", StringComparison.OrdinalIgnoreCase);

        private static bool HasSuspiciousDoubleExtension(string fileName)
        {
            string lower = fileName.ToLowerInvariant();
            string[] bait = { ".pdf.", ".doc.", ".docx.", ".jpg.", ".png.", ".txt.", ".xlsx." };
            return bait.Any(part => lower.Contains(part, StringComparison.Ordinal));
        }

        private static string? FindScriptMarker(string path)
        {
            try
            {
                FileInfo info = new(path);
                if (info.Length > 512 * 1024) return null;
                string text = File.ReadAllText(path).ToLowerInvariant();
                return ScriptMarkers.FirstOrDefault(marker => text.Contains(marker, StringComparison.Ordinal));
            }
            catch { return null; }
        }

        private static bool? HasEmbeddedCertificate(string path)
        {
            try
            {
                using X509Certificate certificate = X509Certificate.CreateFromSignedFile(path);
                return certificate.Handle != IntPtr.Zero;
            }
            catch (CryptographicException) { return false; }
            catch { return null; }
        }

        private static string TrySha256(string path)
        {
            try
            {
                using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return Convert.ToHexString(SHA256.HashData(stream));
            }
            catch { return ""; }
        }

        private static CompromiseRisk RiskFromPoints(int points) => points switch
        {
            >= 65 => CompromiseRisk.Critical,
            >= 45 => CompromiseRisk.High,
            >= 28 => CompromiseRisk.Medium,
            >= 15 => CompromiseRisk.Low,
            _ => CompromiseRisk.Info
        };

        private static bool IsAtLeast(CompromiseRisk actual, CompromiseRisk threshold) =>
            (int)actual >= (int)threshold;

        private static string ShortPath(string path) =>
            path.Length <= 70 ? path : "…" + path[^67..];

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

                using Process process = new() { StartInfo = startInfo };
                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output)) return null;
                return JsonDocument.Parse(output);
            }
            catch { return null; }
        }

        private static IEnumerable<JsonElement> EnumerateObjects(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.Object)
                        yield return item;
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                yield return element;
            }
        }

        private static string TryGetString(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value)) return "";
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
        }

        private static int TryGetInt(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value)) return 0;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)) return number;
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
                    if (!string.IsNullOrWhiteSpace(text)) yield return text;
                }
                yield break;
            }

            string single = value.ToString();
            if (!string.IsNullOrWhiteSpace(single)) yield return single;
        }
    }
}

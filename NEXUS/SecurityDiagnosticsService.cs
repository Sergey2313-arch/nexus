using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Tasks;

namespace NEXUS
{
    public sealed class SecurityDiagnosticsService
    {
        private static readonly string[] SuspiciousWritableFolders =
        {
            "\\AppData\\Local\\Temp\\",
            "\\Windows\\Temp\\",
            "\\Temp\\",
            "\\Downloads\\"
        };

        public async Task<SecurityDiagnosticReport> RunAsync()
        {
            SecurityDiagnosticReport report = new();

            report.IsAdministrator = IsAdministrator();
            report.RunningProcesses = ScanProcesses(report);
            ScanAutoruns(report);

            await ReadDefenderAsync(report);
            await ReadFirewallAsync(report);
            await ReadServicesAsync(report);
            await ReadNetworkAsync(report);

            report.Score = Math.Clamp(report.Score, 0, 100);
            report.Status = report.Score switch
            {
                >= 90 => "PROTECTED",
                >= 75 => "ATTENTION",
                >= 55 => "WARNING",
                _ => "HIGH RISK"
            };

            if (!report.IsAdministrator)
            {
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "Privileges",
                    Title = "NEXUS запущен без прав администратора",
                    Details = "Базовая диагностика выполнена. Часть защищённых системных данных может быть недоступна.",
                    Recommendation = "Повышенные права нужны только для углублённой проверки. Постоянно запускать NEXUS от администратора не требуется.",
                    Severity = DiagnosticSeverity.Info
                });
            }

            return report;
        }

        private static bool IsAdministrator()
        {
            try
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private static int ScanProcesses(SecurityDiagnosticReport report)
        {
            int total = 0;
            int suspicious = 0;

            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    total++;

                    string name = process.ProcessName;
                    string path = "";

                    try
                    {
                        path = process.MainModule?.FileName ?? "";
                    }
                    catch
                    {
                    }

                    if (!string.IsNullOrWhiteSpace(path) &&
                        IsSuspiciousExecutableLocation(path))
                    {
                        suspicious++;

                        report.Findings.Add(new DiagnosticFinding
                        {
                            Category = "Process",
                            Title = $"Процесс требует проверки: {name}",
                            Details = $"PID {process.Id} • {path}",
                            Recommendation = "Сам путь не доказывает наличие вируса. Проверьте происхождение программы, цифровую подпись и необходимость её запуска.",
                            Severity = DiagnosticSeverity.Warning
                        });
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            report.SuspiciousProcesses = suspicious;

            if (suspicious > 0)
            {
                report.Score -= Math.Min(18, suspicious * 4);
            }
            else
            {
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "Process",
                    Title = "Подозрительных путей запуска процессов не обнаружено",
                    Details = $"Проверено запущенных процессов: {total}.",
                    Recommendation = "Это эвристическая проверка и не заменяет антивирусное сканирование.",
                    Severity = DiagnosticSeverity.Good
                });
            }

            return total;
        }

        private static void ScanAutoruns(SecurityDiagnosticReport report)
        {
            List<(string Name, string Command, string Location)> entries = new();

            ReadRunKey(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                "HKCU\\Run",
                entries);

            ReadRunKey(
                Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
                "HKCU\\RunOnce",
                entries);

            ReadRunKey(
                Registry.LocalMachine,
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                "HKLM\\Run",
                entries);

            ReadRunKey(
                Registry.LocalMachine,
                @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
                "HKLM\\RunOnce",
                entries);

            report.AutorunEntries = entries.Count;

            foreach (var entry in entries)
            {
                if (!IsSuspiciousCommand(entry.Command))
                    continue;

                report.SuspiciousAutoruns++;
                report.Score -= 7;

                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "Autorun",
                    Title = $"Автозагрузка требует проверки: {entry.Name}",
                    Details = $"{entry.Location} • {entry.Command}",
                    Recommendation = "Не удаляйте запись автоматически. Сначала убедитесь, что программа вам известна и действительно должна запускаться вместе с Windows.",
                    Severity = DiagnosticSeverity.Warning
                });
            }

            if (report.SuspiciousAutoruns == 0)
            {
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "Autorun",
                    Title = "Явно подозрительных записей автозагрузки не найдено",
                    Details = $"Проверено записей Run/RunOnce: {entries.Count}.",
                    Recommendation = "Позже NEXUS также проверит Scheduled Tasks и дополнительные точки автозапуска.",
                    Severity = DiagnosticSeverity.Good
                });
            }
        }

        private static void ReadRunKey(
            RegistryKey root,
            string path,
            string location,
            List<(string Name, string Command, string Location)> result)
        {
            try
            {
                using RegistryKey? key = root.OpenSubKey(path);
                if (key == null)
                    return;

                foreach (string name in key.GetValueNames())
                {
                    string command = key.GetValue(name)?.ToString() ?? "";
                    result.Add((name, command, location));
                }
            }
            catch
            {
            }
        }

        private static async Task ReadDefenderAsync(SecurityDiagnosticReport report)
        {
            const string script =
                "Get-MpComputerStatus | Select-Object AntivirusEnabled,RealTimeProtectionEnabled,AntispywareEnabled,BehaviorMonitorEnabled,IoavProtectionEnabled,NISEnabled | ConvertTo-Json -Compress";

            using JsonDocument? json = await RunPowerShellJsonAsync(script);

            if (json == null || json.RootElement.ValueKind != JsonValueKind.Object)
            {
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "Defender",
                    Title = "Не удалось получить состояние Microsoft Defender",
                    Details = "Команда Get-MpComputerStatus не вернула доступные данные.",
                    Recommendation = "Проверьте состояние Windows Security вручную. NEXUS не считает отсутствие ответа доказательством отключённой защиты.",
                    Severity = DiagnosticSeverity.Info
                });
                return;
            }

            JsonElement root = json.RootElement;

            report.DefenderEnabled = TryGetBool(root, "AntivirusEnabled");
            report.RealTimeProtectionEnabled = TryGetBool(root, "RealTimeProtectionEnabled");

            if (report.DefenderEnabled == false)
            {
                report.Score -= 25;
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "Defender",
                    Title = "Microsoft Defender Antivirus отключён",
                    Details = "Windows сообщает, что встроенный антивирус не активен.",
                    Recommendation = "Если у вас нет другого доверенного антивируса, включите защиту Windows Security и выполните проверку системы.",
                    Severity = DiagnosticSeverity.Critical
                });
            }
            else if (report.RealTimeProtectionEnabled == false)
            {
                report.Score -= 18;
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "Defender",
                    Title = "Защита Defender в реальном времени отключена",
                    Details = "Антивирус доступен, но realtime-защита выключена.",
                    Recommendation = "Проверьте настройки Windows Security и причину отключения realtime-защиты.",
                    Severity = DiagnosticSeverity.Warning
                });
            }
            else
            {
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "Defender",
                    Title = "Microsoft Defender активен",
                    Details = "Антивирус и защита в реальном времени включены.",
                    Recommendation = "Для дополнительной проверки можно запускать Quick Scan из будущего модуля NEXUS.",
                    Severity = DiagnosticSeverity.Good
                });
            }
        }

        private static async Task ReadFirewallAsync(SecurityDiagnosticReport report)
        {
            const string script =
                "Get-NetFirewallProfile | Select-Object Name,Enabled | ConvertTo-Json -Compress";

            using JsonDocument? json = await RunPowerShellJsonAsync(script);

            if (json == null)
                return;

            List<JsonElement> profiles = EnumerateObjects(json.RootElement).ToList();
            bool allEnabled = profiles.Count > 0 &&
                profiles.All(item => TryGetBool(item, "Enabled") == true);

            report.FirewallEnabled = profiles.Count == 0 ? null : allEnabled;

            if (report.FirewallEnabled == false)
            {
                report.Score -= 15;
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "Firewall",
                    Title = "Один или несколько профилей Windows Firewall отключены",
                    Details = "NEXUS обнаружил выключенный профиль брандмауэра.",
                    Recommendation = "Проверьте Domain/Private/Public профили в Windows Security. Отключать Firewall без причины не рекомендуется.",
                    Severity = DiagnosticSeverity.Warning
                });
            }
            else if (report.FirewallEnabled == true)
            {
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "Firewall",
                    Title = "Windows Firewall включён",
                    Details = "Доступные сетевые профили сообщают Enabled = true.",
                    Recommendation = "Дополнительных действий не требуется.",
                    Severity = DiagnosticSeverity.Good
                });
            }
        }

        private static async Task ReadServicesAsync(SecurityDiagnosticReport report)
        {
            const string script =
                "Get-CimInstance Win32_Service | Where-Object {$_.StartMode -eq 'Auto'} | Select-Object Name,DisplayName,State,StartMode,PathName | ConvertTo-Json -Compress";

            using JsonDocument? json = await RunPowerShellJsonAsync(script);
            if (json == null)
                return;

            foreach (JsonElement service in EnumerateObjects(json.RootElement))
            {
                report.AutomaticServices++;

                string name = TryGetString(service, "Name");
                string displayName = TryGetString(service, "DisplayName");
                string path = TryGetString(service, "PathName");

                if (!IsSuspiciousCommand(path))
                    continue;

                report.SuspiciousServices++;
                report.Score -= 8;

                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "Service",
                    Title = $"Автоматическая служба требует проверки: {displayName}",
                    Details = $"{name} • {path}",
                    Recommendation = "Путь запуска выглядит необычно для автоматической службы. Проверьте издателя и назначение службы перед любыми изменениями.",
                    Severity = DiagnosticSeverity.Warning
                });
            }

            if (report.AutomaticServices > 0 && report.SuspiciousServices == 0)
            {
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "Service",
                    Title = "Автоматические службы: явных аномалий путей не найдено",
                    Details = $"Проверено автоматических служб: {report.AutomaticServices}.",
                    Recommendation = "Это эвристическая проверка путей, а не подтверждение безопасности каждой службы.",
                    Severity = DiagnosticSeverity.Good
                });
            }
        }

        private static async Task ReadNetworkAsync(SecurityDiagnosticReport report)
        {
            const string script =
                "$e=(Get-NetTCPConnection -State Established -ErrorAction SilentlyContinue).Count; $l=(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue).Count; [PSCustomObject]@{Established=$e;Listen=$l} | ConvertTo-Json -Compress";

            using JsonDocument? json = await RunPowerShellJsonAsync(script);
            if (json == null || json.RootElement.ValueKind != JsonValueKind.Object)
                return;

            report.EstablishedConnections = TryGetInt(json.RootElement, "Established");
            report.ListeningPorts = TryGetInt(json.RootElement, "Listen");

            report.Findings.Add(new DiagnosticFinding
            {
                Category = "Network",
                Title = "Сетевые соединения собраны",
                Details = $"Установленных TCP-соединений: {report.EstablishedConnections}; слушающих TCP-портов: {report.ListeningPorts}.",
                Recommendation = "Само наличие соединений и слушающих портов нормально. Следующий этап NEXUS будет связывать их с конкретными процессами и оценивать неизвестные слушатели.",
                Severity = DiagnosticSeverity.Info
            });
        }

        private static bool IsSuspiciousExecutableLocation(string path)
        {
            string normalized = path.Replace('/', '\\');
            return SuspiciousWritableFolders.Any(folder =>
                normalized.Contains(folder, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsSuspiciousCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;

            string normalized = Environment.ExpandEnvironmentVariables(command)
                .Replace('/', '\\');

            if (SuspiciousWritableFolders.Any(folder =>
                normalized.Contains(folder, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            string lower = normalized.ToLowerInvariant();

            return lower.Contains("powershell -enc") ||
                   lower.Contains("powershell.exe -enc") ||
                   lower.Contains("frombase64string") ||
                   lower.Contains("javascript:") ||
                   lower.Contains("wscript.exe") && lower.Contains("\\temp\\") ||
                   lower.Contains("cscript.exe") && lower.Contains("\\temp\\");
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

        private static bool? TryGetBool(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
                return null;

            if (value.ValueKind == JsonValueKind.True)
                return true;

            if (value.ValueKind == JsonValueKind.False)
                return false;

            if (value.ValueKind == JsonValueKind.String &&
                bool.TryParse(value.GetString(), out bool parsed))
            {
                return parsed;
            }

            return null;
        }

        private static int TryGetInt(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
                return 0;

            if (value.TryGetInt32(out int number))
                return number;

            if (value.ValueKind == JsonValueKind.String &&
                int.TryParse(value.GetString(), out number))
            {
                return number;
            }

            return 0;
        }

        private static string TryGetString(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
                return "";

            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : value.ToString();
        }
    }
}
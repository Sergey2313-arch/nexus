using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace NEXUS
{
    public sealed class SecurityAuditReport
    {
        public int Score { get; set; } = 100;
        public string Status { get; set; } = "NORMAL";
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public bool SecurityLogReadable { get; set; }
        public int FailedLogons24h { get; set; }
        public int SuccessfulLogons24h { get; set; }
        public int ServiceInstallEvents7d { get; set; }
        public int PowerShellScriptEvents24h { get; set; }
        public int DefenderThreats30d { get; set; }
        public int LocalAdministrators { get; set; }
        public string LocalAdministratorNames { get; set; } = "";
        public DateTime? DefenderSignatureUpdatedAt { get; set; }
        public int? DefenderQuickScanAgeDays { get; set; }
        public int? DefenderFullScanAgeDays { get; set; }

        public List<DiagnosticFinding> Findings { get; } = new();
    }

    public sealed class IntegrityBaselineEntry
    {
        public string Path { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public long Length { get; set; }
        public DateTime LastWriteUtc { get; set; }
    }

    public sealed class IntegrityCheckResult
    {
        public bool BaselineExists { get; set; }
        public DateTime CheckedAt { get; set; } = DateTime.Now;
        public int Compared { get; set; }
        public int Changed { get; set; }
        public int Missing { get; set; }
        public int NewFiles { get; set; }
        public List<DiagnosticFinding> Findings { get; } = new();
    }

    public sealed class SecurityAuditService
    {
        public async Task<SecurityAuditReport> RunAsync()
        {
            SecurityAuditReport report = new();

            await ReadWindowsAuditAsync(report);
            await ReadDefenderHistoryAsync(report);
            await ReadLocalAdministratorsAsync(report);

            FinalizeReport(report);
            return report;
        }

        public async Task<int> CreateIntegrityBaselineAsync(string dataFolder)
        {
            return await Task.Run(() =>
            {
                Directory.CreateDirectory(dataFolder);
                List<IntegrityBaselineEntry> entries = CaptureIntegrityEntries();
                string path = GetBaselinePath(dataFolder);

                File.WriteAllText(
                    path,
                    JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));

                return entries.Count;
            });
        }

        public async Task<IntegrityCheckResult> VerifyIntegrityBaselineAsync(string dataFolder)
        {
            return await Task.Run(() => VerifyIntegrityBaseline(dataFolder));
        }

        public string GetBaselineFilePath(string dataFolder) => GetBaselinePath(dataFolder);

        private static async Task ReadWindowsAuditAsync(SecurityAuditReport report)
        {
            const string script = @"
$now = Get-Date
$securityReadable = $true
$failed = -1
$success = -1
try {
    $failed = @(Get-WinEvent -FilterHashtable @{LogName='Security'; Id=4625; StartTime=$now.AddHours(-24)} -ErrorAction Stop).Count
    $success = @(Get-WinEvent -FilterHashtable @{LogName='Security'; Id=4624; StartTime=$now.AddHours(-24)} -ErrorAction Stop).Count
} catch {
    $securityReadable = $false
}
$serviceInstalls = @(Get-WinEvent -FilterHashtable @{LogName='System'; Id=7045; StartTime=$now.AddDays(-7)} -ErrorAction SilentlyContinue).Count
$psEvents = @(Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-PowerShell/Operational'; Id=4104; StartTime=$now.AddHours(-24)} -ErrorAction SilentlyContinue).Count
[PSCustomObject]@{
    SecurityLogReadable = $securityReadable
    FailedLogons24h = $failed
    SuccessfulLogons24h = $success
    ServiceInstallEvents7d = $serviceInstalls
    PowerShellScriptEvents24h = $psEvents
} | ConvertTo-Json -Compress";

            using JsonDocument? json = await RunPowerShellJsonAsync(script);
            if (json == null || json.RootElement.ValueKind != JsonValueKind.Object)
            {
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "Event Log",
                    Title = "Не удалось прочитать журналы Windows",
                    Details = "NEXUS не получил сводку Security/System/PowerShell Event Log.",
                    Recommendation = "Это не признак взлома. Часть журналов может требовать повышенных прав или быть отключена политикой Windows.",
                    Severity = DiagnosticSeverity.Info
                });
                return;
            }

            JsonElement root = json.RootElement;
            report.SecurityLogReadable = TryGetBool(root, "SecurityLogReadable") ?? false;
            report.FailedLogons24h = Math.Max(0, TryGetInt(root, "FailedLogons24h"));
            report.SuccessfulLogons24h = Math.Max(0, TryGetInt(root, "SuccessfulLogons24h"));
            report.ServiceInstallEvents7d = Math.Max(0, TryGetInt(root, "ServiceInstallEvents7d"));
            report.PowerShellScriptEvents24h = Math.Max(0, TryGetInt(root, "PowerShellScriptEvents24h"));

            if (!report.SecurityLogReadable)
            {
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "Event Log",
                    Title = "Security Event Log недоступен текущему процессу",
                    Details = "События входа 4624/4625 не удалось прочитать.",
                    Recommendation = "Для углублённого аудита можно один раз запустить NEXUS от администратора. Постоянная работа с повышенными правами не нужна.",
                    Severity = DiagnosticSeverity.Info
                });
            }
            else
            {
                if (report.FailedLogons24h >= 200)
                {
                    report.Score -= 12;
                    report.Findings.Add(new DiagnosticFinding
                    {
                        Category = "Authentication",
                        Title = "Очень много неудачных входов за 24 часа",
                        Details = $"Событий 4625: {report.FailedLogons24h}. Успешных 4624: {report.SuccessfulLogons24h}.",
                        Recommendation = "Проверьте источники неудачных входов в Event Viewer. Высокое число может быть следствием службы, неверного сохранённого пароля или попыток подбора — само по себе оно не доказывает взлом.",
                        Severity = DiagnosticSeverity.Warning
                    });
                }
                else if (report.FailedLogons24h >= 50)
                {
                    report.Score -= 5;
                    report.Findings.Add(new DiagnosticFinding
                    {
                        Category = "Authentication",
                        Title = "Повышенное число неудачных входов",
                        Details = $"Событий 4625 за 24 часа: {report.FailedLogons24h}.",
                        Recommendation = "Сопоставьте время событий с собственными входами, RDP, сетевыми ресурсами и службами.",
                        Severity = DiagnosticSeverity.Warning
                    });
                }
                else
                {
                    report.Findings.Add(new DiagnosticFinding
                    {
                        Category = "Authentication",
                        Title = "Аномального количества неудачных входов не видно",
                        Details = $"4625 за 24 часа: {report.FailedLogons24h}; успешных 4624: {report.SuccessfulLogons24h}.",
                        Recommendation = "Это только статистика журнала входов, а не гарантия отсутствия компрометации.",
                        Severity = DiagnosticSeverity.Good
                    });
                }
            }

            if (report.ServiceInstallEvents7d > 0)
            {
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "Services",
                    Title = "За последние 7 дней устанавливались службы",
                    Details = $"Событий System 7045: {report.ServiceInstallEvents7d}.",
                    Recommendation = "Если вы недавно устанавливали драйверы, VPN, антивирус, игровые компоненты или системные утилиты — это может быть нормально. Неизвестные службы стоит проверить отдельно.",
                    Severity = DiagnosticSeverity.Info
                });
            }

            if (report.PowerShellScriptEvents24h > 0)
            {
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "PowerShell",
                    Title = "Есть события Script Block Logging",
                    Details = $"PowerShell 4104 за 24 часа: {report.PowerShellScriptEvents24h}.",
                    Recommendation = "Наличие 4104 само по себе нормально. При подозрениях можно просмотреть конкретные команды и время их запуска.",
                    Severity = DiagnosticSeverity.Info
                });
            }
        }

        private static async Task ReadDefenderHistoryAsync(SecurityAuditReport report)
        {
            const string script = @"
$now = Get-Date
$status = Get-MpComputerStatus -ErrorAction SilentlyContinue
$threats = @(Get-MpThreatDetection -ErrorAction SilentlyContinue | Where-Object {$_.InitialDetectionTime -gt $now.AddDays(-30)}).Count
[PSCustomObject]@{
    Threats30d = $threats
    SignatureUpdated = if($status){$status.AntivirusSignatureLastUpdated}else{$null}
    QuickScanAge = if($status){$status.QuickScanAge}else{$null}
    FullScanAge = if($status){$status.FullScanAge}else{$null}
} | ConvertTo-Json -Compress";

            using JsonDocument? json = await RunPowerShellJsonAsync(script);
            if (json == null || json.RootElement.ValueKind != JsonValueKind.Object)
                return;

            JsonElement root = json.RootElement;
            report.DefenderThreats30d = Math.Max(0, TryGetInt(root, "Threats30d"));
            report.DefenderSignatureUpdatedAt = TryGetDateTime(root, "SignatureUpdated");
            report.DefenderQuickScanAgeDays = TryGetNullableInt(root, "QuickScanAge");
            report.DefenderFullScanAgeDays = TryGetNullableInt(root, "FullScanAge");

            if (report.DefenderThreats30d > 0)
            {
                report.Score -= Math.Min(20, 8 + report.DefenderThreats30d * 2);
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "Defender",
                    Title = "Microsoft Defender фиксировал угрозы за последние 30 дней",
                    Details = $"Записей Get-MpThreatDetection: {report.DefenderThreats30d}.",
                    Recommendation = "Откройте Protection history и проверьте, были ли угрозы удалены/карантинированы и не повторяются ли обнаружения.",
                    Severity = DiagnosticSeverity.Warning
                });
            }
            else
            {
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "Defender",
                    Title = "Недавних обнаружений Defender не найдено",
                    Details = "Get-MpThreatDetection не вернул обнаружений за последние 30 дней.",
                    Recommendation = "Это не заменяет актуальное антивирусное сканирование.",
                    Severity = DiagnosticSeverity.Good
                });
            }

            if (report.DefenderSignatureUpdatedAt.HasValue)
            {
                double ageHours = (DateTime.Now - report.DefenderSignatureUpdatedAt.Value).TotalHours;
                if (ageHours > 72)
                {
                    report.Score -= 8;
                    report.Findings.Add(new DiagnosticFinding
                    {
                        Category = "Defender",
                        Title = "Сигнатуры Defender давно не обновлялись",
                        Details = $"Последнее обновление: {report.DefenderSignatureUpdatedAt.Value:dd.MM.yyyy HH:mm}.",
                        Recommendation = "Запустите Windows Update или обновление Security Intelligence в Windows Security.",
                        Severity = DiagnosticSeverity.Warning
                    });
                }
            }
        }

        private static async Task ReadLocalAdministratorsAsync(SecurityAuditReport report)
        {
            const string script = @"
try {
    $g = Get-LocalGroup -SID 'S-1-5-32-544' -ErrorAction Stop
    $m = @(Get-LocalGroupMember -Group $g -ErrorAction Stop)
    [PSCustomObject]@{Count=$m.Count; Names=($m.Name -join ' | ')} | ConvertTo-Json -Compress
} catch {
    [PSCustomObject]@{Count=-1; Names=''} | ConvertTo-Json -Compress
}";

            using JsonDocument? json = await RunPowerShellJsonAsync(script);
            if (json == null || json.RootElement.ValueKind != JsonValueKind.Object)
                return;

            int count = TryGetInt(json.RootElement, "Count");
            if (count < 0)
            {
                report.Findings.Add(new DiagnosticFinding
                {
                    Category = "Accounts",
                    Title = "Не удалось получить локальную группу Administrators",
                    Details = "Список локальных администраторов недоступен.",
                    Recommendation = "При необходимости проверьте локальные учётные записи через Computer Management или PowerShell от администратора.",
                    Severity = DiagnosticSeverity.Info
                });
                return;
            }

            report.LocalAdministrators = count;
            report.LocalAdministratorNames = TryGetString(json.RootElement, "Names");

            report.Findings.Add(new DiagnosticFinding
            {
                Category = "Accounts",
                Title = $"Локальных администраторов: {count}",
                Details = string.IsNullOrWhiteSpace(report.LocalAdministratorNames)
                    ? "Имена участников не получены."
                    : report.LocalAdministratorNames,
                Recommendation = "Проверьте, что все участники группы Administrators вам известны. Само количество не считается признаком взлома.",
                Severity = DiagnosticSeverity.Info
            });
        }

        private static void FinalizeReport(SecurityAuditReport report)
        {
            report.Score = Math.Clamp(report.Score, 0, 100);
            report.Status = report.Score switch
            {
                >= 90 => "NORMAL",
                >= 75 => "ATTENTION",
                >= 55 => "REVIEW REQUIRED",
                _ => "HIGH RISK"
            };
        }

        private static IntegrityCheckResult VerifyIntegrityBaseline(string dataFolder)
        {
            IntegrityCheckResult result = new();
            string baselinePath = GetBaselinePath(dataFolder);
            if (!File.Exists(baselinePath))
            {
                result.BaselineExists = false;
                result.Findings.Add(new DiagnosticFinding
                {
                    Category = "Integrity",
                    Title = "Baseline ещё не создан",
                    Details = "Сначала создайте эталон текущего состояния чувствительных пользовательских файлов.",
                    Recommendation = "Создавайте baseline, когда система находится в доверенном состоянии.",
                    Severity = DiagnosticSeverity.Info
                });
                return result;
            }

            result.BaselineExists = true;

            List<IntegrityBaselineEntry>? baseline;
            try
            {
                baseline = JsonSerializer.Deserialize<List<IntegrityBaselineEntry>>(File.ReadAllText(baselinePath));
            }
            catch
            {
                baseline = null;
            }

            if (baseline == null)
            {
                result.Findings.Add(new DiagnosticFinding
                {
                    Category = "Integrity",
                    Title = "Baseline повреждён или не читается",
                    Details = baselinePath,
                    Recommendation = "Создайте baseline заново после ручной проверки системы.",
                    Severity = DiagnosticSeverity.Warning
                });
                return result;
            }

            Dictionary<string, IntegrityBaselineEntry> current = CaptureIntegrityEntries()
                .ToDictionary(item => item.Path, StringComparer.OrdinalIgnoreCase);

            HashSet<string> baselinePaths = baseline
                .Select(item => item.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (IntegrityBaselineEntry expected in baseline)
            {
                result.Compared++;
                if (!current.TryGetValue(expected.Path, out IntegrityBaselineEntry? actual))
                {
                    result.Missing++;
                    result.Findings.Add(new DiagnosticFinding
                    {
                        Category = "Integrity",
                        Title = "Файл из baseline отсутствует",
                        Details = expected.Path,
                        Recommendation = "Если вы сами удалили или переместили файл — обновите baseline. Иначе выясните причину изменения.",
                        Severity = DiagnosticSeverity.Warning
                    });
                    continue;
                }

                if (!string.Equals(expected.Sha256, actual.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    result.Changed++;
                    result.Findings.Add(new DiagnosticFinding
                    {
                        Category = "Integrity",
                        Title = "Файл изменился после создания baseline",
                        Details = expected.Path,
                        Recommendation = "Изменение может быть легитимным. Сверьте время и причину изменения перед обновлением baseline.",
                        Severity = DiagnosticSeverity.Warning
                    });
                }
            }

            foreach (IntegrityBaselineEntry actual in current.Values)
            {
                if (baselinePaths.Contains(actual.Path))
                    continue;

                result.NewFiles++;
                result.Findings.Add(new DiagnosticFinding
                {
                    Category = "Integrity",
                    Title = "Новый файл в контролируемом расположении",
                    Details = actual.Path,
                    Recommendation = "Если файл появился после установки известной программы или был создан вами, это нормально. Неизвестные объекты в Startup/PowerShell профилях стоит проверить.",
                    Severity = DiagnosticSeverity.Info
                });
            }

            if (result.Changed == 0 && result.Missing == 0 && result.NewFiles == 0)
            {
                result.Findings.Add(new DiagnosticFinding
                {
                    Category = "Integrity",
                    Title = "Integrity baseline совпадает",
                    Details = $"Проверено файлов: {result.Compared}.",
                    Recommendation = "Контролируемые файлы не изменились с момента создания baseline.",
                    Severity = DiagnosticSeverity.Good
                });
            }

            return result;
        }

        private static List<IntegrityBaselineEntry> CaptureIntegrityEntries()
        {
            List<string> paths = BuildIntegrityTargets();
            List<IntegrityBaselineEntry> result = new();

            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (!File.Exists(path))
                        continue;

                    FileInfo info = new(path);
                    result.Add(new IntegrityBaselineEntry
                    {
                        Path = info.FullName,
                        Sha256 = ComputeSha256(info.FullName),
                        Length = info.Length,
                        LastWriteUtc = info.LastWriteTimeUtc
                    });
                }
                catch
                {
                }
            }

            return result;
        }

        private static List<string> BuildIntegrityTargets()
        {
            List<string> targets = new();

            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            targets.Add(Path.Combine(windows, "System32", "drivers", "etc", "hosts"));
            targets.Add(Path.Combine(documents, "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1"));
            targets.Add(Path.Combine(documents, "PowerShell", "Microsoft.PowerShell_profile.ps1"));

            foreach (string folder in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
            })
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                    continue;

                try
                {
                    targets.AddRange(Directory.GetFiles(folder));
                }
                catch
                {
                }
            }

            return targets;
        }

        private static string ComputeSha256(string path)
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash);
        }

        private static string GetBaselinePath(string dataFolder) =>
            Path.Combine(dataFolder, "security-integrity-baseline.json");

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

        private static bool? TryGetBool(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement value))
                return null;

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(value.GetString(), out bool parsed) => parsed,
                _ => null
            };
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

        private static int? TryGetNullableInt(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
                return null;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
                return number;

            return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out int parsed)
                ? parsed
                : null;
        }

        private static string TryGetString(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement value))
                return "";

            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
        }

        private static DateTime? TryGetDateTime(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
                return null;

            if (value.ValueKind == JsonValueKind.String && DateTime.TryParse(value.GetString(), out DateTime parsed))
                return parsed;

            return null;
        }
    }
}

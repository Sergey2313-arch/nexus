using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NEXUS.Security;

public sealed record SecurityFinding(string Severity, string Category, string Title, string Evidence, string Recommendation);
public sealed record ScanStage(string Name, bool Completed, string Details);
public sealed class DiagnosticResult
{
    public DateTime Timestamp { get; } = DateTime.Now;
    public List<SecurityFinding> Findings { get; } = new();
    public List<ScanStage> Stages { get; } = new();
    // A configuration risk indicator, not a probability of infection or hardware health.
    public int RiskScore => Math.Min(100, Findings.Sum(f => f.Severity == "Critical" ? 30 : f.Severity == "Warning" ? 10 : 0));
    public bool IsComplete => Stages.Count == 8 && Stages.All(s => s.Completed);
}

public sealed class SecurityScannerService
{
    private const string FileAnalysis = """
        function InspectFile($path, $category) {
            if (-not $path) { return }
            $path = [Environment]::ExpandEnvironmentVariables($path)
            if ($path -match '^"([^\"]+)"') { $path = $Matches[1] }
            elseif ($path -match '^(.+?\.(exe|dll|sys))(?=\s|$)') { $path = $Matches[1] }
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return }
            $risky = $path.StartsWith($env:TEMP + '\', [StringComparison]::OrdinalIgnoreCase) -or $path.StartsWith($env:APPDATA + '\', [StringComparison]::OrdinalIgnoreCase)
            if (-not $risky) { return }
            $sig = Get-AuthenticodeSignature -LiteralPath $path -ErrorAction Stop
            if ($sig.Status -ne 'Valid') {
                [pscustomobject]@{ Severity='Warning'; Category=$category; Title='Файл в пользовательском каталоге без подтверждённой подписи'; Evidence=($path + ' | Authenticode: ' + $sig.Status); Recommendation='Проверьте происхождение файла и выполните проверку Defender. Этот признак сам по себе не доказывает заражение.' }
            }
        }
        """;

    private static readonly (string Name, string Script)[] Checks =
    {
        ("Процессы", "Get-CimInstance Win32_Process -ErrorAction Stop | ForEach-Object { InspectFile $_.ExecutablePath 'Processes' }"),
        ("Автозагрузка", "Get-CimInstance Win32_StartupCommand -ErrorAction Stop | ForEach-Object { InspectFile $_.Command 'Startup' }"),
        ("Службы Windows", "Get-CimInstance Win32_Service -ErrorAction Stop | Where-Object StartMode -eq 'Auto' | ForEach-Object { InspectFile $_.PathName 'Services' }"),
        ("Подозрительные файлы", "Get-ChildItem -LiteralPath $env:TEMP -File -Filter *.exe -ErrorAction Stop | Select-Object -First 200 | ForEach-Object { InspectFile $_.FullName 'Files' }"),
        ("Сетевые соединения", "Get-NetTCPConnection -State Established -ErrorAction Stop | ForEach-Object { $p = Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue; if ($p) { InspectFile $p.Path 'Network' } }"),
        ("Windows Defender", """
            $d = Get-MpComputerStatus -ErrorAction Stop
            if (-not $d.AntivirusEnabled -or -not $d.RealTimeProtectionEnabled) {
                [pscustomobject]@{ Severity='Warning'; Category='Defender'; Title='Защита Defender отключена'; Evidence=('Antivirus=' + $d.AntivirusEnabled + '; RealTime=' + $d.RealTimeProtectionEnabled); Recommendation='Проверьте, работает ли другой антивирус. Если нет, включите защиту Windows.' }
            }
            if ($d.AntivirusSignatureAge -gt 7) {
                [pscustomobject]@{ Severity='Warning'; Category='Defender'; Title='Базы Defender устарели'; Evidence=('Возраст баз: ' + $d.AntivirusSignatureAge + ' дней'); Recommendation='Обновите базы в разделе Безопасность Windows.' }
            }
            Get-MpThreatDetection -ErrorAction Stop | Where-Object { $_.ActionSuccess -eq $false } | ForEach-Object {
                [pscustomobject]@{ Severity='Critical'; Category='Defender'; Title='Defender: действие над обнаруженной угрозой не завершено'; Evidence=('ThreatID=' + $_.ThreatID + '; ' + ($_.Resources -join ', ')); Recommendation='Откройте журнал защиты Windows и проверьте текущий статус обнаружения.' }
            }
            """),
        ("Настройки безопасности", """
            Get-NetFirewallProfile -ErrorAction Stop | Where-Object { -not $_.Enabled } | ForEach-Object {
                [pscustomobject]@{ Severity='Warning'; Category='Configuration'; Title='Профиль брандмауэра отключён'; Evidence=$_.Name; Recommendation='Проверьте настройки брандмауэра и наличие альтернативной защиты.' }
            }
            $uac = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -ErrorAction Stop
            if ($uac.EnableLUA -eq 0) {
                [pscustomobject]@{ Severity='Warning'; Category='Configuration'; Title='UAC отключён'; Evidence='EnableLUA=0'; Recommendation='Включите контроль учётных записей Windows.' }
            }
            """),
        ("Корреляция признаков", "")
    };

    public async Task<DiagnosticResult> ScanAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        var result = new DiagnosticResult();
        for (int i = 0; i < Checks.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var check = Checks[i];
            progress.Report($"[{i + 1}/8] {check.Name}");
            try
            {
                if (i == 7)
                {
                    var unique = result.Findings.DistinctBy(f => (f.Category, f.Evidence, f.Title)).ToList();
                    result.Findings.Clear();
                    result.Findings.AddRange(unique);
                    foreach (var group in unique.Where(f => f.Evidence.Contains(" | Authenticode:")).GroupBy(f => f.Evidence))
                    {
                        if (group.Select(f => f.Category).Distinct().Count() >= 2)
                            result.Findings.Add(new("Warning", "Correlation", "Файл встречается в нескольких источниках", group.Key, "Сопоставьте процесс, автозагрузку, службу и соединения; проверьте файл Defender."));
                    }
                }
                else result.Findings.AddRange(await RunCheckAsync(check.Script, cancellationToken));
                result.Stages.Add(new(check.Name, true, i == 3 ? "Проверены до 200 EXE в корне Temp; это выборочная проверка." : "Проверка выполнена. Недоступные пути процессов могут отсутствовать."));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { result.Stages.Add(new(check.Name, false, ex.Message)); }
        }
        return result;
    }

    private static async Task<List<SecurityFinding>> RunCheckAsync(string script, CancellationToken cancellationToken)
    {
        string wrapped = "$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[System.Text.UTF8Encoding]::new(); " + FileAnalysis + "\n$items = @( & { " + script + " } ); ConvertTo-Json -InputObject $items -Depth 5 -Compress";
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapped)));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить PowerShell.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(output, error);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("Проверка превысила 45 секунд.");
        }
        string json = await output;
        string stderr = await error;
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? "Проверка недоступна." : stderr.Trim());
        return JsonSerializer.Deserialize<List<SecurityFinding>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
    }
}

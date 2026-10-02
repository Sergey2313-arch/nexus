using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NEXUS.Security;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
var result = new DiagnosticResult();
Check(!result.IsComplete, "Empty scan must not claim completion");
for (int i = 0; i < 8; i++) result.Stages.Add(new("Stage", true, ""));
Check(result.IsComplete, "All eight successful stages should complete the scan");
result.Stages[3] = new("Files", false, "Access denied");
Check(!result.IsComplete, "Access denial must produce a partial result");
result.Findings.Add(new("Warning", "Files", "title", "path", "recommendation"));
result.Findings.Add(new("Critical", "Defender", "title", "path", "recommendation"));
Check(result.RiskScore == 40, "Warning and Critical must contribute to risk");
for (int i = 0; i < 10; i++) result.Findings.Add(new("Critical", "Defender", "title", "path", "recommendation"));
Check(result.RiskScore == 100, "Risk indicator must be capped");
var findings = JsonSerializer.Deserialize<SecurityFinding[]>("[{\"Severity\":\"Warning\",\"Category\":\"Files\",\"Title\":\"Тест\",\"Evidence\":\"C:\\\\Temp\\\\x.exe\",\"Recommendation\":\"Проверить\"}]");
Check(findings?[0].Title == "Тест", "PowerShell JSON contract must preserve Unicode");
using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try
{
    await new SecurityScannerService().ScanAsync(new Progress<string>(), cancelled.Token);
    throw new Exception("Cancelled scan should not launch PowerShell");
}
catch (OperationCanceledException) { }
Console.WriteLine("Security scanner contract checks passed.");

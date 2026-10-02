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

var complete = new DiagnosticResult();
for (int i = 0; i < 8; i++) complete.Stages.Add(new("Stage", true, ""));
var partialHardware = HealthAnalyzer.Analyze(complete, new[] { new HardwareReading("RAM", "RAM Load", 30) });
Check(partialHardware.OverallScore == null, "Missing sensors must not yield a healthy overall score");
var hot = HealthAnalyzer.Analyze(complete, new[] { new HardwareReading("CPU", "CPU Temperature", 95) });
Check(hot.Findings.Count == 1 && hot.Findings[0].Severity == "Critical", "CPU overheating needs a critical recommendation");
Check(HealthAnalyzer.Analyze(complete, Array.Empty<HardwareReading>()).HardwareScore == null, "No hardware data means unknown health");
complete.Findings.Add(new("Warning", "Files", "<script>alert(1)</script>", "<img src=x>", "<b>test</b>"));
var report = DiagnosticReportWriter.Save(complete, partialHardware, Array.Empty<HardwareReading>());
var html = System.IO.File.ReadAllText(report);
Check(!html.Contains("<script>alert(1)</script>") && html.Contains("&lt;script&gt;"), "Report must HTML-escape file/event text");
Check(System.IO.File.Exists(System.IO.Path.ChangeExtension(report, ".json")), "JSON report must accompany HTML");
System.IO.File.Delete(report);
System.IO.File.Delete(System.IO.Path.ChangeExtension(report, ".json"));

if (OperatingSystem.IsWindows())
{
    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
    var helper = (string)typeof(SecurityScannerService).GetField("FileAnalysis", flags)!.GetRawConstantValue()!;
    var checks = ((string Name, string Script)[])typeof(SecurityScannerService).GetField("Checks", flags)!.GetValue(null)!;
    var scripts = new System.Collections.Generic.List<string> { helper };
    foreach (var check in checks) scripts.Add(helper + "\n" + check.Script);
    foreach (var script in scripts)
    {
        string scriptPath = System.IO.Path.GetTempFileName();
        System.IO.File.WriteAllText(scriptPath, script, System.Text.Encoding.UTF8);
        string command = "$errors=$null; $tokens=$null; [System.Management.Automation.Language.Parser]::ParseFile('" + scriptPath.Replace("'", "''") + "', [ref]$tokens, [ref]$errors) | Out-Null; if ($errors.Count) { $errors | Out-String | Write-Error; exit 1 }";
        var start = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-EncodedCommand"); start.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command)));
        using var process = System.Diagnostics.Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Check(process.ExitCode == 0, "PowerShell parser failed: " + await stderr);
        await stdout;
        System.IO.File.Delete(scriptPath);
    }
    var runner = typeof(SecurityScannerService).GetMethod("RunCheckAsync", flags)!;
    string fixture = "[pscustomobject]@{Severity='Warning';Category='Test';Title='Тест';Evidence='path';Recommendation='check'}; throw 'fixture error'";
    var task = (Task)runner.Invoke(null, new object[] { fixture, CancellationToken.None, 20 })!;
    await task;
    var output = task.GetType().GetProperty("Result")!.GetValue(task)!;
    var collected = (System.Collections.Generic.List<SecurityFinding>)output.GetType().GetProperty("Findings")!.GetValue(output)!;
    var failure = (string)output.GetType().GetProperty("Error")!.GetValue(output)!;
    Check(collected.Count == 1 && collected[0].Title == "Тест" && failure.Contains("fixture error"), "PowerShell partial failure must preserve previous findings and Unicode");
}
Console.WriteLine("Hardware, report escaping, and PowerShell syntax checks passed.");

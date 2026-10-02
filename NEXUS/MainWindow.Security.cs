using Microsoft.UI.Xaml;
using NEXUS.Security;
using System;
using System.Linq;
using System.Threading;

namespace NEXUS;

public sealed partial class MainWindow
{
    private CancellationTokenSource? _securityScanCancellation;
    private bool _securityWindowClosed;

    private async void ScanSystemButton_Click(object sender, RoutedEventArgs e)
    {
        if (_securityScanCancellation != null) return;
        using var cancellation = new CancellationTokenSource();
        _securityScanCancellation = cancellation;
        ScanSystemButton.IsEnabled = false;
        OpenDiagnosticReportButton.IsEnabled = false;
        _diagnosticReportPath = null;
        CancelSecurityScanButton.IsEnabled = true;
        SecurityResultsText.Text = "";
        SecurityScanProgress.IsIndeterminate = true;
        try
        {
            var readings = CaptureHardwareReadings();
            var progress = new Progress<string>(stage =>
            {
                if (!_securityWindowClosed) SecurityScanStatusText.Text = stage;
            });
            var result = await new SecurityScannerService().ScanAsync(progress, cancellation.Token);
            if (_securityWindowClosed) return;
            var health = HealthAnalyzer.Analyze(result, readings);
            string hardwareSummary = $"Health Score: {health.OverallScore?.ToString() ?? "недостаточно данных"} • Железо: {health.HardwareScore?.ToString() ?? "нет данных"}/100 ({(health.HardwareComplete ? "ключевые датчики" : "частичные данные")}) • Безопасность: {health.SecurityScore}/100\n";
            var allFindings = result.Findings.Concat(health.Findings).ToList();
            SecurityScanStatusText.Text = $"{(result.IsComplete ? "Проверка завершена" : "Проверка частичная")} • {result.Timestamp:HH:mm:ss}";
            SecurityResultsText.Text = hardwareSummary + $"Индикатор риска конфигурации: {result.RiskScore}/100\n" +
                $"Critical: {allFindings.Count(f => f.Severity == "Critical")} • Warning: {allFindings.Count(f => f.Severity == "Warning")}\n" +
                "Это эвристическая диагностика, а не заключение об отсутствии заражения. SFC/DISM доступны при запуске от администратора. Отсутствующие датчики и недоступные объекты ограничивают оценку.\n\n" +
                string.Join("\n", result.Stages.Select(s => $"{(s.Completed ? "✓" : "? Недоступно:")} {s.Name}: {s.Details}")) + "\n\n" +
                (allFindings.Count == 0 ? "В доступных проверках подозрительных признаков не обнаружено." :
                string.Join("\n\n", allFindings.Select(f => $"[{f.Severity}] {f.Title}\n{f.Evidence}\nЧто делать: {f.Recommendation}")));
            foreach (var finding in allFindings)
                _logService.Write("Security", finding.Category, "SecurityScanner", finding.Title,
                    finding.Evidence + "\n" + finding.Recommendation, severity: finding.Severity);
            try
            {
                _diagnosticReportPath = DiagnosticReportWriter.Save(result, health, readings);
                OpenDiagnosticReportButton.IsEnabled = true;
                SecurityResultsText.Text += "\n\nHTML и JSON отчёты: " + _diagnosticReportPath;
            }
            catch (Exception ex) { SecurityResultsText.Text += "\nНе удалось сохранить отчёт: " + ex.Message; }
            _logService.Write("Security", "ScanCompleted", "SecurityScanner", SecurityScanStatusText.Text,
                SecurityResultsText.Text, severity: result.IsComplete ? "Info" : "Warning");
        }
        catch (OperationCanceledException)
        {
            if (!_securityWindowClosed) SecurityScanStatusText.Text = "Проверка отменена. Результат не сформирован.";
        }
        catch (Exception ex)
        {
            if (!_securityWindowClosed) SecurityScanStatusText.Text = "Ошибка проверки: " + ex.Message;
        }
        finally
        {
            _securityScanCancellation = null;
            if (!_securityWindowClosed)
            {
                ScanSystemButton.IsEnabled = true;
                CancelSecurityScanButton.IsEnabled = false;
                SecurityScanProgress.IsIndeterminate = false;
            }
        }
    }

    private void CancelSecurityScanButton_Click(object sender, RoutedEventArgs e) => _securityScanCancellation?.Cancel();
}

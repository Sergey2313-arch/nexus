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
        CancelSecurityScanButton.IsEnabled = true;
        SecurityResultsText.Text = "";
        SecurityScanProgress.IsIndeterminate = true;
        try
        {
            var progress = new Progress<string>(stage =>
            {
                if (!_securityWindowClosed) SecurityScanStatusText.Text = stage;
            });
            var result = await new SecurityScannerService().ScanAsync(progress, cancellation.Token);
            if (_securityWindowClosed) return;
            SecurityScanStatusText.Text = $"{(result.IsComplete ? "Проверка завершена" : "Проверка частичная")} • {result.Timestamp:HH:mm:ss}";
            SecurityResultsText.Text = $"Индикатор риска конфигурации: {result.RiskScore}/100\n" +
                $"Critical: {result.Findings.Count(f => f.Severity == "Critical")} • Warning: {result.Findings.Count(f => f.Severity == "Warning")}\n" +
                "Это эвристическая диагностика, а не заключение об отсутствии заражения. Целостность системных файлов (SFC/DISM) здесь не проверяется.\n\n" +
                string.Join("\n", result.Stages.Select(s => $"{(s.Completed ? "✓" : "? Недоступно:")} {s.Name}: {s.Details}")) + "\n\n" +
                (result.Findings.Count == 0 ? "В доступных проверках подозрительных признаков не обнаружено." :
                string.Join("\n\n", result.Findings.Select(f => $"[{f.Severity}] {f.Title}\n{f.Evidence}\nЧто делать: {f.Recommendation}")));
            foreach (var finding in result.Findings)
                _logService.Write("Security", finding.Category, "SecurityScanner", finding.Title,
                    finding.Evidence + "\n" + finding.Recommendation, severity: finding.Severity);
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

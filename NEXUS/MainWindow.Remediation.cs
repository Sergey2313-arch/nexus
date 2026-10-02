using Microsoft.UI.Xaml;
using NEXUS.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NEXUS;

public sealed partial class MainWindow
{
    private DiagnosticSnapshot? _previousDiagnosticSnapshot;
    private bool _snapshotLoaded;
    private bool _remediationHistoryWriteFailed;
    private readonly HashSet<string> _activeRemediationActions = new();
    public sealed record ComparisonCard(string Status, string Title, string Evidence, string Details)
    {
        public Microsoft.UI.Xaml.Media.SolidColorBrush Accent => Status == "Больше не обнаружено" ? Brush(0x69,0xD4,0xD0) : Brush(0xFF,0xC8,0x57);
    }
    public sealed record ActionHistoryCard(string Time, string Title, string Status, string Details)
    {
        public Microsoft.UI.Xaml.Media.SolidColorBrush Accent => Status == "Ошибка" ? Brush(0xFF,0x75,0x75) : Status is "Выполнено частично" or "Завершение неизвестно" ? Brush(0xFF,0xC8,0x57) : Brush(0x69,0xD4,0xD0);
    }

    private void UpdateDiagnosticComparison(DiagnosticResult result, HealthAssessment health, IReadOnlyList<HardwareReading> readings)
    {
        try
        {
            if (!_snapshotLoaded) { _snapshotLoaded = true; _previousDiagnosticSnapshot = DiagnosticSnapshotStore.Load(); }
            var current = new DiagnosticSnapshot(result.Timestamp, result.Findings.Concat(health.Findings).ToList(), result.Stages.ToList(), readings.ToList());
            if (_previousDiagnosticSnapshot == null)
            {
                DiagnosticComparisonList.ItemsSource = null;
                DiagnosticComparisonStatusText.Text = $"Первый снимок: {current.Timestamp:dd.MM.yyyy HH:mm:ss}. После следующей проверки появится сравнение.";
            }
            else
            {
                var changes = DiagnosticComparison.Compare(_previousDiagnosticSnapshot, current);
                DiagnosticComparisonList.ItemsSource = changes.Select(c => new ComparisonCard(c.Status, c.Finding.Title, c.Finding.Evidence, c.Details)).ToList();
                DiagnosticComparisonStatusText.Text = $"{_previousDiagnosticSnapshot.Timestamp:dd.MM HH:mm:ss} → {current.Timestamp:dd.MM HH:mm:ss}. " + string.Join(" • ", changes.GroupBy(c => c.Status).Select(g => $"{g.Key}: {g.Count()}"));
                if (changes.Count == 0) DiagnosticComparisonStatusText.Text += "Предупреждений в сравниваемых снимках нет; оцените покрытие диагностики.";
                current = DiagnosticComparison.PreserveUnverified(current, changes);
            }
            _previousDiagnosticSnapshot = current;
            DiagnosticSnapshotStore.Save(current);
        }
        catch (Exception ex) { DiagnosticComparisonStatusText.Text = "Сравнение или сохранение снимка недоступно: " + ex.Message; }
    }
    private async Task<T> RunTrackedActionAsync<T>(string title, Func<Task<T>> operation, Func<T, string> details, Func<T, string>? status = null)
    {
        string id = Guid.NewGuid().ToString("N");
        _activeRemediationActions.Add(id);
        RecordRemediationAction(id, "Started", title, "Действие запущено. Результат и повторная диагностика ещё не получены.");
        try
        {
            T result = await operation();
            RecordRemediationAction(id, status?.Invoke(result) ?? "Completed", title, details(result));
            return result;
        }
        catch (Exception ex)
        {
            bool cancelled = ex is OperationCanceledException || ex is System.ComponentModel.Win32Exception win32 && win32.NativeErrorCode == 1223;
            RecordRemediationAction(id, cancelled ? "Cancelled" : "Failed", title, ex.Message);
            throw;
        }
        finally { _activeRemediationActions.Remove(id); if (!_securityWindowClosed) RefreshRemediationHistory(); }
    }
    private void RecordRemediationAction(string id, string state, string title, string details)
    {
        if (!_logService.Write("Action", state, id, title, details, severity: state is "Failed" or "Partial" ? "Warning" : "Info")) _remediationHistoryWriteFailed = true;
        if (!_securityWindowClosed) RefreshRemediationHistory();
    }
    private void RecordRemediationCancellation(string title)
    {
        RecordRemediationAction(Guid.NewGuid().ToString("N"), "Cancelled", title, "Отменено в диалоге подтверждения; операция не запускалась.");
    }
    private void RefreshRemediationHistoryButton_Click(object sender, RoutedEventArgs e) => RefreshRemediationHistory();
    private void RefreshRemediationHistory()
    {
        try
        {
            var rows = _logService.GetLatest(1000, "Action").GroupBy(e => e.Category == "Action" ? e.Source : "legacy-" + e.Id).Select(group =>
            {
                var latest = group.OrderByDescending(e => e.Id).First();
                var first = group.OrderBy(e => e.Id).First();
                string status = latest.Category != "Action" ? "Ранее записанный результат" : latest.EventType switch
                {
                    "Started" => _activeRemediationActions.Contains(latest.Source) ? "Выполняется" : "Завершение неизвестно", "Completed" => "Действие завершено", "Failed" => "Ошибка", "Partial" => "Выполнено частично", "Cancelled" => "Отменено", _ => latest.EventType
                };
                return new ActionHistoryCard($"{first.Timestamp:dd.MM.yyyy HH:mm:ss} → {latest.Timestamp:HH:mm:ss}", latest.Title, status, latest.Details + (latest.EventType == "Completed" ? "\nПовторите диагностику: завершение действия не подтверждает устранение проблемы." : ""));
            }).Take(200).ToList();
            RemediationHistoryList.ItemsSource = rows;
            RemediationHistoryStatusText.Text = rows.Count == 0 ? "Действия пока не записаны. История появится после выполнения встроенных исправлений и очистки." : $"Последних действий: {rows.Count}. История сохраняется между запусками; результат повторной проверки показан в сравнении диагностики.";
            if (_remediationHistoryWriteFailed) RemediationHistoryStatusText.Text += " Не все записи удалось сохранить в этом сеансе; история может быть неполной.";
        }
        catch (Exception ex) { RemediationHistoryStatusText.Text = "История недоступна: " + ex.Message; }
    }
}

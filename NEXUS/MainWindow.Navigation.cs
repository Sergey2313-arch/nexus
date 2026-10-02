using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NEXUS.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;

namespace NEXUS;

public sealed partial class MainWindow
{
    private readonly List<SecurityFinding> _displayFindings = new();
    private bool _hasDiagnosticResult;
    private int _activeScanStage;
    private static readonly string[] StageNames = { "Процессы", "Автозагрузка и задания", "Службы Windows", "Файлы", "Сетевые соединения", "Windows Defender", "Целостность и события", "Анализ признаков" };

    public sealed class StageCard
    {
        public string Name { get; init; } = "";
        public string Details { get; init; } = "";
        public string Symbol { get; init; } = "○";
        public SolidColorBrush Accent { get; init; } = Brush(0x91, 0xA1, 0xB8);
    }

    public sealed class FindingCard
    {
        public string Label { get; init; } = "";
        public string Title { get; init; } = "";
        public string Evidence { get; init; } = "";
        public string Recommendation { get; init; } = "";
        public SolidColorBrush Accent { get; init; } = Brush(0x91, 0xA1, 0xB8);
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromArgb(255, r, g, b));

    private void NavigateTo(string page)
    {
        _isLogbookVisible = page == "logbook";
        DashboardView.Visibility = page == "overview" ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsView.Visibility = page == "diagnostics" ? Visibility.Visible : Visibility.Collapsed;
        LogbookView.Visibility = page == "logbook" ? Visibility.Visible : Visibility.Collapsed;
        PageTitleText.Text = page == "overview" ? "Обзор системы" : page == "diagnostics" ? "Диагностика" : "Журнал событий";
        foreach (var item in new[] { (OverviewNavButton, "overview"), (DiagnosticsNavButton, "diagnostics"), (LogbookNavButton, "logbook") })
        {
            item.Item1.Background = item.Item2 == page ? Brush(0x1C, 0x3A, 0x4B) : Brush(0x10, 0x17, 0x23);
            item.Item1.Foreground = item.Item2 == page ? Brush(0x69, 0xD4, 0xD0) : Brush(0xA7, 0xB5, 0xC8);
        }
    }

    private void DiagnosticsButton_Click(object sender, RoutedEventArgs e) => NavigateTo("diagnostics");

    private void InitializeDiagnosticsView()
    {
        NavigateTo("overview");
        ScanStagesList.ItemsSource = StageNames.Select(name => new StageCard { Name = name, Details = "Ожидает запуска" }).ToList();
    }

    private void ResetDiagnosticCards()
    {
        _hasDiagnosticResult = false;
        _activeScanStage = 0;
        _displayFindings.Clear();
        OverallHealthText.Text = SecurityHealthText.Text = HardwareHealthText.Text = "—";
        OverallHealthDetailText.Text = SecurityHealthDetailText.Text = HardwareHealthDetailText.Text = "Проверка выполняется";
        DiagnosticReadingsList.ItemsSource = null;
        SecurityScanProgress.Value = 0;
        RefreshFindingCards();
        ScanStagesList.ItemsSource = StageNames.Select(name => new StageCard { Name = name, Details = "В очереди" }).ToList();
    }

    private void UpdateScanStage(string status)
    {
        SecurityScanStatusText.Text = status;
        if (status.Length > 2 && char.IsDigit(status[1])) _activeScanStage = status[1] - '0';
        SecurityScanProgress.Value = Math.Max(0, _activeScanStage - 1);
        ScanStagesList.ItemsSource = StageNames.Select((name, i) => new StageCard
        {
            Name = name,
            Symbol = i + 1 == _activeScanStage ? "●" : i + 1 < _activeScanStage ? "·" : "○",
            Details = i + 1 == _activeScanStage ? "Проверяется сейчас" : i + 1 < _activeScanStage ? "Этап обработан; результат появится в итогах" : "В очереди",
            Accent = i + 1 == _activeScanStage ? Brush(0x69, 0xD4, 0xD0) : Brush(0x91, 0xA1, 0xB8)
        }).ToList();
    }

    private void ShowDiagnosticCards(DiagnosticResult result, HealthAssessment health, IReadOnlyList<HardwareReading> readings)
    {
        _hasDiagnosticResult = true;
        _displayFindings.Clear();
        _displayFindings.AddRange(result.Findings.Concat(health.Findings));
        OverallHealthText.Text = health.OverallScore.HasValue ? health.OverallScore + "/100" : "—";
        OverallHealthDetailText.Text = health.OverallScore.HasValue ? "По доступным признакам" : "Недостаточно данных для общей оценки";
        SecurityHealthText.Text = health.SecurityScore + "/100";
        SecurityHealthDetailText.Text = health.SecurityComplete ? "Все этапы обработаны" : "Частичная проверка";
        HardwareHealthText.Text = health.HardwareScore.HasValue ? health.HardwareScore + "/100" : "—";
        HardwareHealthDetailText.Text = health.HardwareComplete ? "Ключевые показатели доступны" : "Часть показателей недоступна";
        SecurityScanProgress.Value = 8;
        ScanCoverageText.Text = $"Выполнено этапов: {result.Stages.Count(s => s.Completed)}/8. Критических находок: {_displayFindings.Count(f => f.Severity == "Critical")}. Предупреждений: {_displayFindings.Count(f => f.Severity == "Warning")}.";
        ScanStagesList.ItemsSource = result.Stages.Select(s => new StageCard { Name = s.Name, Details = s.Details, Symbol = s.Completed ? "✓" : "!", Accent = s.Completed ? Brush(0x69, 0xD4, 0xD0) : Brush(0xFF, 0xC8, 0x57) }).ToList();
        DiagnosticReadingsList.ItemsSource = readings.Count == 0 ? new[] { "Датчики недоступны" } : readings.Select(r => $"{r.Device} • {MetricName(r.Metric)}: {r.Value:F1}{(r.Metric.Contains("Temperature") ? " °C" : " %")}").ToArray();
        RefreshFindingCards();
    }

    private static string MetricName(string metric) => metric switch
    {
        "CPU Temperature" => "Температура CPU", "GPU Temperature" => "Температура GPU", "Storage Temperature" => "Температура накопителя", "Storage Health" => "Остаточный ресурс", "Disk Used" => "Занято места", "RAM Load" => "Загрузка RAM", _ => metric
    };

    private void FindingsFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SecurityFindingsList != null && FindingsEmptyPanel != null) RefreshFindingCards();
    }

    private void RefreshFindingCards()
    {
        int filter = FindingsFilterComboBox.SelectedIndex;
        var filtered = _displayFindings.Where(f => filter switch { 1 => f.Severity == "Critical", 2 => f.Severity == "Warning", 3 => true, _ => f.Severity != "Info" }).OrderBy(f => f.Severity == "Critical" ? 0 : f.Severity == "Warning" ? 1 : 2).ToList();
        SecurityFindingsList.ItemsSource = filtered.Take(200).Select(f => new FindingCard
        {
            Label = f.Severity == "Critical" ? "КРИТИЧЕСКОЕ • " + f.Category : f.Severity == "Warning" ? "ТРЕБУЕТ ВНИМАНИЯ • " + f.Category : "СВЕДЕНИЯ • " + f.Category,
            Title = f.Title, Evidence = f.Evidence, Recommendation = f.Recommendation,
            Accent = f.Severity == "Critical" ? Brush(0xFF, 0x75, 0x75) : f.Severity == "Warning" ? Brush(0xFF, 0xC8, 0x57) : Brush(0x91, 0xA1, 0xB8)
        }).ToList();
        FindingsCountText.Text = _hasDiagnosticResult ? $"Найдено: {_displayFindings.Count}. По фильтру: {filtered.Count}." + (filtered.Count > 200 ? " Показаны первые 200; полный список в отчёте." : "") : "Результаты появятся после проверки";
        FindingsEmptyPanel.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FindingsEmptyTitleText.Text = _hasDiagnosticResult ? "По выбранному фильтру находок нет" : _securityScanCancellation != null ? "Проверяем систему" : "Начните с проверки системы";
        FindingsEmptyDetailText.Text = _hasDiagnosticResult ? "Оцените покрытие проверки и информационные сведения. Отсутствие предупреждений не гарантирует отсутствие угроз." : "Здесь появятся объяснения найденных признаков и рекомендации.";
    }

    private void StopDiagnosticCards(string message)
    {
        OverallHealthDetailText.Text = SecurityHealthDetailText.Text = HardwareHealthDetailText.Text = message;
        ScanCoverageText.Text = "Проверка не завершена. Итоговая оценка не сформирована.";
        ScanStagesList.ItemsSource = StageNames.Select((name, i) => new StageCard { Name = name, Symbol = "—", Details = i + 1 < _activeScanStage ? "Этап обработан; итог не сформирован" : "Проверка не завершена" }).ToList();
        RefreshFindingCards();
        FindingsEmptyTitleText.Text = message;
    }
}

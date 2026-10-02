using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NEXUS.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace NEXUS;

public sealed partial class MainWindow
{
    private readonly List<ChatTurn> _aiHistory = new();
    private CancellationTokenSource? _aiRequestCancellation;
    private bool _repairRunning;
    private bool _aiSettingsLoaded;
    public sealed record AssistantTaskItem(string Id, string Title, string Details, string ButtonText);

    private void AiButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo("ai");
        LoadAiConnectionSettings();
        AssistantTasksList.ItemsSource = new[]
        {
            new AssistantTaskItem("scan", "Проверить причины проблем", "Диагностика процессов, автозагрузки, защиты, целостности и железа. Недоступные этапы отмечаются отдельно.", "Запустить диагностику"),
            new AssistantTaskItem("temp", "Освободить место на диске", "Анализ Temp старше 7 дней → список и объём → удаление после вашего подтверждения.", "Анализировать временные файлы"),
            new AssistantTaskItem("ram", "Уменьшить потребление RAM", "Откроет список доступных приложений. Выберите процесс и используйте кнопку освобождения его рабочего набора.", "Выбрать приложение"),
            new AssistantTaskItem("sfc", "Восстановить системные файлы", "SFC /scannow в отдельном окне Windows с правами администратора. Может изменять повреждённые системные файлы.", "Запустить восстановление SFC"),
            new AssistantTaskItem("dism", "Восстановить хранилище компонентов", "DISM /RestoreHealth с правами администратора. Может использовать Windows Update и занять длительное время.", "Запустить восстановление DISM"),
            new AssistantTaskItem("storage", "Настроить очистку Windows", "Откроет системные параметры хранилища. Дальнейшие действия выбираете в Windows.", "Открыть параметры хранилища")
        };
    }

    private string BuildAiContext()
    {
        if (AiShareContextCheckBox.IsChecked != true) return "Показатели ПК не переданы. Не делай выводов о его состоянии без уточнений.";
        var text = new StringBuilder("Текущие показания (снимок):\n");
        foreach (var reading in CaptureHardwareReadings().Take(40)) text.AppendLine($"{reading.Metric}: {reading.Value:F1}");
        text.AppendLine(_hasDiagnosticResult ? "Есть результаты последней проверки, они могут отличаться от текущего состояния." : "Полная диагностика ещё не запускалась или не завершена.");
        foreach (var finding in _displayFindings.Where(f => f.Severity != "Info").Take(20))
            text.AppendLine($"{finding.Severity}: {finding.Title.Replace('\n', ' ').Replace('\r', ' ')[..Math.Min(finding.Title.Length, 160)]}");
        return text.ToString();
    }

    private async void AiSendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_aiRequestCancellation != null) return;
        string question = AiQuestionBox.Text.Trim();
        if (question.Length == 0) { AiStatusText.Text = "Напишите вопрос."; return; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_controlsLifetime.Token);
        _aiRequestCancellation = cancellation;
        SetAiRequestBusy(true);
        AiStatusText.Text = "Ожидаем ответ модели…";
        try
        {
            using var client = new AiAssistantService();
            string reply = await client.AskAsync(AiEndpointBox.Text, AiModelBox.Text, AiApiKeyBox.Password, BuildAiContext(), _aiHistory, question, cancellation.Token);
            if (_securityWindowClosed) return;
            _aiHistory.Add(new("user", question)); _aiHistory.Add(new("assistant", reply));
            while (_aiHistory.Count > 12) _aiHistory.RemoveRange(0, 2);
            AiChatText.Text = string.Join("\n\n", _aiHistory.Select(t => (t.role == "user" ? "ВЫ\n" : "ИИ\n") + t.content));
            AiQuestionBox.Text = "";
            AiStatusText.Text = "Ответ получен. Модель не выполняла действия на ПК.";
        }
        catch (OperationCanceledException) { if (!_securityWindowClosed) AiStatusText.Text = cancellation.IsCancellationRequested ? "Запрос отменён." : "Модель не ответила за 120 секунд."; }
        catch (Exception ex) { if (!_securityWindowClosed) AiStatusText.Text = "Не удалось получить ответ: " + ex.Message; }
        finally
        {
            _aiRequestCancellation = null;
            if (!_securityWindowClosed) SetAiRequestBusy(false);
        }
    }
    private void AiCancelButton_Click(object sender, RoutedEventArgs e) => _aiRequestCancellation?.Cancel();
    private void AiClearButton_Click(object sender, RoutedEventArgs e) { _aiHistory.Clear(); AiChatText.Text = "Чат очищен. Встроенные задачи остаются доступны."; }
    private void AiRefreshPlanButton_Click(object sender, RoutedEventArgs e)
    {
        var plan = new List<string>();
        var readings = CaptureHardwareReadings();
        if (readings.Any(r => r.Metric == "Disk Used" && r.Value >= 90)) plan.Add("Мало места: начните с анализа Temp, затем проверьте хранилище Windows.");
        if (readings.Any(r => r.Metric == "RAM Load" && r.Value >= 90)) plan.Add("RAM загружена: посмотрите приложения и выберите потребляющее память. Освобождение рабочего набора временно; проверьте причины потребления.");
        if (readings.Any(r => r.Metric.Contains("Temperature") && r.Value >= (r.Metric == "Storage Temperature" ? 60 : 80))) plan.Add("Повышенная температура: проверьте нагрузку, вентиляторы и охлаждение. Программная очистка не заменяет обслуживание охлаждения.");
        if (_displayFindings.Any(f => f.Category == "Integrity" && f.Severity != "Info")) plan.Add("Есть признаки нарушения целостности: изучите отчёт; при подтверждении начните с DISM, затем SFC.");
        foreach (var finding in _displayFindings.Where(f => f.Severity != "Info").Take(8)) plan.Add(finding.Title + "\n" + finding.Recommendation);
        if (!_hasDiagnosticResult) plan.Add("Запустите диагностику: сейчас нет завершённого результата проверки безопасности.");
        if (plan.Count == 0) plan.Add("По доступным текущим показателям срочных задач не найдено. Проверьте покрытие диагностики и недоступные датчики.");
        AiPlanText.Text = "Локальный план по правилам NEXUS (не ответ ИИ):\n\n" + string.Join("\n\n", plan.Distinct());
    }

    private async void AssistantTaskButton_Click(object sender, RoutedEventArgs e)
    {
        string task = ((Button)sender).Tag?.ToString() ?? "";
        switch (task)
        {
            case "scan":
                if (_securityScanCancellation != null) { AiTaskStatusText.Text = "Диагностика уже выполняется."; return; }
                NavigateTo("diagnostics"); ScanSystemButton_Click(sender, e); return;
            case "temp":
                if (!AnalyzeTempButton.IsEnabled) { AiTaskStatusText.Text = "Операция с Temp уже выполняется."; return; }
                NavigateTo("maintenance"); AnalyzeTempButton_Click(sender, e); return;
            case "ram": NavigateTo("maintenance"); if (RefreshMemoryProcessesButton.IsEnabled) RefreshMemoryProcessesButton_Click(sender, e); return;
            case "storage": WindowsStorageButton_Click(sender, e); return;
            case "sfc": case "dism":
                if (_repairRunning) { AiTaskStatusText.Text = "Восстановление уже выполняется. Дождитесь завершения."; return; }
                _repairRunning = true;
                var button = (Button)sender; button.IsEnabled = false;
                try
                {
                    bool dism = task == "dism";
                    var dialog = new ContentDialog { XamlRoot = ShellRoot.XamlRoot, RequestedTheme = ElementTheme.Dark, Title = dism ? "Восстановить хранилище компонентов?" : "Восстановить системные файлы?", Content = (dism ? "DISM /Online /Cleanup-Image /RestoreHealth. Возможно использование Windows Update." : "SFC /scannow.") + "\nКоманда может менять системные файлы. Windows запросит повышение прав. Результат появится после завершения команды.", PrimaryButtonText = "Запустить восстановление", CloseButtonText = "Отмена", DefaultButton = ContentDialogButton.Close };
                    if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                    AiTaskStatusText.Text = "Восстановление выполняется в окне Windows. Приложение можно использовать; повторный запуск заблокирован.";
                    int code = await RepairService.RunAsync(dism);
                    if (_securityWindowClosed) return;
                    AiTaskStatusText.Text = code == 3010 ? "Команда завершилась: требуется перезагрузка Windows. Перезагрузка автоматически не выполняется." : $"Команда завершилась с кодом {code}. Оцените результат в журнале CBS/DISM и повторите диагностику; код сам по себе не гарантирует исправление всех проблем.";
                    _logService.Write("System", "SystemRepair", "AssistantTasks", dism ? "DISM /RestoreHealth завершён" : "SFC /scannow завершён", AiTaskStatusText.Text, severity: code == 0 || code == 3010 ? "Info" : "Warning");
                }
                catch (Exception ex) { if (!_securityWindowClosed) AiTaskStatusText.Text = "Восстановление не завершено: " + ex.Message; }
                finally { _repairRunning = false; if (!_securityWindowClosed) button.IsEnabled = true; }
                return;
        }
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NEXUS.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace NEXUS;

public sealed partial class MainWindow
{
    private void SetAiRequestBusy(bool busy)
    {
        AiSendButton.IsEnabled = AiClearButton.IsEnabled = AiLoadModelsButton.IsEnabled = AiTestConnectionButton.IsEnabled = AiSaveSettingsButton.IsEnabled = !busy;
        AiEndpointBox.IsEnabled = AiModelBox.IsEnabled = AiApiKeyBox.IsEnabled = AiModelsComboBox.IsEnabled = !busy;
        AiCancelButton.IsEnabled = AiConnectionCancelButton.IsEnabled = busy;
    }
    private void LoadAiConnectionSettings()
    {
        if (_aiSettingsLoaded) return;
        _aiSettingsLoaded = true;
        try
        {
            var settings = AiConnectionSettingsStore.Load();
            if (settings == null) return;
            AiEndpointBox.Text = settings.Endpoint;
            AiModelBox.Text = settings.Model;
            AiConnectionStatusText.Text = "Адрес и модель загружены. Подключение ещё не проверено; API-ключ нужно ввести заново, если он требуется.";
        }
        catch { AiConnectionStatusText.Text = "Сохранённые настройки не прочитаны. Укажите адрес и модель заново и сохраните их."; }
    }
    private void AiSaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AiConnectionSettingsStore.Save(new(AiEndpointBox.Text.Trim(), AiModelBox.Text.Trim()));
            AiConnectionStatusText.Text = "Адрес и имя модели сохранены. Ключ, история чата и разрешение передачи контекста не сохраняются.";
        }
        catch (Exception ex) { AiConnectionStatusText.Text = "Настройки не сохранены: " + ex.Message; }
    }
    private void AiModelsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AiModelBox != null && AiModelsComboBox.SelectedItem is string model) AiModelBox.Text = model;
    }
    private async void AiConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_aiRequestCancellation != null) return;
        bool models = ((Button)sender).Tag?.ToString() == "models";
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_controlsLifetime.Token);
        _aiRequestCancellation = cancellation;
        SetAiRequestBusy(true);
        AiConnectionStatusText.Text = models ? "Получаем список моделей (без сообщений чата и показателей ПК)…" : "Отправляем тестовый вопрос выбранной модели, без данных ПК и истории…";
        try
        {
            using var client = new AiAssistantService();
            if (models)
            {
                var names = await client.ListModelsAsync(AiEndpointBox.Text, AiApiKeyBox.Password, cancellation.Token);
                if (_securityWindowClosed) return;
                string current = AiModelBox.Text;
                AiModelsComboBox.ItemsSource = names;
                AiModelsComboBox.SelectedItem = names.Contains(current) ? current : names.FirstOrDefault();
                AiConnectionStatusText.Text = names.Count == 0 ? "Сервер вернул пустой список. Установите/предоставьте модель на сервере или проверьте доступ." : $"Получено моделей: {names.Count} (до 200). Это не проверка генерации; выберите модель и проверьте её ответ.";
            }
            else
            {
                string reply = await client.AskAsync(AiEndpointBox.Text, AiModelBox.Text, AiApiKeyBox.Password, "Это тест подключения. Показатели компьютера не переданы.", Array.Empty<ChatTurn>(), "Ответь одной короткой фразой: подключение NEXUS работает.", cancellation.Token);
                if (_securityWindowClosed) return;
                AiConnectionStatusText.Text = "Модель вернула ответ: " + reply[..Math.Min(reply.Length, 600)] + "\nТест не выполнял диагностику или исправления.";
                AiStatusText.Text = "Тестовый ответ получен. Можно задавать вопросы модели.";
            }
        }
        catch (OperationCanceledException) { if (!_securityWindowClosed) AiConnectionStatusText.Text = cancellation.IsCancellationRequested ? "Проверка отменена." : models ? "Список моделей не получен за 15 секунд." : "Модель не ответила за 120 секунд."; }
        catch (Exception ex) { if (!_securityWindowClosed) AiConnectionStatusText.Text = "Подключение не проверено: " + ex.Message; }
        finally { _aiRequestCancellation = null; if (!_securityWindowClosed) SetAiRequestBusy(false); }
    }
    private void AiPreviewContextButton_Click(object sender, RoutedEventArgs e)
    {
        AiContextPreviewText.Text = BuildAiContext();
    }
}

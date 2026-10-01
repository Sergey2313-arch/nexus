using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;

namespace NEXUS
{
    public sealed partial class MainWindow
    {
        private bool _fullReportAssistantBridgeInitialized;

        public void InitializeFullReportAssistantBridge()
        {
            if (_fullReportAssistantBridgeInitialized || _assistantPage == null)
                return;

            Button? send = FindDescendants<Button>(_assistantPage)
                .FirstOrDefault(button => button.Content?.ToString() == "Отправить");

            if (send == null)
                return;

            _fullReportAssistantBridgeInitialized = true;

            // Сохраняем все старые команды помощника, но перехватываем запросы
            // к полному системному отчёту раньше базового обработчика.
            send.Click -= EnhancedAssistantSend_Click;
            send.Click += FullReportAwareAssistantSend_Click;

            if (_assistantHintText != null)
            {
                _assistantHintText.Text =
                    "Команды: «состояние», «диск», «температура», «процессы», «задачи», «что требует внимания», «безопасность», «полный отчёт», «что не так».";
            }
        }

        private void FullReportAwareAssistantSend_Click(object sender, RoutedEventArgs e)
        {
            string query = _assistantInput?.Text?.Trim().ToLowerInvariant() ?? "";

            if (!string.IsNullOrWhiteSpace(query) &&
                TryBuildFullReportAssistantAnswer(query, out string answer))
            {
                if (_assistantOutputText != null)
                    _assistantOutputText.Text = answer;

                if (_assistantInput != null)
                    _assistantInput.Text = "";

                return;
            }

            EnhancedAssistantSend_Click(sender, e);
        }
    }
}

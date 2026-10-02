using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;

namespace NEXUS
{
    public sealed partial class MainWindow
    {
        private DispatcherTimer? _extraLocalizationTimer;
        private bool _extraLocalizationInitialized;

        private static readonly Dictionary<string, (string Ru, string En)> ExtraUiStrings =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Logbook"] = ("Журнал", "Logbook"),
                ["Бортовой журнал NEXUS"] = ("Бортовой журнал NEXUS", "NEXUS activity log"),
                ["Обновить"] = ("Обновить", "Refresh"),
                ["Категория"] = ("Категория", "Category"),
                ["Все"] = ("Все", "All"),
                ["Программы"] = ("Программы", "Programs"),
                ["Процессы"] = ("Процессы", "Processes"),
                ["Файлы"] = ("Файлы", "Files"),
                ["Буфер обмена"] = ("Буфер обмена", "Clipboard"),
                ["Система"] = ("Система", "System"),
                ["Поиск по журналу..."] = ("Поиск по журналу...", "Search logbook..."),
                ["СЕЙЧАС ЗАПУЩЕНО"] = ("СЕЙЧАС ЗАПУЩЕНО", "RUNNING NOW"),
                ["ИСТОРИЯ"] = ("ИСТОРИЯ", "HISTORY"),
                ["Получение данных..."] = ("Получение данных...", "Getting data..."),
                ["Поиск видеокарты..."] = ("Поиск видеокарты...", "Detecting GPU..."),
                ["Поиск накопителя..."] = ("Поиск накопителя...", "Detecting storage..."),
                ["Проверка..."] = ("Проверка...", "Checking..."),
                ["Состояние системы NEXUS"] = ("Состояние системы NEXUS", "NEXUS system overview"),
                ["Система работает стабильно"] = ("Система работает стабильно", "System is running normally"),
                ["ПОСЛЕДНЕЕ СОБЫТИЕ"] = ("ПОСЛЕДНЕЕ СОБЫТИЕ", "LAST EVENT"),
                ["ТЕМПЕРАТУРА CPU"] = ("ТЕМПЕРАТУРА CPU", "CPU TEMPERATURE"),
                ["СОСТОЯНИЕ SSD"] = ("СОСТОЯНИЕ SSD", "SSD HEALTH"),
                ["Остаточный ресурс"] = ("Остаточный ресурс", "Health remaining"),
                ["Ресурс использован"] = ("Ресурс использован", "Life used"),
                ["Время работы"] = ("Время работы", "Power-on time"),
                ["Включений"] = ("Включений", "Power cycles"),
                ["Накопитель"] = ("Накопитель", "Storage"),
                ["Температура"] = ("Температура", "Temperature"),
                ["Главная"] = ("Главная", "Dashboard"),
                ["Устройства"] = ("Устройства", "Devices"),
                ["Планировщик"] = ("Планировщик", "Planner"),
                ["Диагностика"] = ("Диагностика", "Diagnostics"),
                ["Безопасность"] = ("Безопасность", "Security"),
                ["Обслуживание"] = ("Обслуживание", "Maintenance"),
                ["ИИ-помощник"] = ("ИИ-помощник", "AI Assistant"),
                ["Настройки"] = ("Настройки", "Settings")
            };

        public void InitializeLocalizationExtras()
        {
            if (_extraLocalizationInitialized)
                return;

            _extraLocalizationInitialized = true;
            ApplyExtraLocalization();

            _extraLocalizationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.5)
            };
            _extraLocalizationTimer.Tick += (_, _) => ApplyExtraLocalization();
            _extraLocalizationTimer.Start();

            Closed += (_, _) =>
            {
                try { _extraLocalizationTimer?.Stop(); } catch { }
            };
        }

        private void ApplyExtraLocalization()
        {
            if (Content is not DependencyObject root)
                return;

            foreach (TextBlock textBlock in FindDescendants<TextBlock>(root))
            {
                string current = textBlock.Text ?? "";
                if (TryTranslateExtra(current, out string translated))
                    textBlock.Text = translated;
            }

            foreach (Button button in FindDescendants<Button>(root))
            {
                if (button.Content is string current && TryTranslateExtra(current, out string translated))
                    button.Content = translated;
            }

            foreach (TextBox textBox in FindDescendants<TextBox>(root))
            {
                string current = textBox.PlaceholderText ?? "";
                if (TryTranslateExtra(current, out string translated))
                    textBox.PlaceholderText = translated;
            }

            foreach (ComboBox combo in FindDescendants<ComboBox>(root))
            {
                string placeholder = combo.PlaceholderText ?? "";
                if (TryTranslateExtra(placeholder, out string translatedPlaceholder))
                    combo.PlaceholderText = translatedPlaceholder;

                foreach (object item in combo.Items)
                {
                    if (item is ComboBoxItem comboItem &&
                        comboItem.Content is string current &&
                        TryTranslateExtra(current, out string translated))
                    {
                        comboItem.Content = translated;
                    }
                }
            }
        }

        private static bool TryTranslateExtra(string value, out string translated)
        {
            foreach ((string Ru, string En) entry in ExtraUiStrings.Values)
            {
                if (!value.Equals(entry.Ru, StringComparison.OrdinalIgnoreCase) &&
                    !value.Equals(entry.En, StringComparison.OrdinalIgnoreCase))
                    continue;

                translated = LocalizationService.CurrentLanguage == NexusLanguage.English
                    ? entry.En
                    : entry.Ru;
                return true;
            }

            translated = value;
            return false;
        }
    }
}

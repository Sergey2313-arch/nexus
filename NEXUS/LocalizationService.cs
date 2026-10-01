using System;
using System.Collections.Generic;
using System.IO;

namespace NEXUS
{
    public enum NexusLanguage
    {
        Russian,
        English
    }

    public static class LocalizationService
    {
        private sealed record Entry(string Ru, string En);

        private static readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Nav.Dashboard"] = new("Главная", "Dashboard"),
            ["Nav.Devices"] = new("Устройства", "Devices"),
            ["Nav.Planner"] = new("Планировщик", "Planner"),
            ["Nav.Logbook"] = new("Журнал", "Logbook"),
            ["Nav.Diagnostics"] = new("Диагностика", "Diagnostics"),
            ["Nav.Security"] = new("Безопасность", "Security"),
            ["Nav.Maintenance"] = new("Обслуживание", "Maintenance"),
            ["Nav.AI"] = new("ИИ-помощник", "AI Assistant"),
            ["Nav.AIShort"] = new("ИИ", "AI"),
            ["Nav.Settings"] = new("Настройки", "Settings"),

            ["Dashboard.Title"] = new("Главная", "Dashboard"),
            ["Dashboard.Subtitle"] = new("Состояние системы NEXUS", "NEXUS system overview"),
            ["Dashboard.AgentConnected"] = new("Агент NEXUS подключён", "NEXUS Agent connected"),
            ["Dashboard.Online"] = new("● В СЕТИ", "● ONLINE"),
            ["Dashboard.Disk"] = new("ДИСК C:", "DISK C:"),
            ["Dashboard.CpuTemp"] = new("ТЕМПЕРАТУРА CPU", "CPU TEMPERATURE"),
            ["Dashboard.SsdHealth"] = new("СОСТОЯНИЕ SSD", "SSD HEALTH"),
            ["Dashboard.Temperature"] = new("Температура", "Temperature"),
            ["Dashboard.HealthRemaining"] = new("Остаточный ресурс", "Health remaining"),
            ["Dashboard.LifeUsed"] = new("Ресурс использован", "Life used"),
            ["Dashboard.PowerOn"] = new("Время работы", "Power-on time"),
            ["Dashboard.PowerCycles"] = new("Включений", "Power cycles"),
            ["Dashboard.Storage"] = new("Накопитель", "Storage"),
            ["Dashboard.ReadWritten"] = new("Прочитано: -- | Записано: --", "Read: -- | Written: --"),
            ["Dashboard.Uptime"] = new("ВРЕМЯ РАБОТЫ", "UPTIME"),
            ["Dashboard.Stable"] = new("Система работает стабильно", "System is running normally"),
            ["Dashboard.LastEvent"] = new("ПОСЛЕДНЕЕ СОБЫТИЕ", "LAST EVENT"),

            ["Page.DevicesSubtitle"] = new("Устройства и текущее состояние этого компьютера", "Devices and live status of this computer"),
            ["Page.PlannerSubtitle"] = new("Личные задачи NEXUS. Хранятся локально на этом компьютере.", "Personal NEXUS tasks stored locally on this computer."),
            ["Page.MaintenanceSubtitle"] = new("Безопасный анализ обслуживания Windows и свободного места", "Safe Windows maintenance and storage analysis"),
            ["Page.AISubtitle"] = new("Локальный помощник NEXUS. Полноценная LLM-интеграция будет отдельным этапом.", "Local NEXUS assistant. Full LLM integration is a separate stage."),
            ["Page.SettingsSubtitle"] = new("Настройки поведения NEXUS", "NEXUS behavior settings"),
            ["Page.DiagnosticsSubtitle"] = new("Диагностика железа, температур, износа и рекомендации по обслуживанию", "Hardware diagnostics, temperatures, wear and maintenance recommendations"),
            ["Page.SecuritySubtitle"] = new("Проверка подозрительных файлов, автозапуска, закрепления и признаков компрометации Windows", "Suspicious files, persistence and Windows compromise indicators"),

            ["Common.Refresh"] = new("Обновить", "Refresh"),
            ["Common.Add"] = new("Добавить", "Add"),
            ["Common.Delete"] = new("Удалить", "Delete"),
            ["Common.Done"] = new("Готово", "Done"),
            ["Common.Export"] = new("Экспорт", "Export"),
            ["Common.Checking"] = new("Проверка...", "Checking..."),
            ["Common.GettingData"] = new("Получение данных...", "Getting data..."),
            ["Common.NotAvailable"] = new("Недоступно", "Unavailable"),
            ["Common.NotRun"] = new("Проверка ещё не запускалась.", "Scan has not been run yet."),

            ["Planner.Placeholder"] = new("Добавить задачу...", "Add a task..."),
            ["Planner.Priority"] = new("Приоритет", "Priority"),
            ["Planner.Due"] = new("Срок", "Due date"),
            ["Planner.Normal"] = new("Обычный", "Normal"),
            ["Planner.High"] = new("Высокий", "High"),
            ["Planner.Low"] = new("Низкий", "Low"),
            ["Planner.ClearCompleted"] = new("Удалить выполненные", "Clear completed"),
            ["Planner.Export"] = new("Экспорт TXT", "Export TXT"),
            ["Planner.NoTasks"] = new("Задач пока нет.", "No tasks yet."),
            ["Planner.Deadlines"] = new("СРОКИ И ПРИОРИТЕТЫ", "DEADLINES & PRIORITIES"),

            ["Devices.ThisDevice"] = new("ЭТО УСТРОЙСТВО", "THIS DEVICE"),
            ["Devices.Storage"] = new("НАКОПИТЕЛИ", "STORAGE"),
            ["Devices.Live"] = new("СОСТОЯНИЕ В РЕАЛЬНОМ ВРЕМЕНИ", "LIVE DEVICE STATUS"),
            ["Devices.TopRam"] = new("ТОП ПРОЦЕССОВ ПО RAM", "TOP PROCESSES BY RAM"),
            ["Devices.Export"] = new("Экспорт снимка системы", "Export system snapshot"),

            ["Maintenance.Analyze"] = new("АНАЛИЗИРОВАТЬ СИСТЕМУ", "ANALYZE SYSTEM"),
            ["Maintenance.OpenTemp"] = new("Открыть TEMP", "Open TEMP"),
            ["Maintenance.StorageSettings"] = new("Настройки хранилища", "Storage Settings"),
            ["Maintenance.WindowsUpdate"] = new("Центр обновления Windows", "Windows Update"),
            ["Maintenance.LargeFiles"] = new("АНАЛИЗ КРУПНЫХ ФАЙЛОВ", "LARGE FILE ANALYZER"),
            ["Maintenance.ScanLargeFiles"] = new("НАЙТИ КРУПНЫЕ ФАЙЛЫ", "SCAN LARGE FILES"),

            ["AI.Placeholder"] = new("Спросить NEXUS...", "Ask NEXUS..."),
            ["AI.Send"] = new("Отправить", "Send"),

            ["Settings.Live"] = new("LIVE обновление журнала", "LIVE Logbook updates"),
            ["Settings.Clipboard"] = new("Журнал буфера обмена", "Clipboard logging"),
            ["Settings.Language"] = new("ЯЗЫК ИНТЕРФЕЙСА", "INTERFACE LANGUAGE"),
            ["Settings.LanguageHint"] = new("Язык меняется сразу и сохраняется для следующего запуска.", "Language changes immediately and is saved for the next launch."),
            ["Settings.Data"] = new("ДАННЫЕ NEXUS", "NEXUS DATA"),
            ["Settings.OpenData"] = new("Открыть папку NEXUS", "Open NEXUS folder"),

            ["Diagnostics.Run"] = new("ЗАПУСТИТЬ ДИАГНОСТИКУ", "RUN HARDWARE DIAGNOSTICS"),
            ["Diagnostics.FullReport"] = new("ПОЛНЫЙ СИСТЕМНЫЙ ОТЧЁТ", "FULL SYSTEM REPORT"),
            ["Diagnostics.GenerateReport"] = new("СФОРМИРОВАТЬ ОТЧЁТ", "GENERATE FULL REPORT"),
            ["Diagnostics.ExportReport"] = new("ЭКСПОРТ ОТЧЁТА", "EXPORT REPORT"),
            ["Diagnostics.Telemetry"] = new("ИСТОРИЯ ТЕЛЕМЕТРИИ", "TELEMETRY HISTORY"),
            ["Diagnostics.Charts"] = new("ГРАФИКИ ТЕЛЕМЕТРИИ", "TELEMETRY CHARTS"),

            ["Security.Compromise"] = new("СКАНЕР КОМПРОМЕТАЦИИ", "COMPROMISE SCANNER"),
            ["Security.Quick"] = new("БЫСТРАЯ ПРОВЕРКА", "QUICK SECURITY SCAN"),
            ["Security.Deep"] = new("ГЛУБОКАЯ ПРОВЕРКА ФАЙЛОВ", "DEEP FILE SCAN"),
            ["Security.DefenderQuick"] = new("БЫСТРАЯ ПРОВЕРКА DEFENDER", "DEFENDER QUICK SCAN"),
            ["Security.DefenderFull"] = new("ПОЛНАЯ ПРОВЕРКА DEFENDER", "DEFENDER FULL SCAN"),
            ["Security.Audit"] = new("АУДИТ WINDOWS SECURITY", "WINDOWS SECURITY AUDIT"),
            ["Security.Integrity"] = new("КОНТРОЛЬ ЦЕЛОСТНОСТИ", "INTEGRITY BASELINE"),
            ["Security.ProcessTrust"] = new("ДОВЕРИЕ К ПРОЦЕССАМ", "PROCESS TRUST INSPECTOR"),
            ["Security.Activity"] = new("СЛУЖБЫ И СЕТЕВАЯ АКТИВНОСТЬ", "SERVICES & NETWORK ACTIVITY"),

            ["Live.DiskCritical"] = new("⚠ Критично: мало свободного места", "⚠ Critical: low free disk space"),
            ["Live.DiskWarning"] = new("⚠ Внимание: диск почти заполнен", "⚠ Warning: disk is almost full"),
            ["Live.DiskNormal"] = new("✓ Состояние диска нормальное", "✓ Disk status is normal"),
            ["Live.DiskError"] = new("Не удалось получить данные диска", "Unable to read disk data"),
            ["Live.TempHigh"] = new("⚠ Высокая температура", "⚠ High temperature"),
            ["Live.TempElevated"] = new("⚠ Повышенная температура", "⚠ Elevated temperature"),
            ["Live.TempNormal"] = new("✓ Температура нормальная", "✓ Temperature is normal"),
            ["Live.CpuSensorUnavailable"] = new("Датчик CPU недоступен", "CPU temperature sensor unavailable"),
            ["Live.StorageMissing"] = new("Накопитель не обнаружен", "Storage device not detected"),
            ["Live.StorageCritical"] = new("⚠ КРИТИЧНО", "⚠ CRITICAL"),
            ["Live.StorageWarning"] = new("⚠ ВНИМАНИЕ", "⚠ WARNING"),
            ["Live.StorageLowSpace"] = new("✓ SSD исправен • мало места", "✓ SSD healthy • low free space"),
            ["Live.SmartUnavailable"] = new("SMART данные недоступны", "SMART data unavailable")
        };

        public static NexusLanguage CurrentLanguage { get; private set; } = NexusLanguage.Russian;

        private static string SettingsFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NEXUS");

        private static string LanguageFile => Path.Combine(SettingsFolder, "language.txt");

        public static void Load()
        {
            try
            {
                if (!File.Exists(LanguageFile))
                    return;

                string value = File.ReadAllText(LanguageFile).Trim();
                CurrentLanguage = value.Equals("en", StringComparison.OrdinalIgnoreCase)
                    ? NexusLanguage.English
                    : NexusLanguage.Russian;
            }
            catch
            {
                CurrentLanguage = NexusLanguage.Russian;
            }
        }

        public static void SetLanguage(NexusLanguage language)
        {
            CurrentLanguage = language;

            try
            {
                Directory.CreateDirectory(SettingsFolder);
                File.WriteAllText(LanguageFile, language == NexusLanguage.English ? "en" : "ru");
            }
            catch
            {
            }
        }

        public static string T(string key)
        {
            if (!Entries.TryGetValue(key, out Entry? entry))
                return key;

            return CurrentLanguage == NexusLanguage.English ? entry.En : entry.Ru;
        }

        public static bool TryTranslateLiteral(string value, out string translated)
        {
            foreach (Entry entry in Entries.Values)
            {
                if (value.Equals(entry.Ru, StringComparison.OrdinalIgnoreCase) ||
                    value.Equals(entry.En, StringComparison.OrdinalIgnoreCase))
                {
                    translated = CurrentLanguage == NexusLanguage.English ? entry.En : entry.Ru;
                    return true;
                }
            }

            translated = value;
            return false;
        }

        public static string TranslateDynamic(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            if (TryTranslateLiteral(value, out string exact))
                return exact;

            if (CurrentLanguage == NexusLanguage.Russian)
            {
                return value
                    .Replace("Temperature: unavailable", "Температура: недоступна", StringComparison.OrdinalIgnoreCase)
                    .Replace("Temperature:", "Температура:", StringComparison.OrdinalIgnoreCase)
                    .Replace("Read:", "Прочитано:", StringComparison.OrdinalIgnoreCase)
                    .Replace("Written:", "Записано:", StringComparison.OrdinalIgnoreCase)
                    .Replace(" processes", " процессов", StringComparison.OrdinalIgnoreCase)
                    .Replace(" events", " событий", StringComparison.OrdinalIgnoreCase);
            }

            return value
                .Replace("Температура: недоступна", "Temperature: unavailable", StringComparison.OrdinalIgnoreCase)
                .Replace("Температура:", "Temperature:", StringComparison.OrdinalIgnoreCase)
                .Replace("Прочитано:", "Read:", StringComparison.OrdinalIgnoreCase)
                .Replace("Записано:", "Written:", StringComparison.OrdinalIgnoreCase)
                .Replace(" процессов", " processes", StringComparison.OrdinalIgnoreCase)
                .Replace(" событий", " events", StringComparison.OrdinalIgnoreCase)
                .Replace(" ч", " h", StringComparison.OrdinalIgnoreCase);
        }
    }
}

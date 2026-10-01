using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace NEXUS
{
    public sealed partial class MainWindow
    {
        private DispatcherTimer? _pageEnhancementTimer;
        private TextBlock? _devicesLiveText;
        private StackPanel? _devicesTopProcessesPanel;
        private TextBlock? _plannerSummaryText;
        private TextBlock? _maintenanceQuickStatusText;
        private TextBlock? _assistantHintText;
        private bool _pageEnhancementsInitialized;

        public void InitializePageEnhancements()
        {
            if (_pageEnhancementsInitialized)
                return;

            _pageEnhancementsInitialized = true;

            EnhanceDevicesPage();
            EnhancePlannerPage();
            EnhanceMaintenancePage();
            EnhanceAssistantPage();
            EnhanceSettingsPage();
            UpdateVersionTo011();

            _pageEnhancementTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _pageEnhancementTimer.Tick += PageEnhancementTimer_Tick;
            _pageEnhancementTimer.Start();
        }

        private void PageEnhancementTimer_Tick(object? sender, object e)
        {
            if (_devicesPage?.Visibility == Visibility.Visible)
            {
                RefreshDevicesPage();
                RefreshDevicesEnhancements();
            }

            if (_plannerPage?.Visibility == Visibility.Visible)
                RefreshPlannerSummary();

            if (_maintenancePage?.Visibility == Visibility.Visible)
                RefreshMaintenanceQuickStatus();
        }

        // ============================================================
        // DEVICES
        // ============================================================

        private void EnhanceDevicesPage()
        {
            StackPanel? body = GetPageBody(_devicesPage);
            if (body == null)
                return;

            Border liveCard = CreateCard();
            StackPanel liveBody = new() { Spacing = 8 };
            liveBody.Children.Add(new TextBlock
            {
                Text = "LIVE DEVICE STATUS",
                Foreground = Brush(154, 157, 165),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            _devicesLiveText = new TextBlock
            {
                Text = "Получение данных...",
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap
            };
            liveBody.Children.Add(_devicesLiveText);
            liveCard.Child = liveBody;

            Border processCard = CreateCard();
            StackPanel processBody = new() { Spacing = 10 };
            processBody.Children.Add(new TextBlock
            {
                Text = "TOP PROCESSES BY RAM",
                Foreground = Brush(154, 157, 165),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            _devicesTopProcessesPanel = new StackPanel { Spacing = 6 };
            processBody.Children.Add(_devicesTopProcessesPanel);
            processCard.Child = processBody;

            body.Children.Add(liveCard);
            body.Children.Add(processCard);

            RefreshDevicesEnhancements();
        }

        private void RefreshDevicesEnhancements()
        {
            if (_devicesLiveText != null)
            {
                bool network = NetworkInterface.GetIsNetworkAvailable();
                string activeAdapters = GetActiveAdaptersSummary();

                _devicesLiveText.Text =
                    $"CPU: {CpuValueText.Text} • {CpuTempText.Text}\n" +
                    $"RAM: {RamValueText.Text} • {RamDetailsText.Text}\n" +
                    $"GPU: {GpuLoadText.Text} • {GpuTempText.Text}\n" +
                    $"SSD: {StorageTempText.Text} • health {StorageHealthText.Text}\n" +
                    $"Network: {(network ? "ONLINE" : "OFFLINE")}\n" +
                    activeAdapters;
            }

            if (_devicesTopProcessesPanel == null)
                return;

            _devicesTopProcessesPanel.Children.Clear();

            List<(string Name, int Pid, long Memory)> processes = new();
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    processes.Add((process.ProcessName, process.Id, process.WorkingSet64));
                }
                catch { }
                finally { process.Dispose(); }
            }

            foreach (var process in processes
                .OrderByDescending(item => item.Memory)
                .Take(8))
            {
                Grid row = new() { ColumnSpacing = 10 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                row.Children.Add(new TextBlock
                {
                    Text = $"{process.Name}  •  PID {process.Pid}",
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                TextBlock memory = new()
                {
                    Text = FormatBytes(process.Memory),
                    Foreground = Brush(154, 157, 165)
                };
                Grid.SetColumn(memory, 1);
                row.Children.Add(memory);

                _devicesTopProcessesPanel.Children.Add(row);
            }
        }

        private static string GetActiveAdaptersSummary()
        {
            try
            {
                string[] adapters = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(item => item.OperationalStatus == OperationalStatus.Up &&
                                   item.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Select(item => $"{item.Name} • {item.NetworkInterfaceType}")
                    .Take(3)
                    .ToArray();

                return adapters.Length == 0
                    ? "Адаптеры: нет активных"
                    : "Адаптеры: " + string.Join(" | ", adapters);
            }
            catch
            {
                return "Адаптеры: недоступно";
            }
        }

        // ============================================================
        // PLANNER
        // ============================================================

        private void EnhancePlannerPage()
        {
            StackPanel? body = GetPageBody(_plannerPage);
            if (body == null)
                return;

            Border summaryCard = CreateCard();
            StackPanel summaryBody = new() { Spacing = 9 };

            _plannerSummaryText = new TextBlock
            {
                Text = "Задачи: --",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            summaryBody.Children.Add(_plannerSummaryText);

            StackPanel buttons = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10
            };

            Button clearCompleted = new() { Content = "Удалить выполненные" };
            clearCompleted.Click += (_, _) =>
            {
                _plannerTasks.RemoveAll(item => item.Completed);
                SavePlanner();
                RenderPlanner();
                RefreshPlannerSummary();
            };

            Button export = new() { Content = "Экспорт TXT" };
            export.Click += (_, _) => ExportPlannerToText();

            buttons.Children.Add(clearCompleted);
            buttons.Children.Add(export);
            summaryBody.Children.Add(buttons);
            summaryCard.Child = summaryBody;

            body.Children.Insert(Math.Min(2, body.Children.Count), summaryCard);
            RefreshPlannerSummary();
        }

        private void RefreshPlannerSummary()
        {
            if (_plannerSummaryText == null)
                return;

            int total = _plannerTasks.Count;
            int completed = _plannerTasks.Count(item => item.Completed);
            int active = total - completed;

            _plannerSummaryText.Text =
                $"Активно: {active} • Выполнено: {completed} • Всего: {total}";
        }

        private void ExportPlannerToText()
        {
            try
            {
                Directory.CreateDirectory(NexusDataFolder);

                string path = Path.Combine(NexusDataFolder, "planner-export.txt");
                List<string> lines = new()
                {
                    "NEXUS PLANNER",
                    $"Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    ""
                };

                foreach (PlannerTaskItem task in _plannerTasks
                    .OrderBy(item => item.Completed)
                    .ThenBy(item => item.CreatedAt))
                {
                    lines.Add($"[{(task.Completed ? "x" : " ")}] {task.Text}  ({task.CreatedAt:dd.MM.yyyy HH:mm})");
                }

                File.WriteAllLines(path, lines);
                OpenPath(path);
            }
            catch { }
        }

        // ============================================================
        // MAINTENANCE
        // ============================================================

        private void EnhanceMaintenancePage()
        {
            StackPanel? body = GetPageBody(_maintenancePage);
            if (body == null)
                return;

            Border quickCard = CreateCard();
            StackPanel quickBody = new() { Spacing = 12 };

            quickBody.Children.Add(new TextBlock
            {
                Text = "QUICK MAINTENANCE",
                Foreground = Brush(154, 157, 165),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            _maintenanceQuickStatusText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush(200, 202, 208)
            };
            quickBody.Children.Add(_maintenanceQuickStatusText);

            StackPanel actions = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10
            };

            Button temp = new() { Content = "Открыть TEMP" };
            temp.Click += (_, _) => OpenPath(Path.GetTempPath());

            Button storage = new() { Content = "Storage Settings" };
            storage.Click += (_, _) => OpenUri("ms-settings:storagesense");

            Button updates = new() { Content = "Windows Update" };
            updates.Click += (_, _) => OpenUri("ms-settings:windowsupdate");

            actions.Children.Add(temp);
            actions.Children.Add(storage);
            actions.Children.Add(updates);
            quickBody.Children.Add(actions);

            quickBody.Children.Add(new TextBlock
            {
                Text = "NEXUS пока не удаляет системные файлы автоматически. Все действия здесь безопасные: анализ или переход к штатным инструментам Windows.",
                Foreground = Brush(115, 119, 127),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });

            quickCard.Child = quickBody;
            body.Children.Add(quickCard);
            RefreshMaintenanceQuickStatus();
        }

        private void RefreshMaintenanceQuickStatus()
        {
            if (_maintenanceQuickStatusText == null)
                return;

            try
            {
                DriveInfo drive = new(@"C:\");
                double totalGb = drive.IsReady ? drive.TotalSize / 1024d / 1024d / 1024d : 0;
                double freeGb = drive.IsReady ? drive.AvailableFreeSpace / 1024d / 1024d / 1024d : 0;
                double used = totalGb > 0 ? (totalGb - freeGb) / totalGb * 100d : 0;

                string advice = used switch
                {
                    >= 95 => "Критически мало свободного места. Освобождение диска желательно выполнить в ближайшее время.",
                    >= 90 => "Свободного места мало. Рекомендуется проверить крупные файлы и временные данные.",
                    >= 80 => "Диск заметно заполнен, но срочное обслуживание не требуется.",
                    _ => "Свободного места достаточно."
                };

                _maintenanceQuickStatusText.Text =
                    $"C: {used:F0}% занято • {freeGb:F1} GB свободно\n" +
                    $"SSD: {StorageTempText.Text} • ресурс {StorageHealthText.Text}\n" +
                    advice;
            }
            catch
            {
                _maintenanceQuickStatusText.Text = "Не удалось обновить быстрый статус обслуживания.";
            }
        }

        // ============================================================
        // ASSISTANT
        // ============================================================

        private void EnhanceAssistantPage()
        {
            StackPanel? body = GetPageBody(_assistantPage);
            if (body == null)
                return;

            _assistantHintText = new TextBlock
            {
                Text = "Команды: «состояние», «диск», «температура», «процессы», «задачи», «что требует внимания», «безопасность».",
                Foreground = Brush(115, 119, 127),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            body.Children.Add(_assistantHintText);

            Button? send = FindDescendants<Button>(_assistantPage!)
                .FirstOrDefault(button => button.Content?.ToString() == "Отправить");

            if (send != null)
            {
                // Заменяем старый минимальный обработчик на расширенный,
                // чтобы запрос не очищался до того, как его увидят новые команды.
                send.Click -= AssistantSendButton_Click;
                send.Click += EnhancedAssistantSend_Click;
            }
        }

        private void EnhancedAssistantSend_Click(object sender, RoutedEventArgs e)
        {
            if (_assistantOutputText == null)
                return;

            string query = _assistantInput?.Text?.Trim().ToLowerInvariant() ?? "";
            if (string.IsNullOrWhiteSpace(query))
                return;

            string answer;

            if (query.Contains("процесс"))
            {
                List<(string Name, long Memory)> top = new();
                foreach (Process process in Process.GetProcesses())
                {
                    try { top.Add((process.ProcessName, process.WorkingSet64)); }
                    catch { }
                    finally { process.Dispose(); }
                }

                answer = "Больше всего RAM используют:\n" +
                    string.Join("\n", top.OrderByDescending(item => item.Memory)
                        .Take(5)
                        .Select(item => $"• {item.Name}: {FormatBytes(item.Memory)}"));
            }
            else if (query.Contains("задач") || query.Contains("план"))
            {
                int active = _plannerTasks.Count(item => !item.Completed);
                string tasks = string.Join("\n", _plannerTasks
                    .Where(item => !item.Completed)
                    .OrderByDescending(item => item.CreatedAt)
                    .Take(5)
                    .Select(item => "• " + item.Text));

                answer = active == 0
                    ? "В Planner сейчас нет активных задач."
                    : $"Активных задач: {active}\n{tasks}";
            }
            else if (query.Contains("вниман") || query.Contains("проблем") || query.Contains("здоров"))
            {
                HardwareHealthSnapshot snapshot = CaptureHealthSnapshot();
                HealthDiagnosticReport report = _healthRecommendationService.Analyze(snapshot);
                List<DiagnosticFinding> important = report.Findings
                    .Where(item => item.Severity == DiagnosticSeverity.Warning ||
                                   item.Severity == DiagnosticSeverity.Critical)
                    .Take(5)
                    .ToList();

                answer = important.Count == 0
                    ? $"Hardware score: {report.Score}/100. Явных предупреждений сейчас нет."
                    : $"Hardware score: {report.Score}/100\n" +
                      string.Join("\n", important.Select(item => $"• {item.Title}: {item.Recommendation}"));
            }
            else if (query.Contains("безопас") || query.Contains("security"))
            {
                answer =
                    "Security вынесен в отдельный отдел. Запусти QUICK SECURITY SCAN для проверки файлов, автозагрузки, Scheduled Tasks, служб и других признаков компрометации.";
            }
            else if (query.Contains("диск") || query.Contains("ssd"))
            {
                answer =
                    $"Диск C: {DiskValueText.Text}. {DiskStatusText.Text}\n" +
                    $"SSD: {StorageNameText.Text}, {StorageTempText.Text}, ресурс {StorageHealthText.Text}.";
            }
            else if (query.Contains("температур") || query.Contains("гре"))
            {
                answer =
                    $"CPU: {CpuTempText.Text} ({CpuTempStatusText.Text})\n" +
                    $"GPU: {GpuTempText.Text}\n" +
                    $"SSD: {StorageTempText.Text}.";
            }
            else if (query.Contains("памят") || query.Contains("ram"))
            {
                answer = $"RAM сейчас: {RamValueText.Text}. {RamDetailsText.Text}.";
            }
            else if (query.Contains("состоян") || query.Contains("систем") ||
                     query.Contains("комп") || query.Contains("желез"))
            {
                answer =
                    $"{Environment.MachineName}\n{OsNameText.Text}\n" +
                    $"CPU: {CpuNameText.Text} • {CpuValueText.Text}\n" +
                    $"RAM: {RamDetailsText.Text}\n" +
                    $"GPU: {GpuNameText.Text} • {GpuTempText.Text}\n" +
                    $"SSD: {StorageHealthText.Text} • {StorageTempText.Text}.";
            }
            else
            {
                answer =
                    "Пока это локальный помощник без облачной LLM. Я уже умею читать состояние железа, процессы и Planner. Подсказки команд находятся ниже поля ввода.";
            }

            _assistantOutputText.Text = answer;
            if (_assistantInput != null)
                _assistantInput.Text = "";
        }

        // ============================================================
        // SETTINGS
        // ============================================================

        private void EnhanceSettingsPage()
        {
            StackPanel? body = GetPageBody(_settingsPage);
            if (body == null)
                return;

            NexusUiSettings settings = LoadUiSettings();

            foreach (ToggleSwitch toggle in FindDescendants<ToggleSwitch>(_settingsPage!).ToList())
            {
                string header = toggle.Header?.ToString() ?? "";

                if (header.Contains("LIVE", StringComparison.OrdinalIgnoreCase))
                {
                    toggle.IsOn = settings.LiveLogbook;
                    LiveToggle.IsOn = settings.LiveLogbook;
                    toggle.Toggled += (_, _) => SaveCurrentUiSettings();
                }
                else if (header.Contains("Clipboard", StringComparison.OrdinalIgnoreCase))
                {
                    toggle.IsOn = settings.ClipboardLogging;
                    SetClipboardLogging(settings.ClipboardLogging);
                    toggle.Toggled += (_, _) => SaveCurrentUiSettings();
                }
            }

            Border dataCard = CreateCard();
            StackPanel dataBody = new() { Spacing = 10 };
            dataBody.Children.Add(new TextBlock
            {
                Text = "NEXUS DATA",
                Foreground = Brush(154, 157, 165),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            dataBody.Children.Add(new TextBlock
            {
                Text = NexusDataFolder,
                FontFamily = new FontFamily("Consolas"),
                Foreground = Brush(115, 119, 127),
                TextWrapping = TextWrapping.Wrap
            });

            Button openData = new() { Content = "Открыть папку NEXUS" };
            openData.Click += (_, _) =>
            {
                Directory.CreateDirectory(NexusDataFolder);
                OpenPath(NexusDataFolder);
            };
            dataBody.Children.Add(openData);
            dataCard.Child = dataBody;
            body.Children.Add(dataCard);
        }

        private string NexusDataFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NEXUS");

        private string UiSettingsPath => Path.Combine(NexusDataFolder, "ui-settings.json");

        private NexusUiSettings LoadUiSettings()
        {
            try
            {
                if (!File.Exists(UiSettingsPath))
                    return new NexusUiSettings();

                return JsonSerializer.Deserialize<NexusUiSettings>(File.ReadAllText(UiSettingsPath))
                    ?? new NexusUiSettings();
            }
            catch
            {
                return new NexusUiSettings();
            }
        }

        private void SaveCurrentUiSettings()
        {
            try
            {
                Directory.CreateDirectory(NexusDataFolder);
                NexusUiSettings settings = new()
                {
                    LiveLogbook = LiveToggle.IsOn,
                    ClipboardLogging = _clipboardLoggingEnabled
                };

                File.WriteAllText(
                    UiSettingsPath,
                    JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private static StackPanel? GetPageBody(Grid? page)
        {
            if (page == null)
                return null;

            ScrollViewer? scroll = page.Children.OfType<ScrollViewer>().FirstOrDefault();
            return scroll?.Content as StackPanel;
        }

        private static void OpenPath(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private static void OpenUri(string uri)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void UpdateVersionTo011()
        {
            if (Content is not DependencyObject root)
                return;

            foreach (TextBlock textBlock in FindDescendants<TextBlock>(root))
            {
                if (textBlock.Text.StartsWith("NEXUS v", StringComparison.OrdinalIgnoreCase))
                {
                    textBlock.Text = "NEXUS v0.1.1";
                    break;
                }
            }
        }

        private sealed class NexusUiSettings
        {
            public bool LiveLogbook { get; set; } = true;
            public bool ClipboardLogging { get; set; } = true;
        }
    }
}

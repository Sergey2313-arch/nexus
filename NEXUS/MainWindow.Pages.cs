using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace NEXUS
{
    public sealed partial class MainWindow
    {
        private Grid? _devicesPage;
        private Grid? _plannerPage;
        private Grid? _maintenancePage;
        private Grid? _assistantPage;
        private Grid? _settingsPage;
        private Grid? _diagnosticsDepartmentPage;
        private Grid? _securityDepartmentPage;

        private StackPanel? _securityDepartmentBody;
        private StackPanel? _diagnosticsDepartmentFindings;
        private TextBlock? _diagnosticsDepartmentScore;
        private TextBlock? _diagnosticsDepartmentStatus;

        private TextBlock? _devicesSummaryText;
        private TextBlock? _devicesStorageText;

        private TextBox? _plannerInput;
        private StackPanel? _plannerItemsPanel;
        private readonly List<PlannerTaskItem> _plannerTasks = new();

        private TextBlock? _maintenanceResultText;
        private TextBlock? _assistantOutputText;
        private TextBox? _assistantInput;

        private bool _clipboardLoggingEnabled = true;
        private bool _mainPagesInitialized;

        public void InitializeMainPagesUI()
        {
            if (_mainPagesInitialized)
                return;

            if (Content is not Grid root || root.Children.Count < 2)
                return;

            Grid? mainGrid = root.Children
                .OfType<Grid>()
                .FirstOrDefault(grid => Grid.GetColumn(grid) == 1);

            if (mainGrid == null)
                return;

            _mainPagesInitialized = true;

            RenameLegacySecurityButtonsToDiagnostics(root);

            _devicesPage = BuildDevicesPage();
            _plannerPage = BuildPlannerPage();
            _maintenancePage = BuildMaintenancePage();
            _assistantPage = BuildAssistantPage();
            _settingsPage = BuildSettingsPage();
            _diagnosticsDepartmentPage = BuildDiagnosticsDepartmentPage();
            _securityDepartmentPage = BuildSecurityDepartmentPage();

            foreach (Grid page in GetCustomPages())
            {
                Grid.SetRow(page, 1);
                page.Visibility = Visibility.Collapsed;
                mainGrid.Children.Add(page);
            }

            AddSeparateSecurityNavigation(root);
            AttachPageNavigation(root);
            MoveCompromiseCardToSecurity();
            UpdateVersionTo010(root);

            LoadPlanner();
            RenderPlanner();
            RefreshDevicesPage();
        }

        private IEnumerable<Grid> GetCustomPages()
        {
            if (_devicesPage != null) yield return _devicesPage;
            if (_plannerPage != null) yield return _plannerPage;
            if (_maintenancePage != null) yield return _maintenancePage;
            if (_assistantPage != null) yield return _assistantPage;
            if (_settingsPage != null) yield return _settingsPage;
            if (_diagnosticsDepartmentPage != null) yield return _diagnosticsDepartmentPage;
            if (_securityDepartmentPage != null) yield return _securityDepartmentPage;
        }

        private void RenameLegacySecurityButtonsToDiagnostics(DependencyObject root)
        {
            foreach (Button button in FindDescendants<Button>(root).ToList())
            {
                if (!string.Equals(button.Content?.ToString(), "Security", StringComparison.Ordinal))
                    continue;

                button.Click -= SecurityButton_Click;
                button.Content = "Diagnostics";
            }
        }

        private void AddSeparateSecurityNavigation(Grid root)
        {
            Border? sidebarBorder = root.Children
                .OfType<Border>()
                .FirstOrDefault(border => Grid.GetColumn(border) == 0);

            if (sidebarBorder?.Child is Grid sidebarGrid)
            {
                StackPanel? menu = sidebarGrid.Children
                    .OfType<StackPanel>()
                    .FirstOrDefault(panel => Grid.GetRow(panel) == 1);

                if (menu != null && !menu.Children.OfType<Button>()
                    .Any(button => button.Content?.ToString() == "Security"))
                {
                    Button security = NewPageNavButton("Security");
                    int diagnosticsIndex = FindButtonIndex(menu, "Diagnostics");
                    int insertAt = diagnosticsIndex >= 0 ? diagnosticsIndex + 1 : Math.Min(4, menu.Children.Count);
                    menu.Children.Insert(insertAt, security);
                }
            }

            Grid? mainGrid = root.Children
                .OfType<Grid>()
                .FirstOrDefault(grid => Grid.GetColumn(grid) == 1);

            Border? topBar = mainGrid?.Children
                .OfType<Border>()
                .FirstOrDefault(border => Grid.GetRow(border) == 0);

            if (topBar?.Child is StackPanel topMenu &&
                !topMenu.Children.OfType<Button>()
                    .Any(button => button.Content?.ToString() == "Security"))
            {
                Button security = new() { Content = "Security" };
                int diagnosticsIndex = FindButtonIndex(topMenu, "Diagnostics");
                int insertAt = diagnosticsIndex >= 0 ? diagnosticsIndex + 1 : topMenu.Children.Count;
                topMenu.Children.Insert(insertAt, security);
            }
        }

        private static Button NewPageNavButton(string text)
        {
            return new Button
            {
                Content = text,
                Height = 50,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
        }

        private static int FindButtonIndex(StackPanel panel, string text)
        {
            for (int i = 0; i < panel.Children.Count; i++)
            {
                if (panel.Children[i] is Button button &&
                    string.Equals(button.Content?.ToString(), text, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private void AttachPageNavigation(DependencyObject root)
        {
            foreach (Button button in FindDescendants<Button>(root).ToList())
            {
                string text = button.Content?.ToString() ?? "";

                switch (text)
                {
                    case "Dashboard":
                    case "Logbook":
                        button.Click += BasePageNavigationButton_Click;
                        break;
                    case "Devices":
                        button.Click += DevicesPageButton_Click;
                        break;
                    case "Planner":
                        button.Click += PlannerPageButton_Click;
                        break;
                    case "Maintenance":
                        button.Click += MaintenancePageButton_Click;
                        break;
                    case "AI":
                    case "AI Assistant":
                        button.Click += AssistantPageButton_Click;
                        break;
                    case "Settings":
                        button.Click += SettingsPageButton_Click;
                        break;
                    case "Diagnostics":
                        button.Click += DiagnosticsDepartmentButton_Click;
                        break;
                    case "Security":
                        button.Click += SecurityDepartmentButton_Click;
                        break;
                }
            }
        }

        private void BasePageNavigationButton_Click(object sender, RoutedEventArgs e)
        {
            HideCustomPages();
            if (_diagnosticsView != null)
                _diagnosticsView.Visibility = Visibility.Collapsed;
        }

        private void DevicesPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_devicesPage == null) return;
            RefreshDevicesPage();
            ShowCustomPage(_devicesPage);
        }

        private void PlannerPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_plannerPage != null)
                ShowCustomPage(_plannerPage);
        }

        private void MaintenancePageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_maintenancePage != null)
                ShowCustomPage(_maintenancePage);
        }

        private void AssistantPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_assistantPage != null)
                ShowCustomPage(_assistantPage);
        }

        private void SettingsPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsPage != null)
                ShowCustomPage(_settingsPage);
        }

        private void DiagnosticsDepartmentButton_Click(object sender, RoutedEventArgs e)
        {
            if (_diagnosticsDepartmentPage == null) return;
            ShowCustomPage(_diagnosticsDepartmentPage);
            RunHardwareDiagnosticsDepartment();
        }

        private void SecurityDepartmentButton_Click(object sender, RoutedEventArgs e)
        {
            if (_securityDepartmentPage != null)
                ShowCustomPage(_securityDepartmentPage);
        }

        private void ShowCustomPage(Grid page)
        {
            _isLogbookVisible = false;
            DashboardView.Visibility = Visibility.Collapsed;
            LogbookView.Visibility = Visibility.Collapsed;

            if (_diagnosticsView != null)
                _diagnosticsView.Visibility = Visibility.Collapsed;

            HideCustomPages();
            page.Visibility = Visibility.Visible;
        }

        private void HideCustomPages()
        {
            foreach (Grid page in GetCustomPages())
                page.Visibility = Visibility.Collapsed;
        }

        private Grid BuildDevicesPage()
        {
            var shell = CreatePageShell("Devices", "Устройства и текущее состояние этого компьютера");

            _devicesSummaryText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 16 };
            _devicesStorageText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush(154, 157, 165)
            };

            Border systemCard = CreateCard();
            StackPanel systemBody = new() { Spacing = 10 };
            systemBody.Children.Add(new TextBlock
            {
                Text = "THIS DEVICE",
                Foreground = Brush(154, 157, 165),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            systemBody.Children.Add(_devicesSummaryText);
            systemCard.Child = systemBody;

            Border storageCard = CreateCard();
            StackPanel storageBody = new() { Spacing = 8 };
            storageBody.Children.Add(new TextBlock
            {
                Text = "STORAGE",
                Foreground = Brush(154, 157, 165),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            storageBody.Children.Add(_devicesStorageText);
            storageCard.Child = storageBody;

            Button refresh = new() { Content = "Обновить" };
            refresh.Click += (_, _) => RefreshDevicesPage();

            shell.Body.Children.Add(refresh);
            shell.Body.Children.Add(systemCard);
            shell.Body.Children.Add(storageCard);
            return shell.Page;
        }

        private void RefreshDevicesPage()
        {
            if (_devicesSummaryText == null || _devicesStorageText == null)
                return;

            _devicesSummaryText.Text =
                $"Имя: {Environment.MachineName}\n" +
                $"OS: {OsNameText.Text}\n" +
                $"CPU: {CpuNameText.Text}\n" +
                $"CPU load: {CpuValueText.Text}\n" +
                $"RAM: {RamDetailsText.Text}\n" +
                $"GPU: {GpuNameText.Text} • {GpuTempText.Text}\n" +
                $"Uptime: {UptimeText.Text}";

            List<string> drives = new();
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady) continue;
                    double total = drive.TotalSize / 1024d / 1024d / 1024d;
                    double free = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
                    drives.Add($"{drive.Name}  {free:F0} GB free / {total:F0} GB");
                }
                catch { }
            }

            _devicesStorageText.Text = string.Join("\n", drives);
        }

        private Grid BuildPlannerPage()
        {
            var shell = CreatePageShell("Planner", "Личные задачи NEXUS. Хранятся локально на этом компьютере.");

            Grid inputRow = new() { ColumnSpacing = 10 };
            inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _plannerInput = new TextBox { PlaceholderText = "Добавить задачу..." };
            Button add = new() { Content = "Добавить", Padding = new Thickness(16, 8, 16, 8) };
            add.Click += PlannerAddButton_Click;
            Grid.SetColumn(add, 1);

            inputRow.Children.Add(_plannerInput);
            inputRow.Children.Add(add);

            Border listCard = CreateCard();
            _plannerItemsPanel = new StackPanel { Spacing = 8 };
            listCard.Child = _plannerItemsPanel;

            shell.Body.Children.Add(inputRow);
            shell.Body.Children.Add(listCard);
            return shell.Page;
        }

        private void PlannerAddButton_Click(object sender, RoutedEventArgs e)
        {
            string text = _plannerInput?.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(text)) return;

            _plannerTasks.Add(new PlannerTaskItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Text = text,
                CreatedAt = DateTime.Now,
                Completed = false
            });

            if (_plannerInput != null) _plannerInput.Text = "";
            SavePlanner();
            RenderPlanner();
        }

        private void LoadPlanner()
        {
            try
            {
                if (!File.Exists(PlannerFilePath)) return;
                List<PlannerTaskItem>? items = JsonSerializer.Deserialize<List<PlannerTaskItem>>(File.ReadAllText(PlannerFilePath));
                if (items == null) return;
                _plannerTasks.Clear();
                _plannerTasks.AddRange(items);
            }
            catch { }
        }

        private void SavePlanner()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PlannerFilePath)!);
                File.WriteAllText(PlannerFilePath,
                    JsonSerializer.Serialize(_plannerTasks, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private string PlannerFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NEXUS",
            "planner.json");

        private void RenderPlanner()
        {
            if (_plannerItemsPanel == null) return;
            _plannerItemsPanel.Children.Clear();

            if (_plannerTasks.Count == 0)
            {
                _plannerItemsPanel.Children.Add(new TextBlock
                {
                    Text = "Задач пока нет.",
                    Foreground = Brush(115, 119, 127)
                });
                return;
            }

            foreach (PlannerTaskItem item in _plannerTasks
                .OrderBy(task => task.Completed)
                .ThenByDescending(task => task.CreatedAt)
                .ToList())
            {
                Grid row = new() { ColumnSpacing = 10 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                CheckBox check = new() { IsChecked = item.Completed };
                check.Checked += (_, _) =>
                {
                    item.Completed = true;
                    SavePlanner();
                    RenderPlanner();
                };
                check.Unchecked += (_, _) =>
                {
                    item.Completed = false;
                    SavePlanner();
                    RenderPlanner();
                };

                TextBlock text = new()
                {
                    Text = item.Text,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = item.Completed ? Brush(115, 119, 127) : null
                };
                Grid.SetColumn(text, 1);

                Button delete = new() { Content = "Удалить" };
                delete.Click += (_, _) =>
                {
                    _plannerTasks.Remove(item);
                    SavePlanner();
                    RenderPlanner();
                };
                Grid.SetColumn(delete, 2);

                row.Children.Add(check);
                row.Children.Add(text);
                row.Children.Add(delete);
                _plannerItemsPanel.Children.Add(row);
            }
        }

        private Grid BuildMaintenancePage()
        {
            var shell = CreatePageShell("Maintenance", "Безопасный анализ обслуживания Windows и свободного места");
            Border info = CreateCard();
            StackPanel body = new() { Spacing = 10 };

            body.Children.Add(new TextBlock
            {
                Text = "Maintenance пока работает в режиме анализа. Ничего автоматически не удаляется.",
                TextWrapping = TextWrapping.Wrap
            });

            Button analyze = new() { Content = "ANALYZE SYSTEM", Padding = new Thickness(16, 9, 16, 9) };
            analyze.Click += MaintenanceAnalyzeButton_Click;
            body.Children.Add(analyze);

            _maintenanceResultText = new TextBlock
            {
                Text = "Анализ ещё не запускался.",
                Foreground = Brush(154, 157, 165),
                TextWrapping = TextWrapping.Wrap
            };
            body.Children.Add(_maintenanceResultText);
            info.Child = body;
            shell.Body.Children.Add(info);
            return shell.Page;
        }

        private async void MaintenanceAnalyzeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_maintenanceResultText == null) return;
            _maintenanceResultText.Text = "Анализ временных файлов...";

            try
            {
                long tempBytes = await Task.Run(() => GetDirectorySizeSafe(Path.GetTempPath(), 25000));
                DriveInfo drive = new(@"C:\");
                double freeGb = drive.IsReady ? drive.AvailableFreeSpace / 1024d / 1024d / 1024d : 0;

                _maintenanceResultText.Text =
                    $"TEMP: примерно {FormatBytes(tempBytes)}\n" +
                    $"Свободно на C:: {freeGb:F1} GB\n" +
                    $"Заполнение C:: {DiskValueText.Text}\n\n" +
                    "Позже добавим безопасную очистку с предварительным списком файлов и подтверждением.";
            }
            catch (Exception ex)
            {
                _maintenanceResultText.Text = "Не удалось завершить анализ: " + ex.Message;
            }
        }

        private static long GetDirectorySizeSafe(string root, int maxFiles)
        {
            long total = 0;
            int count = 0;
            Stack<string> pending = new();
            pending.Push(root);

            while (pending.Count > 0 && count < maxFiles)
            {
                string current = pending.Pop();
                try
                {
                    foreach (string file in Directory.GetFiles(current))
                    {
                        if (count++ >= maxFiles) break;
                        try { total += new FileInfo(file).Length; } catch { }
                    }

                    foreach (string dir in Directory.GetDirectories(current))
                    {
                        try
                        {
                            FileAttributes attributes = File.GetAttributes(dir);
                            if ((attributes & FileAttributes.ReparsePoint) == 0)
                                pending.Push(dir);
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return total;
        }

        private Grid BuildAssistantPage()
        {
            var shell = CreatePageShell("AI Assistant", "Локальный помощник NEXUS. Полноценная LLM-интеграция будет отдельным этапом.");
            Border chatCard = CreateCard();
            StackPanel chat = new() { Spacing = 12 };

            _assistantOutputText = new TextBlock
            {
                Text = "NEXUS Assistant готов. Сейчас я умею отвечать по текущему состоянию железа. Попробуй: «что с диском», «температура», «система».",
                TextWrapping = TextWrapping.Wrap
            };
            chat.Children.Add(_assistantOutputText);

            Grid row = new() { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _assistantInput = new TextBox { PlaceholderText = "Спросить NEXUS..." };
            Button send = new() { Content = "Отправить" };
            send.Click += AssistantSendButton_Click;
            Grid.SetColumn(send, 1);

            row.Children.Add(_assistantInput);
            row.Children.Add(send);
            chat.Children.Add(row);
            chatCard.Child = chat;
            shell.Body.Children.Add(chatCard);
            return shell.Page;
        }

        private void AssistantSendButton_Click(object sender, RoutedEventArgs e)
        {
            string query = _assistantInput?.Text?.Trim().ToLowerInvariant() ?? "";
            if (string.IsNullOrWhiteSpace(query) || _assistantOutputText == null) return;

            string answer;
            if (query.Contains("диск") || query.Contains("ssd"))
                answer = $"Диск C: {DiskValueText.Text}. {DiskStatusText.Text}\nSSD: {StorageNameText.Text}, {StorageTempText.Text}, ресурс {StorageHealthText.Text}.";
            else if (query.Contains("температур") || query.Contains("гре"))
                answer = $"CPU: {CpuTempText.Text} ({CpuTempStatusText.Text})\nGPU: {GpuTempText.Text}\nSSD: {StorageTempText.Text}.";
            else if (query.Contains("систем") || query.Contains("комп") || query.Contains("желез"))
                answer = $"{Environment.MachineName}\n{OsNameText.Text}\nCPU: {CpuNameText.Text}\nRAM: {RamDetailsText.Text}\nGPU: {GpuNameText.Text}.";
            else if (query.Contains("памят") || query.Contains("ram"))
                answer = $"RAM сейчас: {RamValueText.Text}. {RamDetailsText.Text}.";
            else
                answer = "Пока это локальный помощник без облачной LLM. Позже подключим Logbook, Planner и полноценный AI-контекст.";

            _assistantOutputText.Text = answer;
            if (_assistantInput != null) _assistantInput.Text = "";
        }

        private Grid BuildSettingsPage()
        {
            var shell = CreatePageShell("Settings", "Настройки поведения NEXUS");
            Border card = CreateCard();
            StackPanel body = new() { Spacing = 14 };

            ToggleSwitch live = new() { Header = "LIVE обновление Logbook", IsOn = LiveToggle.IsOn };
            live.Toggled += (_, _) => LiveToggle.IsOn = live.IsOn;

            ToggleSwitch clipboard = new() { Header = "Журнал Clipboard", IsOn = true };
            clipboard.Toggled += (_, _) => SetClipboardLogging(clipboard.IsOn);

            body.Children.Add(live);
            body.Children.Add(clipboard);
            body.Children.Add(new TextBlock
            {
                Text = $"База Logbook: {_logService.DatabasePath}\nPlanner: {PlannerFilePath}",
                Foreground = Brush(115, 119, 127),
                FontFamily = new FontFamily("Consolas"),
                TextWrapping = TextWrapping.Wrap
            });

            card.Child = body;
            shell.Body.Children.Add(card);
            return shell.Page;
        }

        private void SetClipboardLogging(bool enabled)
        {
            if (_clipboardLoggingEnabled == enabled) return;
            _clipboardLoggingEnabled = enabled;

            if (enabled)
                Clipboard.ContentChanged += Clipboard_ContentChanged;
            else
                Clipboard.ContentChanged -= Clipboard_ContentChanged;
        }

        private Grid BuildDiagnosticsDepartmentPage()
        {
            var shell = CreatePageShell("Diagnostics", "Диагностика железа, температур, износа и рекомендации по обслуживанию");

            Grid header = new() { ColumnSpacing = 15 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel status = new() { Spacing = 5 };
            _diagnosticsDepartmentScore = new TextBlock
            {
                Text = "-- / 100",
                FontSize = 32,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            };
            _diagnosticsDepartmentStatus = new TextBlock
            {
                Text = "Диагностика ещё не запускалась",
                Foreground = Brush(154, 157, 165)
            };
            status.Children.Add(_diagnosticsDepartmentScore);
            status.Children.Add(_diagnosticsDepartmentStatus);

            Button run = new()
            {
                Content = "RUN HARDWARE DIAGNOSTICS",
                Padding = new Thickness(16, 9, 16, 9),
                VerticalAlignment = VerticalAlignment.Center
            };
            run.Click += (_, _) => RunHardwareDiagnosticsDepartment();
            Grid.SetColumn(run, 1);

            header.Children.Add(status);
            header.Children.Add(run);

            _diagnosticsDepartmentFindings = new StackPanel { Spacing = 10 };

            shell.Body.Children.Add(header);
            shell.Body.Children.Add(new TextBlock
            {
                Text = "Здесь только здоровье системы. Проверка вредоносов и признаков взлома вынесена отдельно в Security.",
                Foreground = Brush(115, 119, 127),
                TextWrapping = TextWrapping.Wrap
            });
            shell.Body.Children.Add(_diagnosticsDepartmentFindings);
            return shell.Page;
        }

        private void RunHardwareDiagnosticsDepartment()
        {
            if (_diagnosticsDepartmentScore == null ||
                _diagnosticsDepartmentStatus == null ||
                _diagnosticsDepartmentFindings == null)
                return;

            HardwareHealthSnapshot snapshot = CaptureHealthSnapshot();
            HealthDiagnosticReport report = _healthRecommendationService.Analyze(snapshot);

            _diagnosticsDepartmentScore.Text = $"{report.Score} / 100";
            _diagnosticsDepartmentStatus.Text = $"{report.Status} • {DateTime.Now:HH:mm:ss}";
            _diagnosticsDepartmentFindings.Children.Clear();

            foreach (DiagnosticFinding finding in report.Findings
                .OrderByDescending(item => SeverityRank(item.Severity)))
            {
                _diagnosticsDepartmentFindings.Children.Add(CreateFindingCard(finding));
            }

            _logService.Write(
                "Hardware",
                "DiagnosticsCompleted",
                "Diagnostics",
                "Диагностика железа завершена",
                $"Score {report.Score}/100 • {report.Status}");
        }

        private Grid BuildSecurityDepartmentPage()
        {
            var shell = CreatePageShell("Security", "Проверка подозрительных файлов, автозапуска, закрепления и признаков компрометации Windows");
            _securityDepartmentBody = shell.Body;

            Border intro = CreateCard();
            intro.Child = new TextBlock
            {
                Text = "Security и Diagnostics теперь разделены. Здесь остаются только проверки безопасности. NEXUS ничего не удаляет автоматически и показывает причины каждого предупреждения.",
                TextWrapping = TextWrapping.Wrap
            };
            shell.Body.Children.Add(intro);
            return shell.Page;
        }

        private void MoveCompromiseCardToSecurity()
        {
            if (_compromiseCard == null || _securityDepartmentBody == null) return;

            if (_diagnosticsView != null)
            {
                foreach (StackPanel panel in FindDescendants<StackPanel>(_diagnosticsView).ToList())
                {
                    int index = panel.Children.IndexOf(_compromiseCard);
                    if (index >= 0)
                    {
                        panel.Children.RemoveAt(index);
                        break;
                    }
                }
            }

            if (_securityDepartmentBody.Children.IndexOf(_compromiseCard) < 0)
                _securityDepartmentBody.Children.Add(_compromiseCard);
        }

        private static (Grid Page, StackPanel Body) CreatePageShell(string title, string subtitle)
        {
            Grid page = new() { Margin = new Thickness(30) };
            ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            StackPanel body = new() { Spacing = 20 };

            body.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 36,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            });
            body.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 16,
                Foreground = Brush(154, 157, 165),
                TextWrapping = TextWrapping.Wrap
            });

            scroll.Content = body;
            page.Children.Add(scroll);
            return (page, body);
        }

        private void UpdateVersionTo010(DependencyObject root)
        {
            foreach (TextBlock textBlock in FindDescendants<TextBlock>(root))
            {
                if (textBlock.Text.StartsWith("NEXUS v", StringComparison.OrdinalIgnoreCase))
                {
                    textBlock.Text = "NEXUS v0.1.0";
                    break;
                }
            }
        }

        public sealed class PlannerTaskItem
        {
            public string Id { get; set; } = "";
            public string Text { get; set; } = "";
            public DateTime CreatedAt { get; set; }
            public bool Completed { get; set; }
        }
    }
}

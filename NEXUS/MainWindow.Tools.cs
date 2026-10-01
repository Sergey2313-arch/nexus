using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NEXUS
{
    public sealed partial class MainWindow
    {
        private TextBlock? _logbookSummaryText;
        private StackPanel? _maintenanceLargeFilesPanel;
        private TextBlock? _maintenanceLargeFilesStatus;
        private bool _extraToolsInitialized;

        public void InitializeExtraTools()
        {
            if (_extraToolsInitialized)
                return;

            _extraToolsInitialized = true;

            EnhanceLogbookTools();
            EnhanceMaintenanceLargeFiles();
            EnhanceDeviceSnapshotExport();

            _logbookTimer.Tick += (_, _) =>
            {
                if (_isLogbookVisible)
                    RefreshLogbookSummary();
            };

            UpdateVersionTo012();
        }

        // ============================================================
        // LOGBOOK TOOLS
        // ============================================================

        private void EnhanceLogbookTools()
        {
            Grid? header = LogbookView.Children
                .OfType<Grid>()
                .FirstOrDefault(grid => Grid.GetRow(grid) == 0);

            if (header == null)
                return;

            StackPanel? left = header.Children
                .OfType<StackPanel>()
                .FirstOrDefault(panel => Grid.GetColumn(panel) == 0);

            StackPanel? right = header.Children
                .OfType<StackPanel>()
                .FirstOrDefault(panel => Grid.GetColumn(panel) == 1);

            if (left != null)
            {
                _logbookSummaryText = new TextBlock
                {
                    Text = "Сводка журнала: --",
                    Foreground = Brush(115, 119, 127),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                };
                left.Children.Add(_logbookSummaryText);
            }

            if (right != null)
            {
                Button export = new()
                {
                    Content = "Экспорт журнала"
                };
                export.Click += (_, _) => ExportLogbook();
                right.Children.Add(export);
            }

            RefreshLogbookSummary();
        }

        private void RefreshLogbookSummary()
        {
            if (_logbookSummaryText == null)
                return;

            try
            {
                List<LogEvent> events = _logService.GetLatest(1000);
                DateTime since = DateTime.Now.AddHours(-24);
                List<LogEvent> day = events.Where(item => item.Timestamp >= since).ToList();

                int applications = day.Count(item => item.Category == "Application");
                int processes = day.Count(item => item.Category == "Process");
                int files = day.Count(item => item.Category == "File");
                int security = day.Count(item => item.Category == "Security");

                _logbookSummaryText.Text =
                    $"24 часа: {day.Count} событий • программы {applications} • процессы {processes} • файлы {files} • security {security}";
            }
            catch
            {
                _logbookSummaryText.Text = "Сводка журнала недоступна";
            }
        }

        private void ExportLogbook()
        {
            try
            {
                Directory.CreateDirectory(NexusDataFolder);
                string path = Path.Combine(
                    NexusDataFolder,
                    $"logbook-{DateTime.Now:yyyyMMdd-HHmmss}.tsv");

                List<LogEvent> events = _logService.GetLatest(5000)
                    .OrderBy(item => item.Timestamp)
                    .ToList();

                using StreamWriter writer = new(path, false, Encoding.UTF8);
                writer.WriteLine("Timestamp\tCategory\tEventType\tSource\tTitle\tDetails\tPID\tFilePath\tSeverity");

                foreach (LogEvent item in events)
                {
                    writer.WriteLine(string.Join("\t",
                        Tsv(item.Timestamp.ToString("O")),
                        Tsv(item.Category),
                        Tsv(item.EventType),
                        Tsv(item.Source),
                        Tsv(item.Title),
                        Tsv(item.Details),
                        Tsv(item.ProcessId?.ToString() ?? ""),
                        Tsv(item.FilePath),
                        Tsv(item.Severity)));
                }

                OpenPath(path);
            }
            catch { }
        }

        private static string Tsv(string? value) =>
            (value ?? "")
                .Replace("\t", " ")
                .Replace("\r", " ")
                .Replace("\n", " ");

        // ============================================================
        // MAINTENANCE: LARGE FILES
        // ============================================================

        private void EnhanceMaintenanceLargeFiles()
        {
            StackPanel? body = GetPageBody(_maintenancePage);
            if (body == null)
                return;

            Border card = CreateCard();
            StackPanel panel = new() { Spacing = 10 };

            panel.Children.Add(new TextBlock
            {
                Text = "LARGE FILE ANALYZER",
                Foreground = Brush(154, 157, 165),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Ищет крупные файлы в Downloads, Desktop и Documents. Ничего не удаляет.",
                Foreground = Brush(115, 119, 127),
                TextWrapping = TextWrapping.Wrap
            });

            Button scan = new()
            {
                Content = "SCAN LARGE FILES",
                HorizontalAlignment = HorizontalAlignment.Left
            };
            scan.Click += MaintenanceLargeFilesScan_Click;
            panel.Children.Add(scan);

            _maintenanceLargeFilesStatus = new TextBlock
            {
                Text = "Проверка ещё не запускалась.",
                Foreground = Brush(154, 157, 165),
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(_maintenanceLargeFilesStatus);

            _maintenanceLargeFilesPanel = new StackPanel { Spacing = 6 };
            panel.Children.Add(_maintenanceLargeFilesPanel);

            card.Child = panel;
            body.Children.Add(card);
        }

        private async void MaintenanceLargeFilesScan_Click(object sender, RoutedEventArgs e)
        {
            if (_maintenanceLargeFilesStatus == null || _maintenanceLargeFilesPanel == null)
                return;

            if (sender is Button button)
                button.IsEnabled = false;

            _maintenanceLargeFilesStatus.Text = "Поиск крупных файлов...";
            _maintenanceLargeFilesPanel.Children.Clear();

            try
            {
                List<LargeFileItem> files = await Task.Run(ScanLargeUserFiles);

                if (files.Count == 0)
                {
                    _maintenanceLargeFilesStatus.Text = "Крупных файлов в проверенных папках не найдено.";
                    return;
                }

                long total = files.Sum(item => item.Size);
                _maintenanceLargeFilesStatus.Text =
                    $"Показано {files.Count} крупнейших файлов • суммарно {FormatBytes(total)}";

                foreach (LargeFileItem file in files)
                {
                    Grid row = new() { ColumnSpacing = 10 };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    StackPanel info = new() { Spacing = 2 };
                    info.Children.Add(new TextBlock
                    {
                        Text = file.Name,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                    info.Children.Add(new TextBlock
                    {
                        Text = file.Path,
                        Foreground = Brush(115, 119, 127),
                        FontSize = 11,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });

                    TextBlock size = new()
                    {
                        Text = FormatBytes(file.Size),
                        Foreground = Brush(200, 202, 208),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(size, 1);

                    row.Children.Add(info);
                    row.Children.Add(size);
                    _maintenanceLargeFilesPanel.Children.Add(row);
                }
            }
            catch (Exception ex)
            {
                _maintenanceLargeFilesStatus.Text = "Ошибка анализа: " + ex.Message;
            }
            finally
            {
                if (sender is Button button)
                    button.IsEnabled = true;
            }
        }

        private static List<LargeFileItem> ScanLargeUserFiles()
        {
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] roots =
            {
                Path.Combine(user, "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            const int maxFiles = 25000;
            const long minimumSize = 100L * 1024L * 1024L;
            int visited = 0;
            List<LargeFileItem> result = new();

            foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    continue;

                Stack<string> pending = new();
                pending.Push(root);

                while (pending.Count > 0 && visited < maxFiles)
                {
                    string current = pending.Pop();

                    try
                    {
                        foreach (string file in Directory.GetFiles(current))
                        {
                            if (visited++ >= maxFiles)
                                break;

                            try
                            {
                                FileInfo info = new(file);
                                if (info.Length >= minimumSize)
                                {
                                    result.Add(new LargeFileItem
                                    {
                                        Name = info.Name,
                                        Path = info.FullName,
                                        Size = info.Length
                                    });
                                }
                            }
                            catch { }
                        }

                        foreach (string directory in Directory.GetDirectories(current))
                        {
                            try
                            {
                                FileAttributes attributes = File.GetAttributes(directory);
                                if ((attributes & FileAttributes.ReparsePoint) == 0)
                                    pending.Push(directory);
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }

            return result
                .OrderByDescending(item => item.Size)
                .Take(15)
                .ToList();
        }

        // ============================================================
        // DEVICE SNAPSHOT EXPORT
        // ============================================================

        private void EnhanceDeviceSnapshotExport()
        {
            StackPanel? body = GetPageBody(_devicesPage);
            if (body == null)
                return;

            Button export = new()
            {
                Content = "Экспорт снимка системы",
                HorizontalAlignment = HorizontalAlignment.Left
            };
            export.Click += (_, _) => ExportDeviceSnapshot();
            body.Children.Insert(Math.Min(2, body.Children.Count), export);
        }

        private void ExportDeviceSnapshot()
        {
            try
            {
                Directory.CreateDirectory(NexusDataFolder);
                string path = Path.Combine(
                    NexusDataFolder,
                    $"system-snapshot-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

                List<string> lines = new()
                {
                    "NEXUS SYSTEM SNAPSHOT",
                    $"Created: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    "",
                    $"Device: {Environment.MachineName}",
                    $"OS: {OsNameText.Text}",
                    $"CPU: {CpuNameText.Text}",
                    $"CPU load: {CpuValueText.Text}",
                    $"CPU temp: {CpuTempText.Text}",
                    $"RAM: {RamDetailsText.Text}",
                    $"GPU: {GpuNameText.Text}",
                    $"GPU load: {GpuLoadText.Text}",
                    $"GPU temp: {GpuTempText.Text}",
                    $"SSD: {StorageNameText.Text}",
                    $"SSD temp: {StorageTempText.Text}",
                    $"SSD health: {StorageHealthText.Text}",
                    $"SSD life used: {StorageLifeUsedText.Text}",
                    $"Uptime: {UptimeText.Text}",
                    "",
                    "DRIVES"
                };

                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!drive.IsReady) continue;
                        lines.Add(
                            $"{drive.Name} total {FormatBytes(drive.TotalSize)} • free {FormatBytes(drive.AvailableFreeSpace)}");
                    }
                    catch { }
                }

                File.WriteAllLines(path, lines, Encoding.UTF8);
                OpenPath(path);
            }
            catch { }
        }

        private void UpdateVersionTo012()
        {
            if (Content is not DependencyObject root)
                return;

            foreach (TextBlock textBlock in FindDescendants<TextBlock>(root))
            {
                if (textBlock.Text.StartsWith("NEXUS v", StringComparison.OrdinalIgnoreCase))
                {
                    textBlock.Text = "NEXUS v0.1.2";
                    break;
                }
            }
        }

        private sealed class LargeFileItem
        {
            public string Name { get; set; } = "";
            public string Path { get; set; } = "";
            public long Size { get; set; }
        }
    }
}

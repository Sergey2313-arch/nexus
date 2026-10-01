using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;

namespace NEXUS
{
    public sealed partial class MainWindow
    {
        private readonly CompromiseScannerService _compromiseScannerService = new();

        private Border? _compromiseCard;
        private TextBlock? _compromiseStatusText;
        private TextBlock? _compromiseScoreText;
        private TextBlock? _compromiseSummaryText;
        private StackPanel? _compromiseFindingsPanel;
        private Button? _quickCompromiseButton;
        private Button? _deepCompromiseButton;
        private Button? _defenderQuickButton;
        private Button? _defenderFullButton;
        private CancellationTokenSource? _compromiseScanCts;

        public void InitializeCompromiseUI()
        {
            if (_compromiseCard != null || _diagnosticsView == null)
                return;

            ScrollViewer? scroll = FindDescendants<ScrollViewer>(_diagnosticsView).FirstOrDefault();
            if (scroll?.Content is not StackPanel body)
                return;

            _compromiseCard = BuildCompromiseCard();

            // Ставим Compromise Scanner сразу после верхних блоков диагностики.
            int index = Math.Min(4, body.Children.Count);
            body.Children.Insert(index, _compromiseCard);
        }

        private Border BuildCompromiseCard()
        {
            Border card = new()
            {
                Background = CompromiseBrush(24, 26, 31),
                BorderBrush = CompromiseBrush(43, 46, 53),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(22)
            };

            StackPanel body = new() { Spacing = 16 };

            Grid header = new();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel title = new() { Spacing = 4 };
            title.Children.Add(new TextBlock
            {
                Text = "COMPROMISE SCANNER",
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            });
            title.Children.Add(new TextBlock
            {
                Text = "Поиск подозрительных файлов, автозапуска и признаков закрепления в Windows",
                Foreground = CompromiseBrush(154, 157, 165),
                TextWrapping = TextWrapping.Wrap
            });

            _compromiseScoreText = new TextBlock
            {
                Text = "-- / 100",
                FontSize = 28,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_compromiseScoreText, 1);

            header.Children.Add(title);
            header.Children.Add(_compromiseScoreText);
            body.Children.Add(header);

            _compromiseStatusText = new TextBlock
            {
                Text = "Проверка ещё не запускалась",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            body.Children.Add(_compromiseStatusText);

            _compromiseSummaryText = new TextBlock
            {
                Text = "Quick Scan проверяет ключевые места. Deep Scan глубже проходит AppData и пользовательские каталоги.",
                Foreground = CompromiseBrush(154, 157, 165),
                TextWrapping = TextWrapping.Wrap
            };
            body.Children.Add(_compromiseSummaryText);

            StackPanel buttons = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10
            };

            _quickCompromiseButton = new Button
            {
                Content = "QUICK SECURITY SCAN",
                Padding = new Thickness(14, 9, 14, 9)
            };
            _quickCompromiseButton.Click += QuickCompromiseButton_Click;

            _deepCompromiseButton = new Button
            {
                Content = "DEEP FILE SCAN",
                Padding = new Thickness(14, 9, 14, 9)
            };
            _deepCompromiseButton.Click += DeepCompromiseButton_Click;

            _defenderQuickButton = new Button
            {
                Content = "DEFENDER QUICK SCAN",
                Padding = new Thickness(14, 9, 14, 9)
            };
            _defenderQuickButton.Click += DefenderQuickButton_Click;

            _defenderFullButton = new Button
            {
                Content = "DEFENDER FULL SCAN",
                Padding = new Thickness(14, 9, 14, 9)
            };
            _defenderFullButton.Click += DefenderFullButton_Click;

            buttons.Children.Add(_quickCompromiseButton);
            buttons.Children.Add(_deepCompromiseButton);
            buttons.Children.Add(_defenderQuickButton);
            buttons.Children.Add(_defenderFullButton);
            body.Children.Add(buttons);

            TextBlock note = new()
            {
                Text = "NEXUS использует эвристики: найденный объект не объявляется вирусом автоматически. Ничего не удаляется и не отключается без вашего решения.",
                Foreground = CompromiseBrush(115, 119, 127),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            body.Children.Add(note);

            _compromiseFindingsPanel = new StackPanel { Spacing = 10 };
            body.Children.Add(_compromiseFindingsPanel);

            card.Child = body;
            return card;
        }

        private async void QuickCompromiseButton_Click(object sender, RoutedEventArgs e)
        {
            await RunCompromiseScanAsync(false);
        }

        private async void DeepCompromiseButton_Click(object sender, RoutedEventArgs e)
        {
            await RunCompromiseScanAsync(true);
        }

        private async Task RunCompromiseScanAsync(bool deepScan)
        {
            if (_compromiseStatusText == null || _compromiseFindingsPanel == null)
                return;

            _compromiseScanCts?.Cancel();
            _compromiseScanCts?.Dispose();
            _compromiseScanCts = new CancellationTokenSource();

            SetCompromiseButtonsEnabled(false);
            _compromiseFindingsPanel.Children.Clear();
            _compromiseScoreText!.Text = "SCANNING";

            Progress<string> progress = new(message =>
            {
                if (_compromiseStatusText != null)
                    _compromiseStatusText.Text = message;
            });

            _logService.Write(
                "Security",
                deepScan ? "DeepScanStarted" : "QuickScanStarted",
                "CompromiseScanner",
                deepScan ? "Запущена глубокая проверка компрометации" : "Запущена быстрая проверка компрометации");

            try
            {
                CompromiseScanReport report = await _compromiseScannerService.RunAsync(
                    deepScan,
                    progress,
                    _compromiseScanCts.Token);

                RenderCompromiseReport(report);
                WriteCompromiseReportToLogbook(report);
            }
            catch (OperationCanceledException)
            {
                _compromiseStatusText.Text = "Проверка отменена";
                _compromiseScoreText.Text = "-- / 100";
            }
            catch (Exception ex)
            {
                _compromiseStatusText.Text = "Ошибка проверки";
                _compromiseScoreText.Text = "ERROR";

                _logService.Write(
                    "Security",
                    "ScanError",
                    "CompromiseScanner",
                    "Ошибка проверки компрометации",
                    ex.Message,
                    severity: "Warning");
            }
            finally
            {
                SetCompromiseButtonsEnabled(true);
            }
        }

        private void RenderCompromiseReport(CompromiseScanReport report)
        {
            if (_compromiseStatusText == null ||
                _compromiseScoreText == null ||
                _compromiseSummaryText == null ||
                _compromiseFindingsPanel == null)
                return;

            _compromiseScoreText.Text = $"{report.Score} / 100";
            _compromiseStatusText.Text = report.Status;

            _compromiseStatusText.Foreground = report.Status switch
            {
                "POSSIBLE COMPROMISE" => CompromiseBrush(255, 92, 92),
                "REVIEW REQUIRED" => CompromiseBrush(255, 175, 80),
                "ATTENTION" => CompromiseBrush(255, 210, 92),
                _ => CompromiseBrush(103, 209, 122)
            };

            _compromiseSummaryText.Text =
                $"Файлов просмотрено: {report.FilesScanned} • исполняемых/скриптов: {report.ExecutablesAndScriptsScanned} • " +
                $"подозрительных файлов: {report.SuspiciousFiles}\n" +
                $"Persistence: {report.PersistenceEntries} записей / {report.SuspiciousPersistenceEntries} предупреждений • " +
                $"Scheduled Tasks: {report.ScheduledTasks} / {report.SuspiciousScheduledTasks} • " +
                $"Services: {report.AutomaticServices} / {report.SuspiciousServices}\n" +
                $"Defender exclusions: {report.DefenderExclusions} / {report.SuspiciousDefenderExclusions} • " +
                $"Listening TCP: {report.ListeningPorts} • RDP: {FormatNullableBool(report.RdpEnabled)} • HOSTS: {(report.HostsModified ? "изменён" : "без пользовательских записей")}";

            _compromiseFindingsPanel.Children.Clear();

            foreach (CompromiseFinding finding in report.Findings
                         .OrderByDescending(item => item.Risk)
                         .ThenBy(item => item.Category)
                         .Take(80))
            {
                _compromiseFindingsPanel.Children.Add(CreateCompromiseFindingCard(finding));
            }

            if (report.Findings.Count > 80)
            {
                _compromiseFindingsPanel.Children.Add(new TextBlock
                {
                    Text = $"Показаны первые 80 результатов из {report.Findings.Count}. Остальные сохранены в отчёте сканирования только в памяти текущего запуска.",
                    Foreground = CompromiseBrush(154, 157, 165),
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }

        private Border CreateCompromiseFindingCard(CompromiseFinding finding)
        {
            Border card = new()
            {
                Background = CompromiseBrush(15, 17, 21),
                BorderBrush = RiskBrush(finding.Risk),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14)
            };

            StackPanel body = new() { Spacing = 5 };

            Grid header = new();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock risk = new()
            {
                Text = finding.Risk.ToString().ToUpperInvariant(),
                Foreground = RiskBrush(finding.Risk),
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Margin = new Thickness(0, 0, 10, 0)
            };

            TextBlock category = new()
            {
                Text = finding.Category,
                Foreground = CompromiseBrush(154, 157, 165)
            };
            Grid.SetColumn(category, 1);

            header.Children.Add(risk);
            header.Children.Add(category);
            body.Children.Add(header);

            body.Children.Add(new TextBlock
            {
                Text = finding.Title,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });

            if (!string.IsNullOrWhiteSpace(finding.Details))
            {
                body.Children.Add(new TextBlock
                {
                    Text = finding.Details,
                    Foreground = CompromiseBrush(154, 157, 165),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            if (!string.IsNullOrWhiteSpace(finding.Path))
            {
                body.Children.Add(new TextBlock
                {
                    Text = finding.Path,
                    Foreground = CompromiseBrush(115, 119, 127),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            if (!string.IsNullOrWhiteSpace(finding.Sha256))
            {
                body.Children.Add(new TextBlock
                {
                    Text = $"SHA-256: {finding.Sha256}",
                    Foreground = CompromiseBrush(115, 119, 127),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            if (!string.IsNullOrWhiteSpace(finding.Recommendation))
            {
                body.Children.Add(new TextBlock
                {
                    Text = $"Совет: {finding.Recommendation}",
                    Foreground = CompromiseBrush(200, 202, 208),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            card.Child = body;
            return card;
        }

        private void WriteCompromiseReportToLogbook(CompromiseScanReport report)
        {
            _logService.Write(
                "Security",
                "CompromiseScanCompleted",
                "CompromiseScanner",
                $"Проверка завершена: {report.Status}",
                $"Score {report.Score}/100 • files {report.FilesScanned} • suspicious files {report.SuspiciousFiles} • suspicious persistence {report.SuspiciousPersistenceEntries + report.SuspiciousScheduledTasks + report.SuspiciousServices}",
                severity: report.Status == "POSSIBLE COMPROMISE" ? "Critical" : report.Score < 90 ? "Warning" : "Info");

            foreach (CompromiseFinding finding in report.Findings
                         .Where(item => item.Risk >= CompromiseRisk.Medium)
                         .Take(40))
            {
                _logService.Write(
                    "Security",
                    finding.Category,
                    "CompromiseScanner",
                    finding.Title,
                    finding.Details,
                    filePath: finding.Path,
                    severity: finding.Risk >= CompromiseRisk.High ? "Critical" : "Warning");
            }
        }

        private void DefenderQuickButton_Click(object sender, RoutedEventArgs e)
        {
            StartDefenderScan("QuickScan", "Запущена быстрая проверка Microsoft Defender");
        }

        private void DefenderFullButton_Click(object sender, RoutedEventArgs e)
        {
            StartDefenderScan("FullScan", "Запущена полная проверка Microsoft Defender");
        }

        private void StartDefenderScan(string scanType, string logTitle)
        {
            try
            {
                ProcessStartInfo startInfo = new()
                {
                    FileName = "powershell.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-NonInteractive");
                startInfo.ArgumentList.Add("-Command");
                startInfo.ArgumentList.Add($"Start-MpScan -ScanType {scanType}");

                Process.Start(startInfo);

                if (_compromiseStatusText != null)
                    _compromiseStatusText.Text = logTitle + ". Сканирование продолжится средствами Windows Security.";

                _logService.Write(
                    "Security",
                    "DefenderScanStarted",
                    "Microsoft Defender",
                    logTitle);
            }
            catch (Exception ex)
            {
                if (_compromiseStatusText != null)
                    _compromiseStatusText.Text = "Не удалось запустить Microsoft Defender Scan";

                _logService.Write(
                    "Security",
                    "DefenderScanError",
                    "Microsoft Defender",
                    "Не удалось запустить Defender Scan",
                    ex.Message,
                    severity: "Warning");
            }
        }

        private void SetCompromiseButtonsEnabled(bool enabled)
        {
            if (_quickCompromiseButton != null) _quickCompromiseButton.IsEnabled = enabled;
            if (_deepCompromiseButton != null) _deepCompromiseButton.IsEnabled = enabled;
            if (_defenderQuickButton != null) _defenderQuickButton.IsEnabled = enabled;
            if (_defenderFullButton != null) _defenderFullButton.IsEnabled = enabled;
        }

        private static string FormatNullableBool(bool? value) =>
            value.HasValue ? (value.Value ? "ON" : "OFF") : "N/A";

        private static SolidColorBrush CompromiseBrush(byte r, byte g, byte b) =>
            new(Color.FromArgb(255, r, g, b));

        private static SolidColorBrush RiskBrush(CompromiseRisk risk) => risk switch
        {
            CompromiseRisk.Critical => CompromiseBrush(255, 72, 72),
            CompromiseRisk.High => CompromiseBrush(255, 120, 85),
            CompromiseRisk.Medium => CompromiseBrush(255, 193, 92),
            CompromiseRisk.Low => CompromiseBrush(115, 165, 255),
            _ => CompromiseBrush(132, 136, 145)
        };
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NEXUS
{
    public sealed partial class MainWindow
    {
        private readonly ProcessTrustInspectorService _processTrustInspectorService = new();
        private TextBlock? _processTrustSummaryText;
        private StackPanel? _processTrustItemsPanel;
        private Button? _processTrustRunButton;
        private Button? _processTrustExportButton;
        private ProcessTrustReport? _lastProcessTrustReport;
        private bool _processTrustInitialized;

        public void InitializeProcessTrustInspector()
        {
            if (_processTrustInitialized || _securityDepartmentBody == null)
                return;

            _processTrustInitialized = true;
            _securityDepartmentBody.Children.Add(BuildProcessTrustCard());
            UpdateVersionTo021();
        }

        private Border BuildProcessTrustCard()
        {
            Border card = CreateCard();
            StackPanel body = new() { Spacing = 12 };

            body.Children.Add(new TextBlock
            {
                Text = "PROCESS TRUST INSPECTOR",
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            });

            body.Children.Add(new TextBlock
            {
                Text = "Показывает путь процесса, издателя из FileVersionInfo и наличие встроенного сертификата. Отдельно отмечает запуск из TEMP/Downloads и системные имена вне Windows.",
                Foreground = Brush(115, 119, 127),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });

            _processTrustSummaryText = new TextBlock
            {
                Text = "Проверка ещё не запускалась.",
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap
            };
            body.Children.Add(_processTrustSummaryText);

            StackPanel actions = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10
            };

            _processTrustRunButton = new Button { Content = "SCAN RUNNING PROCESSES" };
            _processTrustRunButton.Click += async (_, _) => await RunProcessTrustInspectorAsync();

            _processTrustExportButton = new Button
            {
                Content = "EXPORT PROCESS TRUST",
                IsEnabled = false
            };
            _processTrustExportButton.Click += (_, _) => ExportProcessTrustReport();

            actions.Children.Add(_processTrustRunButton);
            actions.Children.Add(_processTrustExportButton);
            body.Children.Add(actions);

            body.Children.Add(new TextBlock
            {
                Text = "Важно: наличие сертификата не равно полной проверке доверия Authenticode, а отсутствие сертификата не означает вирус. Это один из сигналов для ручной проверки.",
                Foreground = Brush(115, 119, 127),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });

            _processTrustItemsPanel = new StackPanel { Spacing = 7 };
            body.Children.Add(_processTrustItemsPanel);

            card.Child = body;
            return card;
        }

        private async Task RunProcessTrustInspectorAsync()
        {
            if (_processTrustRunButton == null ||
                _processTrustSummaryText == null ||
                _processTrustItemsPanel == null)
                return;

            _processTrustRunButton.IsEnabled = false;
            _processTrustSummaryText.Text = "Чтение процессов, путей и информации об издателях...";
            _processTrustItemsPanel.Children.Clear();

            try
            {
                ProcessTrustReport report = await _processTrustInspectorService.RunAsync();
                _lastProcessTrustReport = report;

                _processTrustSummaryText.Text =
                    $"Процессов: {report.ProcessesSeen} • доступных путей: {report.PathsReadable} • с сертификатом: {report.Signed} • без сертификата: {report.Unsigned} • требуют просмотра: {report.ReviewRequired}";

                var review = report.Items
                    .Where(item => item.RiskPoints >= 20)
                    .OrderByDescending(item => item.RiskPoints)
                    .ThenBy(item => item.Name)
                    .ToList();

                if (review.Count == 0)
                {
                    _processTrustItemsPanel.Children.Add(new TextBlock
                    {
                        Text = "По текущим эвристикам процессов, требующих срочного просмотра, не найдено.",
                        Foreground = Brush(103, 209, 122),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                else
                {
                    foreach (ProcessTrustItem item in review.Take(30))
                        _processTrustItemsPanel.Children.Add(CreateProcessTrustRow(item));
                }

                // Для контекста показываем несколько крупнейших обычных процессов.
                foreach (ProcessTrustItem item in report.Items
                             .Where(item => item.RiskPoints < 20)
                             .OrderByDescending(item => item.MemoryBytes)
                             .Take(8))
                {
                    _processTrustItemsPanel.Children.Add(CreateProcessTrustRow(item));
                }

                if (_processTrustExportButton != null)
                    _processTrustExportButton.IsEnabled = true;

                _logService.Write(
                    "Security",
                    "ProcessTrustScanCompleted",
                    "ProcessTrustInspector",
                    "Проверены пути и издатели запущенных процессов",
                    $"Processes {report.ProcessesSeen} • readable {report.PathsReadable} • review {report.ReviewRequired}",
                    severity: report.ReviewRequired > 0 ? "Warning" : "Info");
            }
            catch (Exception ex)
            {
                _processTrustSummaryText.Text = "Ошибка Process Trust Inspector: " + ex.Message;
            }
            finally
            {
                _processTrustRunButton.IsEnabled = true;
            }
        }

        private Border CreateProcessTrustRow(ProcessTrustItem item)
        {
            bool warning = item.RiskPoints >= 20;

            Border row = new()
            {
                Background = Brush(15, 17, 21),
                BorderBrush = warning ? Brush(255, 175, 80) : Brush(43, 46, 53),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(11)
            };

            StackPanel body = new() { Spacing = 3 };

            string signed = item.HasEmbeddedCertificate switch
            {
                true => "certificate present",
                false => "no embedded certificate",
                _ => "certificate unknown"
            };

            body.Children.Add(new TextBlock
            {
                Text = $"{item.Name} • PID {item.ProcessId} • {signed}",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = warning ? Brush(255, 210, 92) : null,
                TextWrapping = TextWrapping.Wrap
            });

            string publisher = string.IsNullOrWhiteSpace(item.Company)
                ? "publisher: н/д"
                : "publisher: " + item.Company;

            body.Children.Add(new TextBlock
            {
                Text = publisher +
                       (string.IsNullOrWhiteSpace(item.Product) ? "" : " • " + item.Product),
                Foreground = Brush(154, 157, 165),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });

            body.Children.Add(new TextBlock
            {
                Text = item.Path,
                Foreground = Brush(115, 119, 127),
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap
            });

            if (warning)
            {
                string reason = string.Join("; ", new[]
                {
                    item.SuspiciousLocation ? "запуск из TEMP/Downloads" : "",
                    item.SystemLookalikeOutsideWindows ? "системное имя вне каталога Windows" : "",
                    item.HasEmbeddedCertificate == false && item.SuspiciousLocation ? "встроенный сертификат не найден" : ""
                }.Where(value => !string.IsNullOrWhiteSpace(value)));

                body.Children.Add(new TextBlock
                {
                    Text = "Проверить: " + reason,
                    Foreground = Brush(255, 175, 80),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            row.Child = body;
            return row;
        }

        private void ExportProcessTrustReport()
        {
            if (_lastProcessTrustReport == null)
                return;

            try
            {
                Directory.CreateDirectory(NexusDataFolder);
                string path = Path.Combine(
                    NexusDataFolder,
                    $"process-trust-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

                ProcessTrustReport report = _lastProcessTrustReport;
                StringBuilder text = new();
                text.AppendLine("NEXUS PROCESS TRUST REPORT");
                text.AppendLine($"Created: {report.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                text.AppendLine($"Processes seen: {report.ProcessesSeen}");
                text.AppendLine($"Readable paths: {report.PathsReadable}");
                text.AppendLine($"Certificate present: {report.Signed}");
                text.AppendLine($"No embedded certificate: {report.Unsigned}");
                text.AppendLine($"Review required: {report.ReviewRequired}");
                text.AppendLine();

                foreach (ProcessTrustItem item in report.Items
                             .OrderByDescending(value => value.RiskPoints)
                             .ThenBy(value => value.Name))
                {
                    text.AppendLine($"[{item.RiskPoints}] {item.Name} • PID {item.ProcessId}");
                    text.AppendLine($"  Path: {item.Path}");
                    text.AppendLine($"  Company: {item.Company}");
                    text.AppendLine($"  Product: {item.Product}");
                    text.AppendLine($"  Certificate present: {item.HasEmbeddedCertificate}");
                    if (!string.IsNullOrWhiteSpace(item.CertificateSubject))
                        text.AppendLine($"  Certificate subject: {item.CertificateSubject}");
                    text.AppendLine();
                }

                File.WriteAllText(path, text.ToString(), Encoding.UTF8);
                OpenPath(path);
            }
            catch
            {
            }
        }

        private void UpdateVersionTo021()
        {
            if (Content is not DependencyObject root)
                return;

            foreach (TextBlock textBlock in FindDescendants<TextBlock>(root))
            {
                if (textBlock.Text.StartsWith("NEXUS v", StringComparison.OrdinalIgnoreCase))
                {
                    textBlock.Text = "NEXUS v0.2.1";
                    break;
                }
            }
        }
    }
}

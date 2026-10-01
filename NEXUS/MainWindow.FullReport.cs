using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace NEXUS
{
    public sealed partial class MainWindow
    {
        private Border? _fullReportCard;
        private TextBlock? _fullReportStatusText;
        private TextBlock? _fullReportSummaryText;
        private StackPanel? _fullReportDetailsPanel;
        private Button? _fullReportRunButton;
        private Button? _fullReportExportButton;
        private FullSystemReport? _lastFullSystemReport;
        private bool _fullReportInitialized;

        public void InitializeFullSystemReportUI()
        {
            if (_fullReportInitialized)
                return;

            StackPanel? body = GetPageBody(_diagnosticsDepartmentPage);
            if (body == null)
                return;

            _fullReportInitialized = true;
            _fullReportCard = BuildFullSystemReportCard();
            body.Children.Add(_fullReportCard);
            UpdateVersionTo017();
        }

        private Border BuildFullSystemReportCard()
        {
            Border card = CreateCard();
            StackPanel body = new() { Spacing = 14 };

            Grid header = new();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel title = new() { Spacing = 3 };
            title.Children.Add(new TextBlock
            {
                Text = "FULL SYSTEM REPORT",
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            });
            title.Children.Add(new TextBlock
            {
                Text = "Один отчёт: железо, Windows Security, Event Log, процессы, диски и сеть",
                Foreground = Brush(154, 157, 165),
                TextWrapping = TextWrapping.Wrap
            });

            _fullReportStatusText = new TextBlock
            {
                Text = "REPORT NOT GENERATED",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_fullReportStatusText, 1);

            header.Children.Add(title);
            header.Children.Add(_fullReportStatusText);
            body.Children.Add(header);

            _fullReportSummaryText = new TextBlock
            {
                Text = "Запусти полный отчёт — NEXUS объединит диагностику железа и безопасности в одну понятную сводку.",
                Foreground = Brush(154, 157, 165),
                TextWrapping = TextWrapping.Wrap
            };
            body.Children.Add(_fullReportSummaryText);

            StackPanel buttons = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10
            };

            _fullReportRunButton = new Button { Content = "GENERATE FULL REPORT" };
            _fullReportRunButton.Click += FullReportRunButton_Click;

            _fullReportExportButton = new Button
            {
                Content = "EXPORT REPORT",
                IsEnabled = false
            };
            _fullReportExportButton.Click += (_, _) => ExportFullSystemReport();

            buttons.Children.Add(_fullReportRunButton);
            buttons.Children.Add(_fullReportExportButton);
            body.Children.Add(buttons);

            body.Children.Add(new TextBlock
            {
                Text = "Итоговый балл — удобный индикатор, а не медицинский/криминалистический диагноз компьютера. NEXUS показывает конкретные причины оценки ниже 100.",
                Foreground = Brush(115, 119, 127),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });

            _fullReportDetailsPanel = new StackPanel { Spacing = 10 };
            body.Children.Add(_fullReportDetailsPanel);

            card.Child = body;
            return card;
        }

        private async void FullReportRunButton_Click(object sender, RoutedEventArgs e)
        {
            await GenerateFullSystemReportAsync();
        }

        private async Task GenerateFullSystemReportAsync()
        {
            if (_fullReportRunButton == null ||
                _fullReportStatusText == null ||
                _fullReportSummaryText == null ||
                _fullReportDetailsPanel == null)
                return;

            _fullReportRunButton.IsEnabled = false;
            _fullReportStatusText.Text = "GENERATING...";
            _fullReportSummaryText.Text = "Сбор состояния железа, безопасности, событий Windows и ресурсов...";
            _fullReportDetailsPanel.Children.Clear();

            _logService.Write(
                "System",
                "FullReportStarted",
                "Diagnostics",
                "Запущен полный системный отчёт NEXUS");

            try
            {
                HardwareHealthSnapshot snapshot = CaptureHealthSnapshot();
                HealthDiagnosticReport health = _healthRecommendationService.Analyze(snapshot);

                SecurityDiagnosticReport security = await _securityDiagnosticsService.RunAsync();
                SecurityAuditReport audit = await _securityAuditService.RunAsync();

                FullSystemReport report = new()
                {
                    CreatedAt = DateTime.Now,
                    Hardware = health,
                    Security = security,
                    Audit = audit,
                    TopProcesses = CaptureTopProcesses(),
                    Drives = CaptureDriveReports(),
                    Network = CaptureNetworkReport()
                };

                report.OverallScore = (int)Math.Round(
                    health.Score * 0.40d +
                    security.Score * 0.35d +
                    audit.Score * 0.25d);

                report.ImportantFindings = health.Findings
                    .Concat(security.Findings)
                    .Concat(audit.Findings)
                    .Where(item => item.Severity == DiagnosticSeverity.Warning ||
                                   item.Severity == DiagnosticSeverity.Critical)
                    .OrderByDescending(item => FullReportSeverityRank(item.Severity))
                    .ThenBy(item => item.Category)
                    .Take(12)
                    .ToList();

                report.AiExplanation = BuildFullReportExplanation(report);

                _lastFullSystemReport = report;
                _lastHealthReport = health;
                _lastSecurityReport = security;
                _lastSecurityAuditReport = audit;

                RenderFullSystemReport(report);

                if (_fullReportExportButton != null)
                    _fullReportExportButton.IsEnabled = true;

                _logService.Write(
                    "System",
                    "FullReportCompleted",
                    "Diagnostics",
                    "Полный системный отчёт завершён",
                    $"Overall {report.OverallScore}/100 • Hardware {health.Score} • Security {security.Score} • Audit {audit.Score}",
                    severity: report.OverallScore < 75 ? "Warning" : "Info");
            }
            catch (Exception ex)
            {
                _fullReportStatusText.Text = "ERROR";
                _fullReportSummaryText.Text = "Не удалось сформировать полный отчёт: " + ex.Message;

                _logService.Write(
                    "System",
                    "FullReportError",
                    "Diagnostics",
                    "Ошибка полного системного отчёта",
                    ex.Message,
                    severity: "Warning");
            }
            finally
            {
                _fullReportRunButton.IsEnabled = true;
            }
        }

        private void RenderFullSystemReport(FullSystemReport report)
        {
            if (_fullReportStatusText == null ||
                _fullReportSummaryText == null ||
                _fullReportDetailsPanel == null)
                return;

            string status = report.OverallScore switch
            {
                >= 90 => "SYSTEM NORMAL",
                >= 75 => "ATTENTION",
                >= 55 => "REVIEW REQUIRED",
                _ => "HIGH RISK"
            };

            _fullReportStatusText.Text = $"{status} • {report.OverallScore}/100";
            _fullReportStatusText.Foreground = report.OverallScore switch
            {
                >= 90 => Brush(103, 209, 122),
                >= 75 => Brush(255, 210, 92),
                >= 55 => Brush(255, 175, 80),
                _ => Brush(255, 92, 92)
            };

            _fullReportSummaryText.Text =
                $"Overall: {report.OverallScore}/100 • Hardware: {report.Hardware.Score}/100 • Security: {report.Security.Score}/100 • Windows Audit: {report.Audit.Score}/100\n" +
                $"Процессов: {report.Security.RunningProcesses} • TCP established: {report.Security.EstablishedConnections} • listeners: {report.Security.ListeningPorts} • Defender detections 30d: {report.Audit.DefenderThreats30d}";

            _fullReportDetailsPanel.Children.Clear();

            _fullReportDetailsPanel.Children.Add(CreateFullReportTextCard(
                "NEXUS AI EXPLANATION",
                report.AiExplanation));

            string processText = report.TopProcesses.Count == 0
                ? "Данные недоступны."
                : string.Join("\n", report.TopProcesses.Select(item =>
                    $"• {item.Name} • PID {item.Pid} • {FormatBytes(item.MemoryBytes)} RAM"));

            _fullReportDetailsPanel.Children.Add(CreateFullReportTextCard(
                "TOP PROCESSES BY RAM",
                processText));

            string driveText = report.Drives.Count == 0
                ? "Данные недоступны."
                : string.Join("\n", report.Drives.Select(item =>
                    $"• {item.Name} • {item.UsedPercent:F0}% занято • {FormatBytes(item.FreeBytes)} свободно из {FormatBytes(item.TotalBytes)}"));

            _fullReportDetailsPanel.Children.Add(CreateFullReportTextCard(
                "STORAGE",
                driveText));

            _fullReportDetailsPanel.Children.Add(CreateFullReportTextCard(
                "NETWORK",
                report.Network));

            if (report.ImportantFindings.Count == 0)
            {
                _fullReportDetailsPanel.Children.Add(CreateFullReportTextCard(
                    "IMPORTANT FINDINGS",
                    "Критичных и предупреждающих результатов в текущем отчёте нет."));
            }
            else
            {
                TextBlock header = new()
                {
                    Text = "IMPORTANT FINDINGS",
                    Foreground = Brush(154, 157, 165),
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold
                };
                _fullReportDetailsPanel.Children.Add(header);

                foreach (DiagnosticFinding finding in report.ImportantFindings)
                    _fullReportDetailsPanel.Children.Add(CreateFindingCard(finding));
            }
        }

        private static Border CreateFullReportTextCard(string title, string text)
        {
            Border card = CreateCard();
            StackPanel body = new() { Spacing = 7 };

            body.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brush(154, 157, 165),
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            });

            body.Children.Add(new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush(220, 222, 228)
            });

            card.Child = body;
            return card;
        }

        private static List<FullReportProcess> CaptureTopProcesses()
        {
            List<FullReportProcess> result = new();

            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    result.Add(new FullReportProcess
                    {
                        Name = process.ProcessName,
                        Pid = process.Id,
                        MemoryBytes = process.WorkingSet64
                    });
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            return result
                .OrderByDescending(item => item.MemoryBytes)
                .Take(10)
                .ToList();
        }

        private static List<FullReportDrive> CaptureDriveReports()
        {
            List<FullReportDrive> result = new();

            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || drive.TotalSize <= 0)
                        continue;

                    long free = drive.AvailableFreeSpace;
                    long total = drive.TotalSize;
                    double used = (total - free) / (double)total * 100d;

                    result.Add(new FullReportDrive
                    {
                        Name = drive.Name,
                        TotalBytes = total,
                        FreeBytes = free,
                        UsedPercent = used
                    });
                }
                catch
                {
                }
            }

            return result;
        }

        private static string CaptureNetworkReport()
        {
            try
            {
                NetworkInterface[] adapters = NetworkInterface.GetAllNetworkInterfaces();
                List<string> lines = adapters
                    .Where(item => item.OperationalStatus == OperationalStatus.Up &&
                                   item.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Take(8)
                    .Select(item =>
                    {
                        IPInterfaceProperties props = item.GetIPProperties();
                        string ips = string.Join(", ", props.UnicastAddresses
                            .Select(address => address.Address.ToString())
                            .Take(4));

                        return $"• {item.Name} • {item.NetworkInterfaceType} • {(string.IsNullOrWhiteSpace(ips) ? "IP н/д" : ips)}";
                    })
                    .ToList();

                return lines.Count == 0
                    ? "Активные сетевые адаптеры не обнаружены."
                    : string.Join("\n", lines);
            }
            catch
            {
                return "Сетевые адаптеры недоступны.";
            }
        }

        private static string BuildFullReportExplanation(FullSystemReport report)
        {
            StringBuilder answer = new();
            answer.AppendLine($"Сводная оценка NEXUS: {report.OverallScore}/100.");
            answer.AppendLine($"Железо {report.Hardware.Score}/100, базовая безопасность {report.Security.Score}/100, аудит Windows {report.Audit.Score}/100.");

            if (report.ImportantFindings.Count == 0)
            {
                answer.Append("Сейчас в доступных данных нет предупреждений высокого приоритета. Это не гарантирует отсутствие вредоносного ПО, но явных проблем NEXUS не видит.");
                return answer.ToString();
            }

            answer.AppendLine("Что требует внимания в первую очередь:");
            int index = 1;
            foreach (DiagnosticFinding finding in report.ImportantFindings.Take(5))
            {
                answer.AppendLine($"{index}. {finding.Title}");
                if (!string.IsNullOrWhiteSpace(finding.Recommendation))
                    answer.AppendLine("   → " + finding.Recommendation);
                index++;
            }

            answer.Append("Не исправляй всё подряд автоматически: сначала сверяй предупреждение с тем, что ты сам устанавливал или настраивал.");
            return answer.ToString();
        }

        private bool TryBuildFullReportAssistantAnswer(string query, out string answer)
        {
            bool requested =
                query.Contains("отчет") ||
                query.Contains("отчёт") ||
                query.Contains("полная диагност") ||
                query.Contains("что не так") ||
                query.Contains("объясни диагност") ||
                query.Contains("объясни состояние");

            if (!requested)
            {
                answer = "";
                return false;
            }

            if (_lastFullSystemReport == null)
            {
                answer = "Полный системный отчёт ещё не сформирован. Открой Diagnostics и нажми GENERATE FULL REPORT — после этого я смогу объяснить результаты одной сводкой.";
                return true;
            }

            answer = _lastFullSystemReport.AiExplanation;
            return true;
        }

        private void ExportFullSystemReport()
        {
            if (_lastFullSystemReport == null)
                return;

            try
            {
                Directory.CreateDirectory(NexusDataFolder);
                string path = Path.Combine(
                    NexusDataFolder,
                    $"full-system-report-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

                FullSystemReport report = _lastFullSystemReport;
                StringBuilder text = new();

                text.AppendLine("NEXUS FULL SYSTEM REPORT");
                text.AppendLine($"Created: {report.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                text.AppendLine($"Device: {Environment.MachineName}");
                text.AppendLine($"OS: {OsNameText.Text}");
                text.AppendLine();
                text.AppendLine($"OVERALL: {report.OverallScore}/100");
                text.AppendLine($"Hardware: {report.Hardware.Score}/100 ({report.Hardware.Status})");
                text.AppendLine($"Security: {report.Security.Score}/100 ({report.Security.Status})");
                text.AppendLine($"Windows Audit: {report.Audit.Score}/100 ({report.Audit.Status})");
                text.AppendLine();
                text.AppendLine("AI EXPLANATION");
                text.AppendLine(report.AiExplanation);
                text.AppendLine();

                text.AppendLine("HARDWARE NOW");
                text.AppendLine($"CPU: {CpuNameText.Text} • {CpuValueText.Text} • {CpuTempText.Text}");
                text.AppendLine($"RAM: {RamDetailsText.Text}");
                text.AppendLine($"GPU: {GpuNameText.Text} • {GpuLoadText.Text} • {GpuTempText.Text}");
                text.AppendLine($"SSD: {StorageNameText.Text} • temp {StorageTempText.Text} • health {StorageHealthText.Text} • used life {StorageLifeUsedText.Text}");
                text.AppendLine();

                text.AppendLine("SECURITY SUMMARY");
                text.AppendLine($"Defender: {report.Security.DefenderEnabled}; realtime: {report.Security.RealTimeProtectionEnabled}; firewall: {report.Security.FirewallEnabled}");
                text.AppendLine($"Suspicious processes: {report.Security.SuspiciousProcesses}; autoruns: {report.Security.SuspiciousAutoruns}; services: {report.Security.SuspiciousServices}");
                text.AppendLine($"TCP established: {report.Security.EstablishedConnections}; listening: {report.Security.ListeningPorts}");
                text.AppendLine($"Failed logons 24h: {report.Audit.FailedLogons24h}; Defender detections 30d: {report.Audit.DefenderThreats30d}");
                text.AppendLine();

                text.AppendLine("TOP PROCESSES BY RAM");
                foreach (FullReportProcess process in report.TopProcesses)
                    text.AppendLine($"{process.Name}\tPID {process.Pid}\t{FormatBytes(process.MemoryBytes)}");
                text.AppendLine();

                text.AppendLine("DRIVES");
                foreach (FullReportDrive drive in report.Drives)
                    text.AppendLine($"{drive.Name}\t{drive.UsedPercent:F1}% used\t{FormatBytes(drive.FreeBytes)} free / {FormatBytes(drive.TotalBytes)}");
                text.AppendLine();

                text.AppendLine("NETWORK");
                text.AppendLine(report.Network);
                text.AppendLine();

                text.AppendLine("IMPORTANT FINDINGS");
                foreach (DiagnosticFinding finding in report.ImportantFindings)
                {
                    text.AppendLine($"[{finding.Severity}] [{finding.Category}] {finding.Title}");
                    if (!string.IsNullOrWhiteSpace(finding.Details))
                        text.AppendLine("  " + finding.Details);
                    if (!string.IsNullOrWhiteSpace(finding.Recommendation))
                        text.AppendLine("  Recommendation: " + finding.Recommendation);
                    text.AppendLine();
                }

                File.WriteAllText(path, text.ToString(), Encoding.UTF8);
                OpenPath(path);
            }
            catch
            {
            }
        }

        private static int FullReportSeverityRank(DiagnosticSeverity severity) => severity switch
        {
            DiagnosticSeverity.Critical => 4,
            DiagnosticSeverity.Warning => 3,
            DiagnosticSeverity.Info => 2,
            DiagnosticSeverity.Good => 1,
            _ => 0
        };

        private void UpdateVersionTo017()
        {
            if (Content is not DependencyObject root)
                return;

            foreach (TextBlock textBlock in FindDescendants<TextBlock>(root))
            {
                if (textBlock.Text.StartsWith("NEXUS v", StringComparison.OrdinalIgnoreCase))
                {
                    textBlock.Text = "NEXUS v0.1.7";
                    break;
                }
            }
        }

        private sealed class FullSystemReport
        {
            public DateTime CreatedAt { get; set; }
            public int OverallScore { get; set; }
            public HealthDiagnosticReport Hardware { get; set; } = new();
            public SecurityDiagnosticReport Security { get; set; } = new();
            public SecurityAuditReport Audit { get; set; } = new();
            public List<FullReportProcess> TopProcesses { get; set; } = new();
            public List<FullReportDrive> Drives { get; set; } = new();
            public string Network { get; set; } = "";
            public string AiExplanation { get; set; } = "";
            public List<DiagnosticFinding> ImportantFindings { get; set; } = new();
        }

        private sealed class FullReportProcess
        {
            public string Name { get; set; } = "";
            public int Pid { get; set; }
            public long MemoryBytes { get; set; }
        }

        private sealed class FullReportDrive
        {
            public string Name { get; set; } = "";
            public long TotalBytes { get; set; }
            public long FreeBytes { get; set; }
            public double UsedPercent { get; set; }
        }
    }
}

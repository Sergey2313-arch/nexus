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
        private readonly SecurityAuditService _securityAuditService = new();

        private Border? _securityAuditCard;
        private TextBlock? _securityAuditScoreText;
        private TextBlock? _securityAuditStatusText;
        private TextBlock? _securityAuditSummaryText;
        private StackPanel? _securityAuditFindingsPanel;
        private TextBlock? _integrityStatusText;
        private Button? _securityAuditRunButton;
        private Button? _integrityCreateButton;
        private Button? _integrityVerifyButton;
        private Button? _securityAuditExportButton;
        private SecurityAuditReport? _lastSecurityAuditReport;
        private IntegrityCheckResult? _lastIntegrityCheckResult;
        private bool _securityAuditInitialized;

        public void InitializeSecurityAuditUI()
        {
            if (_securityAuditInitialized || _securityDepartmentBody == null)
                return;

            _securityAuditInitialized = true;
            _securityAuditCard = BuildSecurityAuditCard();
            _securityDepartmentBody.Children.Add(_securityAuditCard);
            UpdateVersionTo016();
        }

        private Border BuildSecurityAuditCard()
        {
            Border card = CreateCard();
            StackPanel body = new() { Spacing = 14 };

            Grid header = new();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel title = new() { Spacing = 3 };
            title.Children.Add(new TextBlock
            {
                Text = "WINDOWS SECURITY AUDIT",
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            });
            title.Children.Add(new TextBlock
            {
                Text = "Event Log, Defender history, локальные администраторы и контроль целостности",
                Foreground = Brush(154, 157, 165),
                TextWrapping = TextWrapping.Wrap
            });

            _securityAuditScoreText = new TextBlock
            {
                Text = "-- / 100",
                FontSize = 27,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_securityAuditScoreText, 1);

            header.Children.Add(title);
            header.Children.Add(_securityAuditScoreText);
            body.Children.Add(header);

            _securityAuditStatusText = new TextBlock
            {
                Text = "Аудит ещё не запускался",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            body.Children.Add(_securityAuditStatusText);

            _securityAuditSummaryText = new TextBlock
            {
                Text = "NEXUS проверит события входа, установку служб, PowerShell audit, историю Defender и группу локальных администраторов.",
                Foreground = Brush(154, 157, 165),
                TextWrapping = TextWrapping.Wrap
            };
            body.Children.Add(_securityAuditSummaryText);

            StackPanel auditActions = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10
            };

            _securityAuditRunButton = new Button { Content = "RUN WINDOWS AUDIT" };
            _securityAuditRunButton.Click += SecurityAuditRunButton_Click;

            _securityAuditExportButton = new Button
            {
                Content = "EXPORT REPORT",
                IsEnabled = false
            };
            _securityAuditExportButton.Click += (_, _) => ExportSecurityAuditReport();

            auditActions.Children.Add(_securityAuditRunButton);
            auditActions.Children.Add(_securityAuditExportButton);
            body.Children.Add(auditActions);

            Border integrityCard = new()
            {
                Background = Brush(15, 17, 21),
                BorderBrush = Brush(43, 46, 53),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14)
            };

            StackPanel integrityBody = new() { Spacing = 9 };
            integrityBody.Children.Add(new TextBlock
            {
                Text = "INTEGRITY BASELINE",
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            });

            integrityBody.Children.Add(new TextBlock
            {
                Text = "Сохраняет SHA-256 для HOSTS, PowerShell profile и текущих файлов Startup. Потом NEXUS покажет изменения, пропажи и новые объекты.",
                Foreground = Brush(115, 119, 127),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });

            StackPanel integrityButtons = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10
            };

            _integrityCreateButton = new Button { Content = "CREATE / REFRESH BASELINE" };
            _integrityCreateButton.Click += IntegrityCreateButton_Click;

            _integrityVerifyButton = new Button { Content = "VERIFY INTEGRITY" };
            _integrityVerifyButton.Click += IntegrityVerifyButton_Click;

            integrityButtons.Children.Add(_integrityCreateButton);
            integrityButtons.Children.Add(_integrityVerifyButton);
            integrityBody.Children.Add(integrityButtons);

            _integrityStatusText = new TextBlock
            {
                Text = File.Exists(_securityAuditService.GetBaselineFilePath(NexusDataFolder))
                    ? "Baseline найден. Можно выполнить VERIFY INTEGRITY."
                    : "Baseline ещё не создан.",
                Foreground = Brush(154, 157, 165),
                TextWrapping = TextWrapping.Wrap
            };
            integrityBody.Children.Add(_integrityStatusText);

            integrityCard.Child = integrityBody;
            body.Children.Add(integrityCard);

            body.Children.Add(new TextBlock
            {
                Text = "Важно: счётчики событий и изменение файла — это доказательства активности, но не автоматический вывод «ПК взломан». NEXUS показывает факты и контекст, ничего не удаляет.",
                Foreground = Brush(115, 119, 127),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });

            _securityAuditFindingsPanel = new StackPanel { Spacing = 8 };
            body.Children.Add(_securityAuditFindingsPanel);

            card.Child = body;
            return card;
        }

        private async void SecurityAuditRunButton_Click(object sender, RoutedEventArgs e)
        {
            if (_securityAuditStatusText == null ||
                _securityAuditSummaryText == null ||
                _securityAuditScoreText == null ||
                _securityAuditFindingsPanel == null)
                return;

            if (_securityAuditRunButton != null)
                _securityAuditRunButton.IsEnabled = false;

            _securityAuditScoreText.Text = "SCANNING";
            _securityAuditStatusText.Text = "Чтение журналов Windows и Microsoft Defender...";
            _securityAuditFindingsPanel.Children.Clear();

            _logService.Write(
                "Security",
                "WindowsAuditStarted",
                "SecurityAudit",
                "Запущен Windows Security Audit");

            try
            {
                SecurityAuditReport report = await _securityAuditService.RunAsync();
                _lastSecurityAuditReport = report;
                RenderSecurityAuditReport(report);

                if (_securityAuditExportButton != null)
                    _securityAuditExportButton.IsEnabled = true;

                _logService.Write(
                    "Security",
                    "WindowsAuditCompleted",
                    "SecurityAudit",
                    $"Windows Security Audit завершён: {report.Status}",
                    $"Score {report.Score}/100 • failed logons {report.FailedLogons24h} • Defender threats {report.DefenderThreats30d} • service installs {report.ServiceInstallEvents7d}",
                    severity: report.Score < 75 ? "Warning" : "Info");

                foreach (DiagnosticFinding finding in report.Findings
                             .Where(item => item.Severity == DiagnosticSeverity.Warning ||
                                            item.Severity == DiagnosticSeverity.Critical)
                             .Take(20))
                {
                    _logService.Write(
                        "Security",
                        finding.Category,
                        "SecurityAudit",
                        finding.Title,
                        finding.Details,
                        severity: finding.Severity == DiagnosticSeverity.Critical ? "Critical" : "Warning");
                }
            }
            catch (Exception ex)
            {
                _securityAuditScoreText.Text = "ERROR";
                _securityAuditStatusText.Text = "Не удалось завершить аудит";
                _securityAuditSummaryText.Text = ex.Message;

                _logService.Write(
                    "Security",
                    "WindowsAuditError",
                    "SecurityAudit",
                    "Ошибка Windows Security Audit",
                    ex.Message,
                    severity: "Warning");
            }
            finally
            {
                if (_securityAuditRunButton != null)
                    _securityAuditRunButton.IsEnabled = true;
            }
        }

        private void RenderSecurityAuditReport(SecurityAuditReport report)
        {
            if (_securityAuditStatusText == null ||
                _securityAuditSummaryText == null ||
                _securityAuditScoreText == null ||
                _securityAuditFindingsPanel == null)
                return;

            _securityAuditScoreText.Text = $"{report.Score} / 100";
            _securityAuditStatusText.Text = report.Status;
            _securityAuditStatusText.Foreground = report.Status switch
            {
                "HIGH RISK" => Brush(255, 92, 92),
                "REVIEW REQUIRED" => Brush(255, 175, 80),
                "ATTENTION" => Brush(255, 210, 92),
                _ => Brush(103, 209, 122)
            };

            string defenderSignature = report.DefenderSignatureUpdatedAt.HasValue
                ? report.DefenderSignatureUpdatedAt.Value.ToString("dd.MM.yyyy HH:mm")
                : "н/д";

            _securityAuditSummaryText.Text =
                $"Failed logons 24h: {report.FailedLogons24h} • Successful: {report.SuccessfulLogons24h} • Security log: {(report.SecurityLogReadable ? "OK" : "LIMITED")}\n" +
                $"Service installs 7d: {report.ServiceInstallEvents7d} • PowerShell 4104 24h: {report.PowerShellScriptEvents24h}\n" +
                $"Defender detections 30d: {report.DefenderThreats30d} • signatures: {defenderSignature}\n" +
                $"Local administrators: {report.LocalAdministrators}";

            _securityAuditFindingsPanel.Children.Clear();
            foreach (DiagnosticFinding finding in report.Findings
                         .OrderByDescending(item => SecurityFindingRank(item.Severity))
                         .ThenBy(item => item.Category))
            {
                _securityAuditFindingsPanel.Children.Add(CreateSecurityAuditFindingCard(finding));
            }
        }

        private Border CreateSecurityAuditFindingCard(DiagnosticFinding finding)
        {
            Border card = new()
            {
                Background = Brush(15, 17, 21),
                BorderBrush = SecurityFindingBrush(finding.Severity),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12)
            };

            StackPanel panel = new() { Spacing = 4 };

            panel.Children.Add(new TextBlock
            {
                Text = $"{finding.Severity.ToString().ToUpperInvariant()} • {finding.Category}",
                Foreground = SecurityFindingBrush(finding.Severity),
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                FontSize = 11
            });

            panel.Children.Add(new TextBlock
            {
                Text = finding.Title,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });

            if (!string.IsNullOrWhiteSpace(finding.Details))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = finding.Details,
                    Foreground = Brush(154, 157, 165),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            if (!string.IsNullOrWhiteSpace(finding.Recommendation))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "Совет: " + finding.Recommendation,
                    Foreground = Brush(200, 202, 208),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            card.Child = panel;
            return card;
        }

        private async void IntegrityCreateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_integrityStatusText == null)
                return;

            SetIntegrityButtonsEnabled(false);
            _integrityStatusText.Text = "Создание baseline...";

            try
            {
                int count = await _securityAuditService.CreateIntegrityBaselineAsync(NexusDataFolder);
                _integrityStatusText.Text =
                    $"Baseline создан: {count} файлов • {DateTime.Now:dd.MM.yyyy HH:mm}. Создавай его только на системе, которой доверяешь.";

                _logService.Write(
                    "Security",
                    "IntegrityBaselineCreated",
                    "SecurityAudit",
                    "Создан integrity baseline",
                    $"Files: {count}");
            }
            catch (Exception ex)
            {
                _integrityStatusText.Text = "Ошибка создания baseline: " + ex.Message;
            }
            finally
            {
                SetIntegrityButtonsEnabled(true);
            }
        }

        private async void IntegrityVerifyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_integrityStatusText == null || _securityAuditFindingsPanel == null)
                return;

            SetIntegrityButtonsEnabled(false);
            _integrityStatusText.Text = "Проверка SHA-256 и контролируемых расположений...";

            try
            {
                IntegrityCheckResult result = await _securityAuditService.VerifyIntegrityBaselineAsync(NexusDataFolder);
                _lastIntegrityCheckResult = result;

                _integrityStatusText.Text = result.BaselineExists
                    ? $"Проверено: {result.Compared} • изменено: {result.Changed} • отсутствует: {result.Missing} • новых: {result.NewFiles}"
                    : "Baseline отсутствует. Сначала CREATE / REFRESH BASELINE.";

                foreach (DiagnosticFinding finding in result.Findings)
                    _securityAuditFindingsPanel.Children.Insert(0, CreateSecurityAuditFindingCard(finding));

                string severity = result.Changed > 0 || result.Missing > 0 ? "Warning" : "Info";
                _logService.Write(
                    "Security",
                    "IntegrityCheckCompleted",
                    "SecurityAudit",
                    "Integrity check завершён",
                    $"Compared {result.Compared} • changed {result.Changed} • missing {result.Missing} • new {result.NewFiles}",
                    severity: severity);
            }
            catch (Exception ex)
            {
                _integrityStatusText.Text = "Ошибка integrity check: " + ex.Message;
            }
            finally
            {
                SetIntegrityButtonsEnabled(true);
            }
        }

        private void SetIntegrityButtonsEnabled(bool enabled)
        {
            if (_integrityCreateButton != null) _integrityCreateButton.IsEnabled = enabled;
            if (_integrityVerifyButton != null) _integrityVerifyButton.IsEnabled = enabled;
        }

        private void ExportSecurityAuditReport()
        {
            if (_lastSecurityAuditReport == null)
                return;

            try
            {
                Directory.CreateDirectory(NexusDataFolder);
                string path = Path.Combine(
                    NexusDataFolder,
                    $"security-audit-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

                SecurityAuditReport report = _lastSecurityAuditReport;
                StringBuilder text = new();
                text.AppendLine("NEXUS WINDOWS SECURITY AUDIT");
                text.AppendLine($"Created: {report.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                text.AppendLine($"Status: {report.Status}");
                text.AppendLine($"Score: {report.Score}/100");
                text.AppendLine();
                text.AppendLine($"Security Event Log readable: {report.SecurityLogReadable}");
                text.AppendLine($"Failed logons 24h: {report.FailedLogons24h}");
                text.AppendLine($"Successful logons 24h: {report.SuccessfulLogons24h}");
                text.AppendLine($"Service install events 7d: {report.ServiceInstallEvents7d}");
                text.AppendLine($"PowerShell 4104 events 24h: {report.PowerShellScriptEvents24h}");
                text.AppendLine($"Defender detections 30d: {report.DefenderThreats30d}");
                text.AppendLine($"Defender signatures: {report.DefenderSignatureUpdatedAt:yyyy-MM-dd HH:mm}");
                text.AppendLine($"Local administrators: {report.LocalAdministrators}");
                text.AppendLine($"Administrators: {report.LocalAdministratorNames}");
                text.AppendLine();
                text.AppendLine("FINDINGS");

                foreach (DiagnosticFinding finding in report.Findings)
                {
                    text.AppendLine($"[{finding.Severity}] [{finding.Category}] {finding.Title}");
                    if (!string.IsNullOrWhiteSpace(finding.Details))
                        text.AppendLine("  " + finding.Details);
                    if (!string.IsNullOrWhiteSpace(finding.Recommendation))
                        text.AppendLine("  Recommendation: " + finding.Recommendation);
                    text.AppendLine();
                }

                if (_lastIntegrityCheckResult != null)
                {
                    IntegrityCheckResult integrity = _lastIntegrityCheckResult;
                    text.AppendLine("INTEGRITY CHECK");
                    text.AppendLine($"Compared: {integrity.Compared}; Changed: {integrity.Changed}; Missing: {integrity.Missing}; New: {integrity.NewFiles}");
                    foreach (DiagnosticFinding finding in integrity.Findings)
                        text.AppendLine($"[{finding.Severity}] {finding.Title}: {finding.Details}");
                }

                File.WriteAllText(path, text.ToString(), Encoding.UTF8);
                OpenPath(path);
            }
            catch
            {
            }
        }

        private static int SecurityFindingRank(DiagnosticSeverity severity) => severity switch
        {
            DiagnosticSeverity.Critical => 4,
            DiagnosticSeverity.Warning => 3,
            DiagnosticSeverity.Info => 2,
            DiagnosticSeverity.Good => 1,
            _ => 0
        };

        private static Microsoft.UI.Xaml.Media.SolidColorBrush SecurityFindingBrush(DiagnosticSeverity severity) => severity switch
        {
            DiagnosticSeverity.Critical => Brush(255, 92, 92),
            DiagnosticSeverity.Warning => Brush(255, 175, 80),
            DiagnosticSeverity.Good => Brush(103, 209, 122),
            _ => Brush(112, 168, 255)
        };

        private void UpdateVersionTo016()
        {
            if (Content is not DependencyObject root)
                return;

            foreach (TextBlock textBlock in FindDescendants<TextBlock>(root))
            {
                if (textBlock.Text.StartsWith("NEXUS v", StringComparison.OrdinalIgnoreCase))
                {
                    textBlock.Text = "NEXUS v0.1.6";
                    break;
                }
            }
        }
    }
}

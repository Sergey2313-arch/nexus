using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.UI;

namespace NEXUS
{
    public sealed partial class MainWindow
    {
        private readonly HealthRecommendationService _healthRecommendationService = new();
        private readonly SecurityDiagnosticsService _securityDiagnosticsService = new();

        private Grid? _diagnosticsView;
        private StackPanel? _diagnosticsFindingsPanel;

        private TextBlock? _overallScoreText;
        private TextBlock? _healthScoreText;
        private TextBlock? _securityScoreText;
        private TextBlock? _diagnosticsStatusText;
        private TextBlock? _defenderStateText;
        private TextBlock? _firewallStateText;
        private TextBlock? _servicesStateText;
        private TextBlock? _autorunsStateText;
        private TextBlock? _networkStateText;
        private TextBlock? _privilegesStateText;

        private Button? _runDiagnosticsButton;

        private HealthDiagnosticReport? _lastHealthReport;
        private SecurityDiagnosticReport? _lastSecurityReport;

        public void InitializeDiagnosticsUI()
        {
            if (_diagnosticsView != null)
                return;

            if (Content is not Grid root || root.Children.Count < 2)
                return;

            Grid? mainGrid = root.Children
                .OfType<Grid>()
                .FirstOrDefault(grid => Grid.GetColumn(grid) == 1);

            if (mainGrid == null)
                return;

            AddSecurityNavigation(root);
            AttachNavigationCleanup(root);
            UpdateVersionLabel(root);

            _diagnosticsView = BuildDiagnosticsView();
            Grid.SetRow(_diagnosticsView, 1);
            _diagnosticsView.Visibility = Visibility.Collapsed;

            mainGrid.Children.Add(_diagnosticsView);
        }

        private void AddSecurityNavigation(Grid root)
        {
            Border? sidebarBorder = root.Children
                .OfType<Border>()
                .FirstOrDefault(border => Grid.GetColumn(border) == 0);

            if (sidebarBorder?.Child is Grid sidebarGrid)
            {
                StackPanel? menu = sidebarGrid.Children
                    .OfType<StackPanel>()
                    .FirstOrDefault(panel => Grid.GetRow(panel) == 1);

                if (menu != null && !menu.Children
                    .OfType<Button>()
                    .Any(button => button.Content?.ToString() == "Security"))
                {
                    Button securityButton = new()
                    {
                        Content = "Security",
                        Height = 50,
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };

                    securityButton.Click += SecurityButton_Click;

                    int maintenanceIndex = menu.Children
                        .OfType<Button>()
                        .Select((button, index) => new { button, index })
                        .FirstOrDefault(item => item.button.Content?.ToString() == "Maintenance")
                        ?.index ?? menu.Children.Count;

                    menu.Children.Insert(maintenanceIndex, securityButton);
                }
            }

            Grid? mainGrid = root.Children
                .OfType<Grid>()
                .FirstOrDefault(grid => Grid.GetColumn(grid) == 1);

            Border? topBar = mainGrid?.Children
                .OfType<Border>()
                .FirstOrDefault(border => Grid.GetRow(border) == 0);

            if (topBar?.Child is StackPanel topMenu &&
                !topMenu.Children
                    .OfType<Button>()
                    .Any(button => button.Content?.ToString() == "Security"))
            {
                Button topSecurityButton = new()
                {
                    Content = "Security"
                };

                topSecurityButton.Click += SecurityButton_Click;

                int logbookIndex = topMenu.Children
                    .OfType<Button>()
                    .Select((button, index) => new { button, index })
                    .FirstOrDefault(item => item.button.Content?.ToString() == "Logbook")
                    ?.index ?? 0;

                topMenu.Children.Insert(Math.Min(logbookIndex + 1, topMenu.Children.Count), topSecurityButton);
            }
        }

        private void AttachNavigationCleanup(DependencyObject root)
        {
            foreach (Button button in FindDescendants<Button>(root))
            {
                string text = button.Content?.ToString() ?? "";

                if (text == "Dashboard" || text == "Logbook")
                {
                    button.Click += ExistingNavigationButton_Click;
                }
            }
        }

        private void ExistingNavigationButton_Click(object sender, RoutedEventArgs e)
        {
            if (_diagnosticsView != null)
            {
                _diagnosticsView.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateVersionLabel(DependencyObject root)
        {
            foreach (TextBlock textBlock in FindDescendants<TextBlock>(root))
            {
                if (textBlock.Text.StartsWith("NEXUS v", StringComparison.OrdinalIgnoreCase))
                {
                    textBlock.Text = "NEXUS v0.0.9";
                    break;
                }
            }
        }

        private async void SecurityButton_Click(object sender, RoutedEventArgs e)
        {
            _isLogbookVisible = false;

            DashboardView.Visibility = Visibility.Collapsed;
            LogbookView.Visibility = Visibility.Collapsed;

            if (_diagnosticsView != null)
            {
                _diagnosticsView.Visibility = Visibility.Visible;
            }

            if (_lastHealthReport == null || _lastSecurityReport == null)
            {
                await RunFullDiagnosticsAsync();
            }
        }

        private Grid BuildDiagnosticsView()
        {
            Grid page = new()
            {
                Margin = new Thickness(30)
            };

            ScrollViewer scroll = new()
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            StackPanel body = new()
            {
                Spacing = 20
            };

            Grid header = new();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel titlePanel = new() { Spacing = 4 };
            titlePanel.Children.Add(new TextBlock
            {
                Text = "Security & Diagnostics",
                FontSize = 36,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            });
            titlePanel.Children.Add(new TextBlock
            {
                Text = "Полная диагностика состояния, обслуживания и безопасности Windows",
                FontSize = 16,
                Foreground = Brush(154, 157, 165)
            });

            _runDiagnosticsButton = new Button
            {
                Content = "RUN FULL DIAGNOSTICS",
                Padding = new Thickness(18, 10, 18, 10),
                VerticalAlignment = VerticalAlignment.Center
            };
            _runDiagnosticsButton.Click += RunDiagnosticsButton_Click;
            Grid.SetColumn(_runDiagnosticsButton, 1);

            header.Children.Add(titlePanel);
            header.Children.Add(_runDiagnosticsButton);
            body.Children.Add(header);

            Grid scoreCards = new() { ColumnSpacing = 16 };
            scoreCards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            scoreCards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            scoreCards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _overallScoreText = new TextBlock { Text = "-- / 100", FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
            _healthScoreText = new TextBlock { Text = "-- / 100", FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
            _securityScoreText = new TextBlock { Text = "-- / 100", FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.Bold };

            Border overallCard = CreateMetricCard("SYSTEM HEALTH", _overallScoreText, "Итоговая оценка NEXUS");
            Border healthCard = CreateMetricCard("HARDWARE", _healthScoreText, "Температуры, износ, ресурсы");
            Border securityCard = CreateMetricCard("SECURITY", _securityScoreText, "Defender, Firewall, процессы, сервисы");

            Grid.SetColumn(overallCard, 0);
            Grid.SetColumn(healthCard, 1);
            Grid.SetColumn(securityCard, 2);

            scoreCards.Children.Add(overallCard);
            scoreCards.Children.Add(healthCard);
            scoreCards.Children.Add(securityCard);
            body.Children.Add(scoreCards);

            Border statusCard = CreateCard();
            StackPanel statusPanel = new() { Spacing = 15 };

            _diagnosticsStatusText = new TextBlock
            {
                Text = "Диагностика ещё не запускалась",
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            statusPanel.Children.Add(_diagnosticsStatusText);

            Grid statusGrid = new() { ColumnSpacing = 22, RowSpacing = 12 };
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statusGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            statusGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _defenderStateText = AddStatusCell(statusGrid, 0, 0, "Microsoft Defender");
            _firewallStateText = AddStatusCell(statusGrid, 1, 0, "Windows Firewall");
            _privilegesStateText = AddStatusCell(statusGrid, 2, 0, "Privileges");
            _servicesStateText = AddStatusCell(statusGrid, 0, 1, "Automatic services");
            _autorunsStateText = AddStatusCell(statusGrid, 1, 1, "Autoruns");
            _networkStateText = AddStatusCell(statusGrid, 2, 1, "Network");

            statusPanel.Children.Add(statusGrid);
            statusCard.Child = statusPanel;
            body.Children.Add(statusCard);

            TextBlock recommendationsHeader = new()
            {
                Text = "РЕКОМЕНДАЦИИ И НАЙДЕННЫЕ СОБЫТИЯ",
                Foreground = Brush(154, 157, 165),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            body.Children.Add(recommendationsHeader);

            _diagnosticsFindingsPanel = new StackPanel { Spacing = 10 };
            _diagnosticsFindingsPanel.Children.Add(CreateFindingCard(new DiagnosticFinding
            {
                Category = "NEXUS",
                Title = "Запустите полную диагностику",
                Details = "NEXUS проверит доступные датчики, износ SSD, свободное место, Defender, Firewall, автозагрузку, автоматические службы, процессы и сеть.",
                Recommendation = "Проверка работает в режиме read-only: NEXUS ничего не удаляет и не отключает.",
                Severity = DiagnosticSeverity.Info
            }));

            body.Children.Add(_diagnosticsFindingsPanel);

            scroll.Content = body;
            page.Children.Add(scroll);

            return page;
        }

        private async void RunDiagnosticsButton_Click(object sender, RoutedEventArgs e)
        {
            await RunFullDiagnosticsAsync();
        }

        private async Task RunFullDiagnosticsAsync()
        {
            if (_runDiagnosticsButton == null)
                return;

            _runDiagnosticsButton.IsEnabled = false;
            _runDiagnosticsButton.Content = "DIAGNOSTICS RUNNING...";

            if (_diagnosticsStatusText != null)
            {
                _diagnosticsStatusText.Text = "Проверка системы...";
            }

            _logService.Write(
                "Security",
                "DiagnosticsStarted",
                "NEXUS",
                "Полная диагностика запущена");

            try
            {
                HardwareHealthSnapshot snapshot = CaptureHealthSnapshot();

                HealthDiagnosticReport health =
                    _healthRecommendationService.Analyze(snapshot);

                SecurityDiagnosticReport security =
                    await _securityDiagnosticsService.RunAsync();

                _lastHealthReport = health;
                _lastSecurityReport = security;

                RenderDiagnostics(health, security);

                int overall = (int)Math.Round((health.Score + security.Score) / 2d);

                _logService.Write(
                    "Security",
                    "DiagnosticsCompleted",
                    "NEXUS",
                    "Полная диагностика завершена",
                    $"System {overall}/100 • Hardware {health.Score}/100 • Security {security.Score}/100");

                foreach (DiagnosticFinding finding in health.Findings
                    .Concat(security.Findings)
                    .Where(item => item.Severity == DiagnosticSeverity.Warning ||
                                   item.Severity == DiagnosticSeverity.Critical)
                    .Take(25))
                {
                    _logService.Write(
                        finding.Category == "CPU" || finding.Category == "GPU" || finding.Category == "Storage"
                            ? "Hardware"
                            : "Security",
                        "Finding",
                        finding.Category,
                        finding.Title,
                        finding.Recommendation,
                        severity: finding.Severity.ToString());
                }
            }
            catch (Exception ex)
            {
                if (_diagnosticsStatusText != null)
                {
                    _diagnosticsStatusText.Text = "Ошибка диагностики";
                }

                _logService.Write(
                    "Security",
                    "DiagnosticsError",
                    "NEXUS",
                    "Ошибка полной диагностики",
                    ex.Message,
                    severity: "Warning");
            }
            finally
            {
                _runDiagnosticsButton.IsEnabled = true;
                _runDiagnosticsButton.Content = "RUN FULL DIAGNOSTICS";
            }
        }

        private HardwareHealthSnapshot CaptureHealthSnapshot()
        {
            return new HardwareHealthSnapshot
            {
                CpuTemperature = ParseOptionalNumber(CpuTempText.Text),
                CpuLoad = CpuProgress.Value,
                GpuTemperature = ParseOptionalNumber(GpuTempText.Text),
                GpuLoad = GpuProgress.Value,
                StorageTemperature = ParseOptionalNumber(StorageTempText.Text),
                StorageHealth = ParseOptionalNumber(StorageHealthText.Text),
                StorageLifeUsed = ParseOptionalNumber(StorageLifeUsedText.Text),
                DiskUsedPercent = DiskProgress.Value,
                RamUsedPercent = RamProgress.Value,
                StorageName = StorageNameText.Text ?? ""
            };
        }

        private void RenderDiagnostics(
            HealthDiagnosticReport health,
            SecurityDiagnosticReport security)
        {
            int overall = (int)Math.Round((health.Score + security.Score) / 2d);

            if (_overallScoreText != null)
                _overallScoreText.Text = $"{overall} / 100";

            if (_healthScoreText != null)
                _healthScoreText.Text = $"{health.Score} / 100";

            if (_securityScoreText != null)
                _securityScoreText.Text = $"{security.Score} / 100";

            if (_diagnosticsStatusText != null)
            {
                _diagnosticsStatusText.Text =
                    $"{GetOverallStatus(overall)} • проверено {DateTime.Now:HH:mm:ss}";
            }

            if (_defenderStateText != null)
            {
                _defenderStateText.Text = security.DefenderEnabled switch
                {
                    true when security.RealTimeProtectionEnabled == true => "✓ ACTIVE • realtime ON",
                    true => "⚠ ACTIVE • realtime OFF",
                    false => "✖ DISABLED",
                    _ => "ℹ недоступно"
                };
            }

            if (_firewallStateText != null)
            {
                _firewallStateText.Text = security.FirewallEnabled switch
                {
                    true => "✓ ENABLED",
                    false => "⚠ DISABLED PROFILE",
                    _ => "ℹ недоступно"
                };
            }

            if (_privilegesStateText != null)
            {
                _privilegesStateText.Text = security.IsAdministrator
                    ? "Administrator"
                    : "Standard user";
            }

            if (_servicesStateText != null)
            {
                _servicesStateText.Text =
                    $"{security.AutomaticServices} auto • {security.SuspiciousServices} review";
            }

            if (_autorunsStateText != null)
            {
                _autorunsStateText.Text =
                    $"{security.AutorunEntries} entries • {security.SuspiciousAutoruns} review";
            }

            if (_networkStateText != null)
            {
                _networkStateText.Text =
                    $"{security.EstablishedConnections} connected • {security.ListeningPorts} listen";
            }

            if (_diagnosticsFindingsPanel == null)
                return;

            _diagnosticsFindingsPanel.Children.Clear();

            IEnumerable<DiagnosticFinding> findings =
                health.Findings
                    .Concat(security.Findings)
                    .OrderByDescending(item => SeverityRank(item.Severity))
                    .ThenBy(item => item.Category);

            foreach (DiagnosticFinding finding in findings)
            {
                _diagnosticsFindingsPanel.Children.Add(CreateFindingCard(finding));
            }
        }

        private static int SeverityRank(DiagnosticSeverity severity)
        {
            return severity switch
            {
                DiagnosticSeverity.Critical => 4,
                DiagnosticSeverity.Warning => 3,
                DiagnosticSeverity.Info => 2,
                DiagnosticSeverity.Good => 1,
                _ => 0
            };
        }

        private static string GetOverallStatus(int score)
        {
            return score switch
            {
                >= 90 => "✓ SYSTEM NORMAL",
                >= 75 => "ℹ ATTENTION",
                >= 55 => "⚠ SERVICE / SECURITY CHECK ADVISED",
                _ => "✖ HIGH RISK"
            };
        }

        private static Border CreateMetricCard(
            string title,
            TextBlock value,
            string subtitle)
        {
            Border card = CreateCard();
            StackPanel panel = new() { Spacing = 8 };

            panel.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brush(154, 157, 165)
            });

            panel.Children.Add(value);

            panel.Children.Add(new TextBlock
            {
                Text = subtitle,
                Foreground = Brush(115, 119, 127),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });

            card.Child = panel;
            return card;
        }

        private static Border CreateCard()
        {
            return new Border
            {
                Background = Brush(24, 26, 31),
                BorderBrush = Brush(43, 46, 53),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(20)
            };
        }

        private static TextBlock AddStatusCell(
            Grid grid,
            int column,
            int row,
            string title)
        {
            StackPanel panel = new() { Spacing = 4 };

            panel.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brush(115, 119, 127),
                FontSize = 12
            });

            TextBlock value = new()
            {
                Text = "--",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };

            panel.Children.Add(value);

            Grid.SetColumn(panel, column);
            Grid.SetRow(panel, row);
            grid.Children.Add(panel);

            return value;
        }

        private static Border CreateFindingCard(DiagnosticFinding finding)
        {
            Border card = CreateCard();
            card.Padding = new Thickness(16);

            StackPanel panel = new() { Spacing = 5 };

            string icon = finding.Severity switch
            {
                DiagnosticSeverity.Good => "✓",
                DiagnosticSeverity.Warning => "⚠",
                DiagnosticSeverity.Critical => "✖",
                _ => "ℹ"
            };

            panel.Children.Add(new TextBlock
            {
                Text = $"{icon} {finding.Category.ToUpperInvariant()}  •  {finding.Title}",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });

            if (!string.IsNullOrWhiteSpace(finding.Details))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = finding.Details,
                    Foreground = Brush(154, 157, 165),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            if (!string.IsNullOrWhiteSpace(finding.Recommendation))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "Совет: " + finding.Recommendation,
                    Foreground = Brush(154, 157, 165),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            card.Child = panel;
            return card;
        }

        private static double? ParseOptionalNumber(string? text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Contains("--"))
                return null;

            Match match = Regex.Match(text, @"-?\d+(?:[\.,]\d+)?");
            if (!match.Success)
                return null;

            string value = match.Value.Replace(',', '.');

            return double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double result)
                ? result
                : null;
        }

        private static SolidColorBrush Brush(byte r, byte g, byte b)
        {
            return new SolidColorBrush(Color.FromArgb(255, r, g, b));
        }

        private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
            where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);

            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);

                if (child is T typed)
                    yield return typed;

                foreach (T descendant in FindDescendants<T>(child))
                    yield return descendant;
            }
        }
    }
}
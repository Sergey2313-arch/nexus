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
        private readonly SystemActivityInspectorService _systemActivityInspectorService = new();
        private TextBlock? _activityInspectorSummaryText;
        private StackPanel? _activityServicesPanel;
        private StackPanel? _activityNetworkPanel;
        private Button? _activityRefreshButton;
        private Button? _activityExportButton;
        private SystemActivityReport? _lastActivityReport;
        private bool _activityInspectorInitialized;

        public void InitializeActivityInspector()
        {
            if (_activityInspectorInitialized)
                return;

            StackPanel? body = GetPageBody(_diagnosticsDepartmentPage);
            if (body == null)
                return;

            _activityInspectorInitialized = true;
            body.Children.Add(BuildActivityInspectorCard());
            UpdateVersionTo019();
        }

        private Border BuildActivityInspectorCard()
        {
            Border card = CreateCard();
            StackPanel body = new() { Spacing = 12 };

            body.Children.Add(new TextBlock
            {
                Text = "SERVICES & NETWORK ACTIVITY",
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            });

            body.Children.Add(new TextBlock
            {
                Text = "Показывает запущенные Windows-службы, память их host-процессов и текущие TCP-соединения. Режим read-only.",
                Foreground = Brush(115, 119, 127),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });

            _activityInspectorSummaryText = new TextBlock
            {
                Text = "Проверка ещё не запускалась.",
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap
            };
            body.Children.Add(_activityInspectorSummaryText);

            StackPanel actions = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10
            };

            _activityRefreshButton = new Button { Content = "REFRESH ACTIVITY" };
            _activityRefreshButton.Click += async (_, _) => await RefreshActivityInspectorAsync();

            _activityExportButton = new Button
            {
                Content = "EXPORT ACTIVITY",
                IsEnabled = false
            };
            _activityExportButton.Click += (_, _) => ExportActivityReport();

            actions.Children.Add(_activityRefreshButton);
            actions.Children.Add(_activityExportButton);
            body.Children.Add(actions);

            Border servicesCard = CreateCard();
            servicesCard.Padding = new Thickness(14);
            StackPanel servicesBody = new() { Spacing = 8 };
            servicesBody.Children.Add(new TextBlock
            {
                Text = "TOP RUNNING SERVICES BY HOST RAM",
                Foreground = Brush(154, 157, 165),
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            });
            servicesBody.Children.Add(new TextBlock
            {
                Text = "Для svchost память относится ко всему host-процессу, а не к одной службе — это ориентир, не точный расход конкретной службы.",
                Foreground = Brush(115, 119, 127),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });
            _activityServicesPanel = new StackPanel { Spacing = 6 };
            servicesBody.Children.Add(_activityServicesPanel);
            servicesCard.Child = servicesBody;
            body.Children.Add(servicesCard);

            Border networkCard = CreateCard();
            networkCard.Padding = new Thickness(14);
            StackPanel networkBody = new() { Spacing = 8 };
            networkBody.Children.Add(new TextBlock
            {
                Text = "TCP CONNECTIONS",
                Foreground = Brush(154, 157, 165),
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            });
            networkBody.Children.Add(new TextBlock
            {
                Text = "Наличие соединения само по себе не означает угрозу. NEXUS показывает PID и процесс, чтобы было понятно, кто держит подключение или порт.",
                Foreground = Brush(115, 119, 127),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });
            _activityNetworkPanel = new StackPanel { Spacing = 6 };
            networkBody.Children.Add(_activityNetworkPanel);
            networkCard.Child = networkBody;
            body.Children.Add(networkCard);

            card.Child = body;
            return card;
        }

        private async Task RefreshActivityInspectorAsync()
        {
            if (_activityRefreshButton == null ||
                _activityInspectorSummaryText == null ||
                _activityServicesPanel == null ||
                _activityNetworkPanel == null)
                return;

            _activityRefreshButton.IsEnabled = false;
            _activityInspectorSummaryText.Text = "Сбор Windows Services и TCP activity...";
            _activityServicesPanel.Children.Clear();
            _activityNetworkPanel.Children.Clear();

            try
            {
                SystemActivityReport report = await _systemActivityInspectorService.RunAsync();
                _lastActivityReport = report;

                _activityInspectorSummaryText.Text =
                    $"Running services: {report.RunningServices} • automatic: {report.AutomaticRunningServices} • TCP established: {report.EstablishedConnections} • listeners: {report.ListeningConnections}";

                foreach (ServiceRuntimeInfo service in report.Services
                             .OrderByDescending(item => item.MemoryBytes)
                             .ThenBy(item => item.DisplayName)
                             .Take(15))
                {
                    _activityServicesPanel.Children.Add(CreateActivityRow(
                        string.IsNullOrWhiteSpace(service.DisplayName) ? service.Name : service.DisplayName,
                        $"{service.Name} • {service.StartMode} • PID {service.ProcessId}",
                        service.MemoryBytes > 0 ? FormatBytes(service.MemoryBytes) : "н/д"));
                }

                if (report.Services.Count == 0)
                {
                    _activityServicesPanel.Children.Add(new TextBlock
                    {
                        Text = "Список служб недоступен.",
                        Foreground = Brush(115, 119, 127)
                    });
                }

                foreach (NetworkConnectionInfo connection in report.Connections
                             .OrderBy(item => string.Equals(item.State, "Established", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                             .ThenBy(item => item.ProcessName)
                             .Take(35))
                {
                    string remote = string.Equals(connection.State, "Listen", StringComparison.OrdinalIgnoreCase)
                        ? "LISTEN"
                        : $"{connection.RemoteAddress}:{connection.RemotePort}";

                    string local = $"{connection.LocalAddress}:{connection.LocalPort}";
                    string title = $"{connection.ProcessName} • PID {connection.ProcessId}";
                    string details = $"{connection.State} • {local} → {remote}";

                    _activityNetworkPanel.Children.Add(CreateActivityRow(title, details, ""));
                }

                if (report.Connections.Count == 0)
                {
                    _activityNetworkPanel.Children.Add(new TextBlock
                    {
                        Text = "Активные TCP-соединения не получены.",
                        Foreground = Brush(115, 119, 127)
                    });
                }

                if (_activityExportButton != null)
                    _activityExportButton.IsEnabled = true;

                _logService.Write(
                    "System",
                    "ActivityInspectorRefreshed",
                    "Diagnostics",
                    "Обновлены службы и сетевые соединения",
                    $"Services {report.RunningServices} • Established {report.EstablishedConnections} • Listeners {report.ListeningConnections}");
            }
            catch (Exception ex)
            {
                _activityInspectorSummaryText.Text = "Ошибка Activity Inspector: " + ex.Message;
            }
            finally
            {
                _activityRefreshButton.IsEnabled = true;
            }
        }

        private static Grid CreateActivityRow(string title, string details, string value)
        {
            Grid row = new() { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel info = new() { Spacing = 2 };
            info.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            info.Children.Add(new TextBlock
            {
                Text = details,
                Foreground = Brush(115, 119, 127),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });

            row.Children.Add(info);

            if (!string.IsNullOrWhiteSpace(value))
            {
                TextBlock valueText = new()
                {
                    Text = value,
                    Foreground = Brush(200, 202, 208),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(valueText, 1);
                row.Children.Add(valueText);
            }

            return row;
        }

        private void ExportActivityReport()
        {
            if (_lastActivityReport == null)
                return;

            try
            {
                Directory.CreateDirectory(NexusDataFolder);
                string path = Path.Combine(
                    NexusDataFolder,
                    $"activity-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

                SystemActivityReport report = _lastActivityReport;
                StringBuilder text = new();
                text.AppendLine("NEXUS SERVICES & NETWORK ACTIVITY");
                text.AppendLine($"Created: {report.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                text.AppendLine($"Running services: {report.RunningServices}");
                text.AppendLine($"Automatic running services: {report.AutomaticRunningServices}");
                text.AppendLine($"TCP established: {report.EstablishedConnections}");
                text.AppendLine($"TCP listeners: {report.ListeningConnections}");
                text.AppendLine();

                text.AppendLine("SERVICES");
                foreach (ServiceRuntimeInfo service in report.Services
                             .OrderByDescending(item => item.MemoryBytes))
                {
                    text.AppendLine($"{service.DisplayName}\t{service.Name}\t{service.StartMode}\tPID {service.ProcessId}\t{FormatBytes(service.MemoryBytes)}\t{service.Path}");
                }

                text.AppendLine();
                text.AppendLine("TCP CONNECTIONS");
                foreach (NetworkConnectionInfo connection in report.Connections)
                {
                    text.AppendLine(
                        $"{connection.State}\t{connection.ProcessName}\tPID {connection.ProcessId}\t{connection.LocalAddress}:{connection.LocalPort}\t{connection.RemoteAddress}:{connection.RemotePort}\t{connection.ProcessPath}");
                }

                File.WriteAllText(path, text.ToString(), Encoding.UTF8);
                OpenPath(path);
            }
            catch
            {
            }
        }

        private void UpdateVersionTo019()
        {
            if (Content is not DependencyObject root)
                return;

            foreach (TextBlock textBlock in FindDescendants<TextBlock>(root))
            {
                if (textBlock.Text.StartsWith("NEXUS v", StringComparison.OrdinalIgnoreCase))
                {
                    textBlock.Text = "NEXUS v0.1.9";
                    break;
                }
            }
        }
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;

namespace NEXUS
{
    public sealed partial class MainWindow
    {
        private TelemetryHistoryService? _telemetryHistoryService;
        private DispatcherTimer? _telemetryHistoryTimer;
        private TextBlock? _telemetryHistorySummaryText;
        private TextBlock? _telemetryHistoryStatusText;
        private bool _telemetryHistoryInitialized;

        public void InitializeTelemetryHistory()
        {
            if (_telemetryHistoryInitialized)
                return;

            StackPanel? body = GetPageBody(_devicesPage);
            if (body == null)
                return;

            _telemetryHistoryInitialized = true;
            _telemetryHistoryService = new TelemetryHistoryService(NexusDataFolder);

            try
            {
                _telemetryHistoryService.DeleteOlderThan(TimeSpan.FromDays(30));
            }
            catch
            {
            }

            body.Children.Add(BuildTelemetryHistoryCard());

            RecordTelemetrySample();
            RefreshTelemetryHistorySummary();

            _telemetryHistoryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1)
            };
            _telemetryHistoryTimer.Tick += TelemetryHistoryTimer_Tick;
            _telemetryHistoryTimer.Start();

            Closed += TelemetryHistoryWindow_Closed;
            UpdateVersionTo018();
        }

        private Border BuildTelemetryHistoryCard()
        {
            Border card = CreateCard();
            StackPanel body = new() { Spacing = 10 };

            body.Children.Add(new TextBlock
            {
                Text = "24H TELEMETRY HISTORY",
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            });

            body.Children.Add(new TextBlock
            {
                Text = "NEXUS сохраняет один локальный снимок нагрузки и температур в минуту. История хранится 30 дней только на этом компьютере.",
                Foreground = Brush(115, 119, 127),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });

            _telemetryHistorySummaryText = new TextBlock
            {
                Text = "История ещё не накоплена.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 15
            };
            body.Children.Add(_telemetryHistorySummaryText);

            _telemetryHistoryStatusText = new TextBlock
            {
                Text = _telemetryHistoryService == null
                    ? "Telemetry DB: недоступно"
                    : "Telemetry DB: " + _telemetryHistoryService.DatabasePath,
                Foreground = Brush(115, 119, 127),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };
            body.Children.Add(_telemetryHistoryStatusText);

            StackPanel buttons = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10
            };

            Button refresh = new() { Content = "REFRESH HISTORY" };
            refresh.Click += (_, _) => RefreshTelemetryHistorySummary();

            Button sample = new() { Content = "SAVE SNAPSHOT NOW" };
            sample.Click += (_, _) =>
            {
                RecordTelemetrySample();
                RefreshTelemetryHistorySummary();
            };

            Button export = new() { Content = "EXPORT CSV" };
            export.Click += (_, _) => ExportTelemetryHistory();

            buttons.Children.Add(refresh);
            buttons.Children.Add(sample);
            buttons.Children.Add(export);
            body.Children.Add(buttons);

            card.Child = body;
            return card;
        }

        private void TelemetryHistoryTimer_Tick(object? sender, object e)
        {
            RecordTelemetrySample();

            if (_devicesPage?.Visibility == Visibility.Visible)
                RefreshTelemetryHistorySummary();
        }

        private void RecordTelemetrySample()
        {
            if (_telemetryHistoryService == null)
                return;

            try
            {
                TelemetrySample sample = new()
                {
                    Timestamp = DateTime.Now,
                    CpuLoad = CpuProgress.Value,
                    RamUsedPercent = RamProgress.Value,
                    DiskUsedPercent = DiskProgress.Value,
                    GpuLoad = GpuProgress.Value,
                    CpuTemperature = ParseOptionalNumber(CpuTempText.Text),
                    GpuTemperature = ParseOptionalNumber(GpuTempText.Text),
                    StorageTemperature = ParseOptionalNumber(StorageTempText.Text),
                    StorageHealth = ParseOptionalNumber(StorageHealthText.Text),
                    UptimeMinutes = Environment.TickCount64 / 60000d
                };

                _telemetryHistoryService.AddSample(sample);
            }
            catch (Exception ex)
            {
                if (_telemetryHistoryStatusText != null)
                    _telemetryHistoryStatusText.Text = "Ошибка записи telemetry: " + ex.Message;
            }
        }

        private void RefreshTelemetryHistorySummary()
        {
            if (_telemetryHistoryService == null || _telemetryHistorySummaryText == null)
                return;

            try
            {
                TelemetrySummary summary = _telemetryHistoryService.GetSummary(TimeSpan.FromHours(24));

                if (summary.Samples == 0)
                {
                    _telemetryHistorySummaryText.Text = "За последние 24 часа снимков ещё нет.";
                    return;
                }

                string cpuTemp = FormatTelemetryValue(summary.MaxCpuTemperature, "°C");
                string gpuTemp = FormatTelemetryValue(summary.MaxGpuTemperature, "°C");
                string ssdTemp = FormatTelemetryValue(summary.MaxStorageTemperature, "°C");
                string health = FormatTelemetryValue(summary.MinimumStorageHealth, "%");

                _telemetryHistorySummaryText.Text =
                    $"Снимков: {summary.Samples} • период: {summary.FirstSample:HH:mm} — {summary.LastSample:HH:mm}\n" +
                    $"CPU: avg {FormatTelemetryValue(summary.AverageCpu, "%")} • max {FormatTelemetryValue(summary.MaxCpu, "%")} • max temp {cpuTemp}\n" +
                    $"RAM: avg {FormatTelemetryValue(summary.AverageRam, "%")} • max {FormatTelemetryValue(summary.MaxRam, "%")}\n" +
                    $"GPU: max load {FormatTelemetryValue(summary.MaxGpu, "%")} • max temp {gpuTemp}\n" +
                    $"SSD: max temp {ssdTemp} • minimum recorded health {health}";
            }
            catch (Exception ex)
            {
                _telemetryHistorySummaryText.Text = "Не удалось прочитать историю: " + ex.Message;
            }
        }

        private void ExportTelemetryHistory()
        {
            if (_telemetryHistoryService == null)
                return;

            try
            {
                string path = _telemetryHistoryService.ExportCsv(NexusDataFolder);
                OpenPath(path);

                _logService.Write(
                    "System",
                    "TelemetryExported",
                    "TelemetryHistory",
                    "Экспортирована история телеметрии",
                    path);
            }
            catch (Exception ex)
            {
                if (_telemetryHistoryStatusText != null)
                    _telemetryHistoryStatusText.Text = "Ошибка экспорта telemetry: " + ex.Message;
            }
        }

        private void TelemetryHistoryWindow_Closed(object sender, WindowEventArgs args)
        {
            try
            {
                _telemetryHistoryTimer?.Stop();
            }
            catch
            {
            }
        }

        private static string FormatTelemetryValue(double? value, string suffix) =>
            value.HasValue ? $"{value.Value:F1}{suffix}" : "н/д";

        private void UpdateVersionTo018()
        {
            if (Content is not DependencyObject root)
                return;

            foreach (TextBlock textBlock in FindDescendants<TextBlock>(root))
            {
                if (textBlock.Text.StartsWith("NEXUS v", StringComparison.OrdinalIgnoreCase))
                {
                    textBlock.Text = "NEXUS v0.1.8";
                    break;
                }
            }
        }
    }
}

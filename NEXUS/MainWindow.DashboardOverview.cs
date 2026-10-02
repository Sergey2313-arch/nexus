using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace NEXUS
{
    public sealed partial class MainWindow
    {
        private DispatcherTimer? _dashboardOverviewTimer;
        private TextBlock? _dashboardOverviewStatusText;
        private TextBlock? _dashboardOverviewHintText;
        private TextBlock? _dashboardOverviewCpuText;
        private TextBlock? _dashboardOverviewRamText;
        private TextBlock? _dashboardOverviewStorageText;
        private TextBlock? _dashboardOverviewSecurityText;
        private bool _dashboardOverviewInitialized;

        public void InitializeDashboardOverview()
        {
            if (_dashboardOverviewInitialized)
                return;

            _dashboardOverviewInitialized = true;

            if (DashboardView.Content is not StackPanel body)
                return;

            Border card = CreateDashboardOverviewCard();
            body.Children.Insert(Math.Min(1, body.Children.Count), card);

            _dashboardOverviewTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _dashboardOverviewTimer.Tick += (_, _) => RefreshDashboardOverview();
            _dashboardOverviewTimer.Start();

            Closed += (_, _) =>
            {
                try { _dashboardOverviewTimer?.Stop(); } catch { }
            };

            RefreshDashboardOverview();
        }

        private Border CreateDashboardOverviewCard()
        {
            Border card = CreateCard();
            card.Padding = new Thickness(20);

            Grid root = new() { ColumnSpacing = 20 };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.65, GridUnitType.Star) });

            StackPanel summary = new() { Spacing = 6 };
            summary.Children.Add(new TextBlock
            {
                Text = "NEXUS HEALTH",
                Foreground = Brush(115, 119, 127),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            _dashboardOverviewStatusText = new TextBlock
            {
                Text = "--",
                FontSize = 25,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            };
            summary.Children.Add(_dashboardOverviewStatusText);

            _dashboardOverviewHintText = new TextBlock
            {
                Text = "",
                Foreground = Brush(154, 157, 165),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            summary.Children.Add(_dashboardOverviewHintText);
            root.Children.Add(summary);

            Grid metrics = new() { ColumnSpacing = 10 };
            for (int i = 0; i < 4; i++)
                metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _dashboardOverviewCpuText = AddOverviewMetric(metrics, 0, "CPU");
            _dashboardOverviewRamText = AddOverviewMetric(metrics, 1, "RAM");
            _dashboardOverviewStorageText = AddOverviewMetric(metrics, 2, "SSD");
            _dashboardOverviewSecurityText = AddOverviewMetric(metrics, 3, "SECURITY");

            Grid.SetColumn(metrics, 1);
            root.Children.Add(metrics);

            card.Child = root;
            return card;
        }

        private static TextBlock AddOverviewMetric(Grid grid, int column, string title)
        {
            Border tile = new()
            {
                Background = Brush(17, 20, 25),
                BorderBrush = Brush(39, 44, 53),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12)
            };

            StackPanel panel = new() { Spacing = 4 };
            panel.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brush(115, 119, 127),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            TextBlock value = new()
            {
                Text = "--",
                FontSize = 17,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            panel.Children.Add(value);

            tile.Child = panel;
            Grid.SetColumn(tile, column);
            grid.Children.Add(tile);
            return value;
        }

        private void RefreshDashboardOverview()
        {
            if (_dashboardOverviewStatusText == null ||
                _dashboardOverviewHintText == null ||
                _dashboardOverviewCpuText == null ||
                _dashboardOverviewRamText == null ||
                _dashboardOverviewStorageText == null ||
                _dashboardOverviewSecurityText == null)
                return;

            double cpu = CpuProgress.Value;
            double ram = RamProgress.Value;
            double disk = DiskProgress.Value;
            double? cpuTemp = ParseOptionalNumber(CpuTempText.Text);
            double? ssdHealth = ParseOptionalNumber(StorageHealthText.Text);

            int score = 100;
            if (disk >= 95) score -= 25;
            else if (disk >= 90) score -= 15;
            else if (disk >= 80) score -= 5;

            if (ram >= 95) score -= 15;
            else if (ram >= 90) score -= 8;

            if (cpuTemp >= 95) score -= 25;
            else if (cpuTemp >= 88) score -= 15;
            else if (cpuTemp >= 82) score -= 7;

            if (ssdHealth.HasValue && ssdHealth.Value < 70) score -= 20;
            else if (ssdHealth.HasValue && ssdHealth.Value < 85) score -= 8;

            score = Math.Clamp(score, 0, 100);
            bool english = LocalizationService.CurrentLanguage == NexusLanguage.English;

            string state = score switch
            {
                >= 92 => english ? "SYSTEM NORMAL" : "СИСТЕМА В НОРМЕ",
                >= 78 => english ? "ATTENTION" : "ЕСТЬ ЗАМЕЧАНИЯ",
                >= 60 => english ? "CHECK RECOMMENDED" : "РЕКОМЕНДУЕТСЯ ПРОВЕРКА",
                _ => english ? "SERVICE ADVISED" : "РЕКОМЕНДУЕТСЯ ОБСЛУЖИВАНИЕ"
            };

            _dashboardOverviewStatusText.Text = $"{state}  •  {score}/100";
            _dashboardOverviewStatusText.Foreground = score switch
            {
                >= 92 => Brush(103, 209, 122),
                >= 78 => Brush(244, 201, 93),
                >= 60 => Brush(255, 160, 80),
                _ => Brush(255, 107, 107)
            };

            _dashboardOverviewHintText.Text = english
                ? "Quick local score based on current load, temperatures, disk usage and SSD wear."
                : "Быстрая локальная оценка по нагрузке, температурам, заполнению диска и износу SSD.";

            _dashboardOverviewCpuText.Text = cpuTemp.HasValue
                ? $"{cpu:F0}% • {cpuTemp:F0}°C"
                : $"{cpu:F0}%";

            _dashboardOverviewRamText.Text = $"{ram:F0}%";
            _dashboardOverviewStorageText.Text = ssdHealth.HasValue
                ? $"{ssdHealth:F0}% • C: {disk:F0}%"
                : $"C: {disk:F0}%";

            _dashboardOverviewSecurityText.Text = _lastSecurityReport == null
                ? (english ? "NOT SCANNED" : "НЕ ПРОВЕРЕНО")
                : $"{_lastSecurityReport.Score}/100";
        }
    }
}

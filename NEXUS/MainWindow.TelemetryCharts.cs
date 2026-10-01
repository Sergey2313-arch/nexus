using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;

namespace NEXUS
{
    public sealed partial class MainWindow
    {
        private Canvas? _telemetryLoadChart;
        private Canvas? _telemetryTemperatureChart;
        private TextBlock? _telemetryChartsStatusText;
        private bool _telemetryChartsInitialized;

        public void InitializeTelemetryCharts()
        {
            if (_telemetryChartsInitialized || _telemetryHistoryService == null)
                return;

            StackPanel? body = GetPageBody(_devicesPage);
            if (body == null)
                return;

            _telemetryChartsInitialized = true;
            body.Children.Add(BuildTelemetryChartsCard());
            RefreshTelemetryCharts();
            UpdateVersionTo020();
        }

        private Border BuildTelemetryChartsCard()
        {
            Border card = CreateCard();
            StackPanel body = new() { Spacing = 12 };

            body.Children.Add(new TextBlock
            {
                Text = "TELEMETRY CHARTS",
                FontSize = 22,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            });

            body.Children.Add(new TextBlock
            {
                Text = "Графики строятся из локальной telemetry.db. Никакие данные не отправляются наружу.",
                Foreground = Brush(115, 119, 127),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });

            _telemetryChartsStatusText = new TextBlock
            {
                Text = "Подготовка истории...",
                Foreground = Brush(154, 157, 165),
                TextWrapping = TextWrapping.Wrap
            };
            body.Children.Add(_telemetryChartsStatusText);

            body.Children.Add(BuildChartLegend(
                "LOAD • последние 24 часа",
                "CPU",
                "RAM"));

            _telemetryLoadChart = NewTelemetryCanvas();
            _telemetryLoadChart.SizeChanged += (_, _) => RefreshTelemetryCharts();
            body.Children.Add(WrapChart(_telemetryLoadChart));

            body.Children.Add(BuildChartLegend(
                "TEMPERATURE • последние 24 часа",
                "GPU",
                "SSD"));

            _telemetryTemperatureChart = NewTelemetryCanvas();
            _telemetryTemperatureChart.SizeChanged += (_, _) => RefreshTelemetryCharts();
            body.Children.Add(WrapChart(_telemetryTemperatureChart));

            Button refresh = new()
            {
                Content = "REFRESH CHARTS",
                HorizontalAlignment = HorizontalAlignment.Left
            };
            refresh.Click += (_, _) => RefreshTelemetryCharts();
            body.Children.Add(refresh);

            card.Child = body;
            return card;
        }

        private static StackPanel BuildChartLegend(string title, string first, string second)
        {
            StackPanel panel = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 14
            };

            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            panel.Children.Add(new TextBlock
            {
                Text = $"— {first}",
                Foreground = Brush(112, 168, 255)
            });

            panel.Children.Add(new TextBlock
            {
                Text = $"— {second}",
                Foreground = Brush(103, 209, 122)
            });

            return panel;
        }

        private static Canvas NewTelemetryCanvas()
        {
            return new Canvas
            {
                Height = 190,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 420
            };
        }

        private static Border WrapChart(Canvas canvas)
        {
            return new Border
            {
                Background = Brush(15, 17, 21),
                BorderBrush = Brush(43, 46, 53),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8),
                Child = canvas
            };
        }

        private void RefreshTelemetryCharts()
        {
            if (_telemetryHistoryService == null ||
                _telemetryLoadChart == null ||
                _telemetryTemperatureChart == null ||
                _telemetryChartsStatusText == null)
                return;

            try
            {
                List<TelemetrySample> samples = _telemetryHistoryService
                    .GetRecent(1600)
                    .Where(item => item.Timestamp >= DateTime.Now.AddHours(-24))
                    .ToList();

                if (samples.Count < 2)
                {
                    _telemetryChartsStatusText.Text =
                        $"Пока недостаточно точек для графика: {samples.Count}. NEXUS сохраняет по одной точке в минуту.";
                    DrawEmptyChart(_telemetryLoadChart, "Ждём минимум 2 снимка...");
                    DrawEmptyChart(_telemetryTemperatureChart, "Ждём минимум 2 снимка...");
                    return;
                }

                _telemetryChartsStatusText.Text =
                    $"Точек за 24 часа: {samples.Count} • {samples.First().Timestamp:dd.MM HH:mm} — {samples.Last().Timestamp:dd.MM HH:mm}";

                DrawTelemetryChart(
                    _telemetryLoadChart,
                    samples,
                    item => item.CpuLoad,
                    item => item.RamUsedPercent,
                    0,
                    100,
                    "%");

                double maxTemp = samples
                    .SelectMany(item => new[] { item.GpuTemperature, item.StorageTemperature })
                    .Where(value => value.HasValue)
                    .Select(value => value!.Value)
                    .DefaultIfEmpty(100)
                    .Max();

                maxTemp = Math.Max(70, Math.Ceiling(maxTemp / 10d) * 10d);

                DrawTelemetryChart(
                    _telemetryTemperatureChart,
                    samples,
                    item => item.GpuTemperature,
                    item => item.StorageTemperature,
                    0,
                    maxTemp,
                    "°C");
            }
            catch (Exception ex)
            {
                _telemetryChartsStatusText.Text = "Ошибка построения графиков: " + ex.Message;
            }
        }

        private static void DrawTelemetryChart(
            Canvas canvas,
            IReadOnlyList<TelemetrySample> samples,
            Func<TelemetrySample, double?> firstSelector,
            Func<TelemetrySample, double?> secondSelector,
            double minimum,
            double maximum,
            string unit)
        {
            canvas.Children.Clear();

            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            if (width <= 40) width = 760;
            if (height <= 40) height = 190;

            const double left = 42;
            const double right = 10;
            const double top = 10;
            const double bottom = 24;

            double plotWidth = Math.Max(10, width - left - right);
            double plotHeight = Math.Max(10, height - top - bottom);
            double range = Math.Max(1, maximum - minimum);

            for (int i = 0; i <= 4; i++)
            {
                double ratio = i / 4d;
                double y = top + plotHeight * ratio;
                double value = maximum - range * ratio;

                Line gridLine = new()
                {
                    X1 = left,
                    X2 = left + plotWidth,
                    Y1 = y,
                    Y2 = y,
                    Stroke = Brush(43, 46, 53),
                    StrokeThickness = 1
                };
                canvas.Children.Add(gridLine);

                TextBlock label = new()
                {
                    Text = $"{value:F0}{unit}",
                    Foreground = Brush(115, 119, 127),
                    FontSize = 10
                };
                Canvas.SetLeft(label, 0);
                Canvas.SetTop(label, Math.Max(0, y - 8));
                canvas.Children.Add(label);
            }

            Polyline firstLine = new()
            {
                Stroke = Brush(112, 168, 255),
                StrokeThickness = 2
            };

            Polyline secondLine = new()
            {
                Stroke = Brush(103, 209, 122),
                StrokeThickness = 2
            };

            DateTime start = samples.First().Timestamp;
            DateTime end = samples.Last().Timestamp;
            double seconds = Math.Max(1, (end - start).TotalSeconds);

            foreach (TelemetrySample sample in samples)
            {
                double x = left + ((sample.Timestamp - start).TotalSeconds / seconds) * plotWidth;

                double? first = firstSelector(sample);
                if (first.HasValue)
                {
                    double normalized = Math.Clamp((first.Value - minimum) / range, 0, 1);
                    double y = top + (1 - normalized) * plotHeight;
                    firstLine.Points.Add(new Point(x, y));
                }

                double? second = secondSelector(sample);
                if (second.HasValue)
                {
                    double normalized = Math.Clamp((second.Value - minimum) / range, 0, 1);
                    double y = top + (1 - normalized) * plotHeight;
                    secondLine.Points.Add(new Point(x, y));
                }
            }

            if (firstLine.Points.Count > 1)
                canvas.Children.Add(firstLine);
            if (secondLine.Points.Count > 1)
                canvas.Children.Add(secondLine);

            TextBlock from = new()
            {
                Text = start.ToString("HH:mm"),
                Foreground = Brush(115, 119, 127),
                FontSize = 10
            };
            Canvas.SetLeft(from, left);
            Canvas.SetTop(from, top + plotHeight + 4);
            canvas.Children.Add(from);

            TextBlock to = new()
            {
                Text = end.ToString("HH:mm"),
                Foreground = Brush(115, 119, 127),
                FontSize = 10
            };
            Canvas.SetLeft(to, Math.Max(left, left + plotWidth - 36));
            Canvas.SetTop(to, top + plotHeight + 4);
            canvas.Children.Add(to);
        }

        private static void DrawEmptyChart(Canvas canvas, string text)
        {
            canvas.Children.Clear();
            TextBlock message = new()
            {
                Text = text,
                Foreground = Brush(115, 119, 127),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Canvas.SetLeft(message, 20);
            Canvas.SetTop(message, 75);
            canvas.Children.Add(message);
        }

        private void UpdateVersionTo020()
        {
            if (Content is not DependencyObject root)
                return;

            foreach (TextBlock textBlock in FindDescendants<TextBlock>(root))
            {
                if (textBlock.Text.StartsWith("NEXUS v", StringComparison.OrdinalIgnoreCase))
                {
                    textBlock.Text = "NEXUS v0.2.0";
                    break;
                }
            }
        }
    }
}

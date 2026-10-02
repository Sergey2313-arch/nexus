using LibreHardwareMonitor.Hardware;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NEXUS
{
    public sealed partial class MainWindow
    {
        private readonly MotherboardInventoryService _motherboardInventoryService = new();
        private Border? _motherboardCard;
        private TextBlock? _motherboardStatusText;
        private TextBlock? _motherboardSummaryText;
        private StackPanel? _motherboardMemoryPanel;
        private StackPanel? _motherboardDevicesPanel;
        private StackPanel? _motherboardSensorsPanel;
        private Button? _motherboardRefreshButton;
        private DispatcherTimer? _motherboardSensorTimer;
        private CancellationTokenSource? _motherboardCts;
        private MotherboardInventorySnapshot? _lastMotherboardSnapshot;
        private bool _motherboardInitialized;

        public void InitializeMotherboardInspector()
        {
            if (_motherboardInitialized)
                return;

            StackPanel? body = GetPageBody(_devicesPage);
            if (body == null)
                return;

            _motherboardInitialized = true;
            _motherboardCard = BuildMotherboardCard();
            body.Children.Add(_motherboardCard);

            _motherboardSensorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4)
            };
            _motherboardSensorTimer.Tick += (_, _) =>
            {
                if (_devicesPage?.Visibility == Visibility.Visible)
                    RefreshMotherboardSensors();
            };
            _motherboardSensorTimer.Start();

            Closed += (_, _) =>
            {
                try { _motherboardSensorTimer?.Stop(); } catch { }
                try { _motherboardCts?.Cancel(); } catch { }
                try { _motherboardCts?.Dispose(); } catch { }
            };

            _ = RefreshMotherboardInventoryAsync();
        }

        private Border BuildMotherboardCard()
        {
            Border card = CreateCard();
            StackPanel body = new() { Spacing = 14 };

            Grid header = new() { ColumnSpacing = 12 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel title = new() { Spacing = 4 };
            title.Children.Add(new TextBlock
            {
                Text = "МАТЕРИНСКАЯ ПЛАТА И КОМПОНЕНТЫ",
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            });
            title.Children.Add(new TextBlock
            {
                Text = "Плата, BIOS/UEFI, модули памяти, системные устройства, контроллеры и доступные аппаратные датчики.",
                Foreground = Brush(154, 157, 165),
                TextWrapping = TextWrapping.Wrap
            });

            _motherboardRefreshButton = new Button
            {
                Content = "ОБНОВИТЬ КОМПОНЕНТЫ",
                Padding = new Thickness(14, 8, 14, 8),
                VerticalAlignment = VerticalAlignment.Center
            };
            _motherboardRefreshButton.Click += async (_, _) => await RefreshMotherboardInventoryAsync();
            Grid.SetColumn(_motherboardRefreshButton, 1);

            header.Children.Add(title);
            header.Children.Add(_motherboardRefreshButton);
            body.Children.Add(header);

            _motherboardStatusText = new TextBlock
            {
                Text = "Подготовка инвентаризации оборудования...",
                Foreground = Brush(154, 157, 165),
                TextWrapping = TextWrapping.Wrap
            };
            body.Children.Add(_motherboardStatusText);

            _motherboardSummaryText = new TextBlock
            {
                Text = "Данные ещё не получены.",
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas")
            };

            Expander boardExpander = NewMotherboardExpander("Плата и BIOS / UEFI", _motherboardSummaryText, true);
            body.Children.Add(boardExpander);

            _motherboardMemoryPanel = new StackPanel { Spacing = 8 };
            body.Children.Add(NewMotherboardExpander("Оперативная память по слотам", _motherboardMemoryPanel, true));

            _motherboardSensorsPanel = new StackPanel { Spacing = 7 };
            body.Children.Add(NewMotherboardExpander("Аппаратные датчики", _motherboardSensorsPanel, true));

            _motherboardDevicesPanel = new StackPanel { Spacing = 10 };
            body.Children.Add(NewMotherboardExpander("Все обнаруженные компоненты Windows", _motherboardDevicesPanel, false));

            body.Children.Add(new TextBlock
            {
                Text = "Примечание: Windows и прошивка ноутбука могут не раскрывать отдельные VRM, фазы питания, MOSFET, EC и другие физические микросхемы. NEXUS показывает все компоненты и датчики, которые доступны через Windows и LibreHardwareMonitor.",
                Foreground = Brush(115, 119, 127),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });

            card.Child = body;
            return card;
        }

        private static Expander NewMotherboardExpander(string header, UIElement content, bool expanded)
        {
            return new Expander
            {
                Header = new TextBlock
                {
                    Text = header,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                },
                Content = content,
                IsExpanded = expanded
            };
        }

        private async Task RefreshMotherboardInventoryAsync()
        {
            if (_motherboardStatusText == null)
                return;

            _motherboardCts?.Cancel();
            _motherboardCts?.Dispose();
            _motherboardCts = new CancellationTokenSource();

            if (_motherboardRefreshButton != null)
                _motherboardRefreshButton.IsEnabled = false;

            _motherboardStatusText.Text = "Сканирование материнской платы и устройств Windows...";

            try
            {
                MotherboardInventorySnapshot snapshot =
                    await _motherboardInventoryService.CaptureAsync(_motherboardCts.Token);

                _lastMotherboardSnapshot = snapshot;
                RenderMotherboardInventory(snapshot);
                RefreshMotherboardSensors();

                int problems = snapshot.Devices.Count(item => item.HasProblem);
                _motherboardStatusText.Text =
                    $"Найдено устройств: {snapshot.Devices.Count} • проблем: {problems} • обновлено {snapshot.CapturedAt:HH:mm:ss}";

                _logService.Write(
                    "Hardware",
                    "MotherboardInventory",
                    "Devices",
                    "Инвентаризация компонентов платы завершена",
                    $"Устройств: {snapshot.Devices.Count}; проблем: {problems}; RAM-модулей: {snapshot.MemoryModules.Count}");
            }
            catch (OperationCanceledException)
            {
                _motherboardStatusText.Text = "Сканирование отменено.";
            }
            catch (Exception ex)
            {
                _motherboardStatusText.Text = "Не удалось получить компоненты платы: " + ex.Message;
            }
            finally
            {
                if (_motherboardRefreshButton != null)
                    _motherboardRefreshButton.IsEnabled = true;
            }
        }

        private void RenderMotherboardInventory(MotherboardInventorySnapshot snapshot)
        {
            if (_motherboardSummaryText != null)
            {
                MotherboardInfo? board = snapshot.Board;
                BiosInfo? bios = snapshot.Bios;

                _motherboardSummaryText.Text =
                    $"Производитель платы: {ValueOrUnknown(board?.Manufacturer)}\n" +
                    $"Модель платы: {ValueOrUnknown(board?.Product)}\n" +
                    $"Версия платы: {ValueOrUnknown(board?.Version)}\n" +
                    $"Серийный номер платы: {ValueOrUnknown(board?.SerialNumber)}\n\n" +
                    $"Производитель BIOS/UEFI: {ValueOrUnknown(bios?.Manufacturer)}\n" +
                    $"Версия BIOS/UEFI: {ValueOrUnknown(bios?.Version)}\n" +
                    $"Дата BIOS/UEFI: {ValueOrUnknown(bios?.ReleaseDate)}";
            }

            RenderMemoryModules(snapshot.MemoryModules);
            RenderBoardDevices(snapshot.Devices, snapshot.Warnings);
        }

        private void RenderMemoryModules(IReadOnlyList<MemoryModuleInfo> modules)
        {
            if (_motherboardMemoryPanel == null)
                return;

            _motherboardMemoryPanel.Children.Clear();

            if (modules.Count == 0)
            {
                _motherboardMemoryPanel.Children.Add(NewMutedText("Сведения о слотах памяти недоступны."));
                return;
            }

            foreach (MemoryModuleInfo module in modules)
            {
                string capacity = module.CapacityBytes > 0
                    ? $"{module.CapacityBytes / 1024d / 1024d / 1024d:F0} ГБ"
                    : "неизвестно";

                _motherboardMemoryPanel.Children.Add(new TextBlock
                {
                    Text =
                        $"{ValueOrUnknown(module.Bank)} • {capacity} • {module.SpeedMhz} МГц\n" +
                        $"{ValueOrUnknown(module.Manufacturer)} • {ValueOrUnknown(module.PartNumber)}",
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }

        private void RenderBoardDevices(IReadOnlyList<BoardDeviceInfo> devices, IReadOnlyList<string> warnings)
        {
            if (_motherboardDevicesPanel == null)
                return;

            _motherboardDevicesPanel.Children.Clear();

            int problems = devices.Count(item => item.HasProblem);
            _motherboardDevicesPanel.Children.Add(new TextBlock
            {
                Text = $"Всего: {devices.Count} • без ошибок: {Math.Max(0, devices.Count - problems)} • требуют внимания: {problems}",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            foreach (IGrouping<string, BoardDeviceInfo> group in devices
                .OrderByDescending(item => item.HasProblem)
                .ThenBy(item => item.Category)
                .ThenBy(item => item.Name)
                .GroupBy(item => string.IsNullOrWhiteSpace(item.Category) ? "Прочие устройства" : item.Category)
                .OrderBy(group => group.Key))
            {
                StackPanel groupPanel = new() { Spacing = 5 };
                int total = group.Count();
                int groupProblems = group.Count(item => item.HasProblem);

                groupPanel.Children.Add(new TextBlock
                {
                    Text = $"{group.Key} — {total}" + (groupProblems > 0 ? $" • ⚠ {groupProblems}" : string.Empty),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                });

                foreach (BoardDeviceInfo device in group
                    .OrderByDescending(item => item.HasProblem)
                    .ThenBy(item => item.Name)
                    .Take(45))
                {
                    string status = device.HasProblem
                        ? $"⚠ код {device.ErrorCode}"
                        : "✓ исправно";

                    groupPanel.Children.Add(new TextBlock
                    {
                        Text = $"{status}  {device.Name}" +
                               (string.IsNullOrWhiteSpace(device.Manufacturer) ? string.Empty : $" • {device.Manufacturer}"),
                        Foreground = device.HasProblem ? Brush(240, 183, 77) : Brush(194, 198, 207),
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                if (total > 45)
                    groupPanel.Children.Add(NewMutedText($"Показано 45 из {total} устройств этой категории."));

                _motherboardDevicesPanel.Children.Add(new Expander
                {
                    Header = new TextBlock { Text = group.Key },
                    Content = groupPanel,
                    IsExpanded = groupProblems > 0
                });
            }

            foreach (string warning in warnings)
                _motherboardDevicesPanel.Children.Add(NewMutedText("ℹ " + warning));
        }

        private void RefreshMotherboardSensors()
        {
            if (_motherboardSensorsPanel == null)
                return;

            List<(string Hardware, string Sensor, SensorType Type, float Value)> sensors = new();

            try
            {
                foreach (IHardware hardware in _hardwareMonitor.Hardware)
                    CollectHardwareSensors(hardware, sensors);
            }
            catch
            {
                return;
            }

            _motherboardSensorsPanel.Children.Clear();

            var usefulSensors = sensors
                .Where(item => IsUsefulBoardSensor(item.Type, item.Value))
                .OrderBy(item => SensorOrder(item.Type))
                .ThenBy(item => item.Hardware)
                .ThenBy(item => item.Sensor)
                .Take(120)
                .ToList();

            if (usefulSensors.Count == 0)
            {
                _motherboardSensorsPanel.Children.Add(NewMutedText("Доступные датчики платы не обнаружены."));
                return;
            }

            foreach (var sensor in usefulSensors)
            {
                _motherboardSensorsPanel.Children.Add(new TextBlock
                {
                    Text = $"{SensorTypeRu(sensor.Type)} • {sensor.Hardware} • {sensor.Sensor}: {FormatSensorValue(sensor.Type, sensor.Value)}",
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }

        private static void CollectHardwareSensors(
            IHardware hardware,
            ICollection<(string Hardware, string Sensor, SensorType Type, float Value)> result)
        {
            try { hardware.Update(); } catch { }

            foreach (ISensor sensor in hardware.Sensors)
            {
                if (!sensor.Value.HasValue)
                    continue;

                float value = sensor.Value.Value;
                if (float.IsNaN(value) || float.IsInfinity(value))
                    continue;

                result.Add((hardware.Name, sensor.Name, sensor.SensorType, value));
            }

            foreach (IHardware subHardware in hardware.SubHardware)
                CollectHardwareSensors(subHardware, result);
        }

        private static bool IsUsefulBoardSensor(SensorType type, float value)
        {
            return type switch
            {
                SensorType.Temperature => value > 0.5f,
                SensorType.Voltage => value >= 0,
                SensorType.Fan => value >= 0,
                SensorType.Power => value >= 0,
                SensorType.Load => value >= 0,
                SensorType.Clock => value >= 0,
                _ => false
            };
        }

        private static int SensorOrder(SensorType type)
        {
            return type switch
            {
                SensorType.Temperature => 0,
                SensorType.Fan => 1,
                SensorType.Voltage => 2,
                SensorType.Power => 3,
                SensorType.Load => 4,
                SensorType.Clock => 5,
                _ => 9
            };
        }

        private static string SensorTypeRu(SensorType type)
        {
            return type switch
            {
                SensorType.Temperature => "Температура",
                SensorType.Voltage => "Напряжение",
                SensorType.Fan => "Вентилятор",
                SensorType.Power => "Мощность",
                SensorType.Load => "Нагрузка",
                SensorType.Clock => "Частота",
                _ => type.ToString()
            };
        }

        private static string FormatSensorValue(SensorType type, float value)
        {
            return type switch
            {
                SensorType.Temperature => $"{value:F1} °C",
                SensorType.Voltage => $"{value:F3} В",
                SensorType.Fan => $"{value:F0} об/мин",
                SensorType.Power => $"{value:F1} Вт",
                SensorType.Load => $"{value:F1}%",
                SensorType.Clock => $"{value:F0} МГц",
                _ => value.ToString("F1")
            };
        }

        private static TextBlock NewMutedText(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brush(115, 119, 127),
                TextWrapping = TextWrapping.Wrap
            };
        }

        private static string ValueOrUnknown(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "неизвестно" : value.Trim();
        }
    }
}

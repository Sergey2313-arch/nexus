using LibreHardwareMonitor.Hardware;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NEXUS.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;

namespace NEXUS;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, LiveSensor> _sensorRows = new();
    private readonly List<ComponentItem> _components = new();
    private readonly Queue<double> _cpuHistory = new(), _gpuHistory = new(), _storageHistory = new();
    private readonly CancellationTokenSource _controlsLifetime = new();
    private bool _inventoryLoading, _inventoryLoaded;
    private TempPreview? _tempPreview;
    private readonly string _tempRoot = Path.GetTempPath();

    public sealed class LiveSensor : INotifyPropertyChanged
    {
        public string Device { get; init; } = "";
        public string Name { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Unit { get; init; } = "";
        private string _valueText = "—", _range = "";
        private double? _min, _max;
        public string ValueText => _valueText;
        public string Range => _range;
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Update(double? value)
        {
            if (value.HasValue && double.IsFinite(value.Value))
            {
                _min = !_min.HasValue ? value : Math.Min(_min.Value, value.Value);
                _max = !_max.HasValue ? value : Math.Max(_max.Value, value.Value);
                _valueText = $"{value:F1} {Unit}";
                _range = $"С начала сеанса: min {_min:F1} / max {_max:F1} {Unit}";
            }
            else { _valueText = "Недоступен"; _range = "Нет текущего показания"; }
            PropertyChanged?.Invoke(this, new(nameof(ValueText)));
            PropertyChanged?.Invoke(this, new(nameof(Range)));
        }
    }

    private void ComponentsButton_Click(object sender, RoutedEventArgs e) { NavigateTo("components"); _ = LoadComponentsAsync(false); }
    private void SensorsButton_Click(object sender, RoutedEventArgs e) => NavigateTo("sensors");
    private void TemperaturesButton_Click(object sender, RoutedEventArgs e) => NavigateTo("temperatures");
    private void MaintenanceButton_Click(object sender, RoutedEventArgs e) => NavigateTo("maintenance");
    private void RefreshComponentsButton_Click(object sender, RoutedEventArgs e) => _ = LoadComponentsAsync(true);

    private async Task LoadComponentsAsync(bool force)
    {
        if (_inventoryLoading || (_inventoryLoaded && !force)) return;
        _inventoryLoading = true;
        ComponentsStatusText.Text = "Собираем модели и характеристики…";
        try
        {
            var items = await ComponentInventoryService.ReadAsync(_controlsLifetime.Token);
            if (_securityWindowClosed) return;
            _components.Clear(); _components.AddRange(items);
            _inventoryLoaded = true;
            ComponentsStatusText.Text = $"Компонентов: {items.Count} • обновлено {DateTime.Now:HH:mm:ss}";
            RefreshComponents();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (_securityWindowClosed) return;
            _components.Clear();
            foreach (var hardware in _hardwareMonitor.Hardware) _components.Add(new(hardware.HardwareType.ToString() switch { "Cpu" => "CPU", "GpuAmd" or "GpuNvidia" or "GpuIntel" => "GPU", "Memory" => "RAM", "Motherboard" => "Board", _ => hardware.HardwareType.ToString() }, hardware.Name, "Обнаружен LibreHardwareMonitor; подробная инвентаризация недоступна"));
            ComponentsStatusText.Text = "Частичные данные: " + ex.Message;
            RefreshComponents();
        }
        finally { _inventoryLoading = false; }
    }

    private void ComponentFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (ComponentsList != null) RefreshComponents(); }
    private void RefreshComponents()
    {
        string[] kinds = { "", "CPU", "GPU", "RAM", "Board", "Storage", "BIOS" };
        int selected = Math.Clamp(ComponentFilterComboBox.SelectedIndex, 0, 6);
        ComponentsList.ItemsSource = _components.Where(c => selected == 0 || c.Kind == kinds[selected]).ToList();
    }
    private void DiagramComponent_Click(object sender, RoutedEventArgs e)
    {
        string kind = ((Button)sender).Tag?.ToString() ?? "";
        ComponentFilterComboBox.SelectedIndex = kind switch { "CPU" => 1, "GPU" => 2, "RAM" => 3, "Storage" => 5, _ => 0 };
        NavigateTo("components"); _ = LoadComponentsAsync(false);
    }

    private void SensorSearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (LiveSensorsList != null) RefreshSensors(); }
    private void RefreshSensors()
    {
        string query = SensorSearchBox.Text ?? "";
        LiveSensorsList.ItemsSource = _sensorRows.Values.Where(s => (s.Device + " " + s.Name + " " + s.Kind).Contains(query, StringComparison.OrdinalIgnoreCase)).OrderBy(s => s.Device).ThenBy(s => s.Kind).ThenBy(s => s.Name).ToList();
    }
    private static string SensorUnit(SensorType type) => type.ToString() switch
    {
        "Temperature" => "°C", "Load" or "Level" or "Control" => "%", "Clock" => "MHz", "Fan" => "RPM", "Voltage" => "V", "Current" => "A", "Power" => "W", "Data" => "GB", "SmallData" => "MB", "Throughput" => "B/s", "Frequency" => "Hz", "Energy" => "mWh", _ => ""
    };
    private void CollectSensors(IHardware hardware, HashSet<string> seen)
    {
        foreach (var sensor in hardware.Sensors)
        {
            string key = sensor.Identifier.ToString(); seen.Add(key);
            if (!_sensorRows.TryGetValue(key, out var row))
            {
                row = new() { Device = hardware.Name, Name = sensor.Name, Kind = sensor.SensorType.ToString(), Unit = SensorUnit(sensor.SensorType) };
                _sensorRows.Add(key, row);
            }
            row.Update(SensorReadingPolicy.CurrentValue(sensor.SensorType.ToString(), sensor.Value));
        }
        foreach (var child in hardware.SubHardware) CollectSensors(child, seen);
    }
    private void UpdateControlCenter()
    {
        int count = _sensorRows.Count;
        var seen = new HashSet<string>();
        foreach (var hardware in _hardwareMonitor.Hardware) CollectSensors(hardware, seen);
        foreach (var row in _sensorRows.Where(pair => !seen.Contains(pair.Key))) row.Value.Update(null);
        if (count != _sensorRows.Count) RefreshSensors();
        SensorsStatusText.Text = $"Датчиков: {seen.Count} • обновление раз в секунду • {DateTime.Now:HH:mm:ss}";
        var readings = CaptureHardwareReadings();
        double? Highest(string metric) { var values = readings.Where(r => r.Metric == metric).Select(r => r.Value).ToList(); return values.Count > 0 ? values.Max() : null; }
        double? cpu = Highest("CPU Temperature"), gpu = Highest("GPU Temperature"), storage = Highest("Storage Temperature"), ram = Highest("RAM Load");
        string Temp(double? value) => value.HasValue ? $"{value:F1} °C" : "Датчик недоступен";
        DiagramCpuText.Text = ThermalCpuText.Text = Temp(cpu);
        var gpuSource = readings.Where(r => r.Metric == "GPU Temperature").OrderByDescending(r => r.Value).FirstOrDefault();
        var gpuAuxiliary = readings.Where(r => r.Metric == "GPU Auxiliary Temperature").OrderByDescending(r => r.Value).FirstOrDefault();
        string gpuText = gpuSource != null ? Temp(gpu) + " • " + gpuSource.Source : gpuAuxiliary != null ? $"{gpuAuxiliary.Value:F1} °C • {gpuAuxiliary.Source} (не ядро GPU)" : "Датчик ядра GPU недоступен";
        DiagramGpuText.Text = ThermalGpuText.Text = gpuText;
        GpuTempText.Text = gpuText;
        DiagramStorageText.Text = ThermalStorageText.Text = Temp(storage);
        DiagramRamText.Text = ram.HasValue ? $"{ram:F0} %" : "Нет данных";
        MaintenanceRamText.Text = RamDetailsText.Text;
        PlotTemperature(_cpuHistory, cpu, CpuTemperatureLine);
        PlotTemperature(_gpuHistory, gpu ?? gpuAuxiliary?.Value, GpuTemperatureLine);
        PlotTemperature(_storageHistory, storage, StorageTemperatureLine);
        bool hot = (cpu >= 80) || (gpu >= 85) || (storage >= 60);
        ThermalStatusText.Text = hot ? "Повышенная температура: проверьте нагрузку, вентиляцию и охлаждение." : "Показания доступны только для обнаруженных датчиков. Показан источник температуры. GPU VR/SoC не заменяет датчик ядра GPU.";
        ThermalStatusText.Foreground = hot ? Brush(0xFF,0xC8,0x57) : Brush(0xA7,0xB5,0xC8);
    }
    private static void PlotTemperature(Queue<double> history, double? value, Microsoft.UI.Xaml.Shapes.Polyline line)
    {
        if (!value.HasValue) { history.Clear(); line.Points = new PointCollection(); return; }
        history.Enqueue(value.Value); while (history.Count > 60) history.Dequeue();
        var points = new PointCollection(); int index = 0;
        foreach (double sample in history) points.Add(new Point(index++ * 600.0 / 59, 87 - Math.Clamp(sample, 0, 110) / 110 * 86));
        line.Points = points;
    }

    private void SetMemoryActionStatus(string message)
    {
        MemoryActionStatusText.Text = MaintenanceStatusText.Text = message;
    }

    private async void TrimMemoryButton_Click(object sender, RoutedEventArgs e)
    {
        TrimMemoryButton.IsEnabled = false;
        SetMemoryActionStatus("Освобождаем память NEXUS…");
        try
        {
            var result = await RunTrackedActionAsync("Освободить память NEXUS", () => Task.Run(MaintenanceService.TrimOwnMemory), r => $"Рабочий набор: {r.Item1 / 1048576.0:F1} → {r.Item2 / 1048576.0:F1} MB. Память может снова потребоваться.");
            if (_securityWindowClosed) return;
            SetMemoryActionStatus($"Рабочий набор NEXUS: {result.Before / 1048576.0:F1} → {result.After / 1048576.0:F1} MB. Память может снова потребоваться приложению.");
            _logService.Write("System", "MemoryTrim", "Maintenance", "Уменьшен рабочий набор NEXUS", MaintenanceStatusText.Text);
        }
        catch (Exception ex) { if (!_securityWindowClosed) SetMemoryActionStatus(ex.Message); }
        finally { if (!_securityWindowClosed) TrimMemoryButton.IsEnabled = true; }
    }
    private async void RefreshMemoryProcessesButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshMemoryProcessesButton.IsEnabled = false;
        SetMemoryActionStatus("Получаем список приложений…");
        try
        {
            var processes = await Task.Run(MaintenanceService.ListMemoryProcesses);
            if (_securityWindowClosed) return;
            MemoryProcessComboBox.ItemsSource = processes;
            MemoryProcessComboBox.SelectedIndex = processes.Count > 0 ? 0 : -1;
            TrimSelectedMemoryButton.IsEnabled = processes.Count > 0;
            SetMemoryActionStatus($"Доступно приложений: {processes.Count}. Выберите одно для освобождения RAM.");
        }
        catch (Exception ex) { if (!_securityWindowClosed) SetMemoryActionStatus(ex.Message); }
        finally { if (!_securityWindowClosed) RefreshMemoryProcessesButton.IsEnabled = true; }
    }

    private async void TrimSelectedMemoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (MemoryProcessComboBox.SelectedItem is not MemoryProcess candidate) { SetMemoryActionStatus("Сначала выберите приложение из списка."); return; }
        TrimSelectedMemoryButton.IsEnabled = false;
        SetMemoryActionStatus("Освобождаем память выбранного приложения…");
        try
        {
            var result = await RunTrackedActionAsync("Освободить память: " + candidate.Name, () => Task.Run(() => MaintenanceService.TrimSelectedMemory(candidate)), r => $"PID {candidate.Id}, рабочий набор: {r.Item1 / 1048576.0:F1} → {r.Item2 / 1048576.0:F1} MB. Приложение не закрывалось.");
            if (_securityWindowClosed) return;
            SetMemoryActionStatus($"{candidate.Name}: рабочий набор {result.Before / 1048576.0:F1} → {result.After / 1048576.0:F1} MB. Приложение не закрывалось; память может снова потребоваться.");
            _logService.Write("System", "MemoryTrim", "Maintenance", "Уменьшен рабочий набор выбранного приложения", MaintenanceStatusText.Text, processId: candidate.Id);
            MemoryProcessComboBox.ItemsSource = null;
        }
        catch (Exception ex) { if (!_securityWindowClosed) SetMemoryActionStatus("Не удалось освободить память: " + ex.Message); }
        finally { if (!_securityWindowClosed) TrimSelectedMemoryButton.IsEnabled = MemoryProcessComboBox.SelectedItem is MemoryProcess; }
    }

    private async void AnalyzeTempButton_Click(object sender, RoutedEventArgs e)
    {
        AnalyzeTempButton.IsEnabled = CleanTempButton.IsEnabled = false; _tempPreview = null;
        TempPreviewText.Text = "Анализируем временные файлы…";
        try
        {
            var preview = await Task.Run(() => MaintenanceService.PreviewTemp(_tempRoot));
            if (_securityWindowClosed) return;
            _tempPreview = preview;
            TempPreviewText.Text = $"{preview.Files.Count} файлов • {preview.Bytes / 1048576.0:F1} MB" + (preview.Limited ? " • выборка ограничена" : "");
            TempFilesText.Text = string.Join("\n", preview.Files.Take(100).Select(f => f.Path));
            MaintenanceStatusText.Text = $"Пропущено недоступных объектов: {preview.Skipped}. Ничего не удалено.";
            CleanTempButton.IsEnabled = preview.Files.Count > 0;
        }
        catch (Exception ex) { if (!_securityWindowClosed) TempPreviewText.Text = "Ошибка анализа: " + ex.Message; }
        finally { if (!_securityWindowClosed) AnalyzeTempButton.IsEnabled = true; }
    }
    private async void CleanTempButton_Click(object sender, RoutedEventArgs e)
    {
        var preview = _tempPreview; if (preview == null) return;
        AnalyzeTempButton.IsEnabled = CleanTempButton.IsEnabled = false;
        try
        {
            var dialog = new ContentDialog { XamlRoot = ShellRoot.XamlRoot, RequestedTheme = ElementTheme.Dark, Title = "Удалить временные файлы?", Content = $"{preview.Files.Count} файлов старше 7 дней в {_tempRoot}\nДо {preview.Bytes / 1048576.0:F1} MB. Удаление не использует корзину; изменённые и занятые файлы пропускаются.", PrimaryButtonText = "Удалить", CloseButtonText = "Отмена", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) { RecordRemediationCancellation("Очистка пользовательского Temp"); return; }
            var result = await RunTrackedActionAsync("Очистка пользовательского Temp", () => Task.Run(() => MaintenanceService.CleanTemp(_tempRoot, preview)), r => $"Удалено: {r.Deleted}, пропущено: {r.Skipped}, освобождено: {r.Bytes / 1048576.0:F1} MB.", r => r.Skipped > 0 ? "Partial" : "Completed");
            if (_securityWindowClosed) return;
            MaintenanceStatusText.Text = $"Удалено: {result.Deleted}. Пропущено: {result.Skipped}. Освобождено: {result.Bytes / 1048576.0:F1} MB.";
            _logService.Write("System", "TempCleanup", "Maintenance", "Очистка пользовательского Temp", MaintenanceStatusText.Text);
            _tempPreview = null; TempFilesText.Text = ""; TempPreviewText.Text = "Для повторной очистки выполните анализ";
        }
        catch (Exception ex) { if (!_securityWindowClosed) MaintenanceStatusText.Text = "Ошибка очистки: " + ex.Message; }
        finally { if (!_securityWindowClosed) { AnalyzeTempButton.IsEnabled = true; CleanTempButton.IsEnabled = _tempPreview?.Files.Count > 0; } }
    }
    private async void WindowsStorageButton_Click(object sender, RoutedEventArgs e)
    {
        try { await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:storagesense")); }
        catch (Exception ex) { MaintenanceStatusText.Text = ex.Message; }
    }
}

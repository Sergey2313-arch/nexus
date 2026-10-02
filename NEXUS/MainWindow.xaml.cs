using LibreHardwareMonitor.Hardware;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Windows.ApplicationModel.DataTransfer;

namespace NEXUS
{
    public sealed partial class MainWindow : Window
    {
        private readonly DispatcherTimer _monitorTimer;
        private readonly Computer _hardwareMonitor;
        private readonly LogService _logService;
        private readonly SystemMonitorService _systemMonitor;
        private readonly DispatcherTimer _logbookTimer;

        private readonly ObservableCollection<RunningProcessItem> _runningProcesses = new();
        private readonly ObservableCollection<LogbookItem> _logbookItems = new();

        private bool _isLogbookVisible;


        private ulong _previousIdle;
        private ulong _previousKernel;
        private ulong _previousUser;

        public MainWindow()
        {
            InitializeComponent();
            InitializeDiagnosticsView();

            _logService =
            new LogService();

            _systemMonitor =
            new SystemMonitorService(
            _logService);

            _systemMonitor.Start();

            // ============================================
            // LOGBOOK UI
            // ============================================

            RunningProcessesList.ItemsSource = _processGroups;
            LogbookList.ItemsSource = _logbookItems;

            CategoryFilterComboBox.SelectedIndex = 0;
            CategoryFilterComboBox.SelectionChanged +=
                CategoryFilterComboBox_SelectionChanged;

            LogbookSearchBox.TextChanged +=
                LogbookSearchBox_TextChanged;

            _logbookTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };

            _logbookTimer.Tick += LogbookTimer_Tick;
            _logbookTimer.Start();

            // Отслеживаем буфер обмена, пока NEXUS запущен.
            Clipboard.ContentChanged += Clipboard_ContentChanged;

            // ============================================
            // HARDWARE MONITOR
            // ============================================

            _hardwareMonitor = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = true,
                IsControllerEnabled = true
            };

            _hardwareMonitor.Open();

            // Сохраняем полный список датчиков на рабочий стол.
            DumpAllSensors();

            // ============================================
            // ОСНОВНАЯ ИНФОРМАЦИЯ
            // ============================================

            DeviceNameText.Text = Environment.MachineName;
            CpuNameText.Text = GetCpuName();
            OsNameText.Text = GetWindowsName();

            LastEventText.Text = "NEXUS запущен";
            LastEventTimeText.Text =
                $"{DateTime.Now:HH:mm:ss} • System";

            // Первое обновление.
            UpdateSystemInfo();

            // ============================================
            // ТАЙМЕР
            // ============================================

            _monitorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            _monitorTimer.Tick += MonitorTimer_Tick;
            _monitorTimer.Start();

            Closed += MainWindow_Closed;
        }

        // ============================================
        // ЗАКРЫТИЕ
        // ============================================

        private void MainWindow_Closed(
        object sender,
        WindowEventArgs args)
        {
            _securityWindowClosed = true;
            _controlsLifetime.Cancel();
            _securityScanCancellation?.Cancel();
            _monitorTimer.Stop();
            _logbookTimer.Stop();

            Clipboard.ContentChanged -= Clipboard_ContentChanged;

            _systemMonitor.Stop();

            _hardwareMonitor.Close();
        }

        // ============================================
        // ОБНОВЛЕНИЕ
        // ============================================

        private void MonitorTimer_Tick(
            object? sender,
            object e)
        {
            UpdateSystemInfo();
        }

        private void UpdateSystemInfo()
        {
            UpdateCpu();
            UpdateMemory();
            UpdateDisk();
            UpdateUptime();

            UpdateHardwareSensors();
            UpdateStorageHealth();
            UpdateControlCenter();
        }

        // ============================================
        // LOGBOOK NAVIGATION
        // ============================================

        private void DashboardButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            NavigateTo("overview");
        }

        private void LogbookButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            NavigateTo("logbook");

            RefreshLogbook();
        }

        private void RefreshLogbookButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            RefreshLogbook();
        }

        private void LogbookTimer_Tick(
            object? sender,
            object e)
        {
            if (!_isLogbookVisible)
                return;

            if (!LiveToggle.IsOn)
                return;

            RefreshLogbook();
        }

        private void CategoryFilterComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (_isLogbookVisible)
            {
                RefreshLogbookHistory();
            }
        }

        private void LogbookSearchBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (_isLogbookVisible)
            {
                RefreshLogbookHistory();
            }
        }

        private void RefreshLogbook()
        {
            RefreshRunningProcesses();
            RefreshLogbookHistory();
        }

        // ============================================
        // RUNNING PROCESSES
        // ============================================

        private void RefreshRunningProcesses()
        {
            try
            {
                var selected = RunningProcessesList.SelectedItem as RunningProcessItem;
                var processes = Process.GetProcesses().Select(ReadProcess).OfType<RunningProcessItem>().ToList();
                var ids = processes.Select(p => p.ProcessId).ToHashSet();
                foreach (int id in _processCpuSamples.Keys.Where(id => !ids.Contains(id)).ToArray()) _processCpuSamples.Remove(id);
                string search = ProcessSearchBox?.Text ?? "";
                var filtered = processes.Where(p => (p.Name + " " + p.ProcessId).Contains(search, StringComparison.OrdinalIgnoreCase));
                var sorted = (ProcessSortComboBox?.SelectedIndex ?? 0) switch
                {
                    1 => filtered.OrderByDescending(p => p.CpuPercent),
                    2 => filtered.OrderBy(p => p.Name),
                    3 => filtered.OrderBy(p => p.ProcessId),
                    _ => filtered.OrderByDescending(p => p.MemoryBytes)
                };
                _runningProcesses.Clear();
                foreach (var process in sorted) _runningProcesses.Add(process);
                RefreshProcessGroups();
                RunningProcessesCountText.Text += $" • всего процессов {processes.Count}";
            }
            catch (Exception ex) { RunningProcessesCountText.Text = "Не удалось получить процессы: " + ex.Message; }
        }

        // ============================================
        // LOGBOOK HISTORY
        // ============================================

        private void RefreshLogbookHistory()
        {
            try
            {
                string? category =
                    CategoryFilterComboBox.SelectedIndex switch
                    {
                        1 => "Application",
                        2 => "Process",
                        3 => "File",
                        4 => "Clipboard",
                        5 => "System",
                        6 => "Security",
                        7 => "Action",
                        _ => null
                    };

                string search =
                    LogbookSearchBox.Text?
                    .Trim()
                    .ToLowerInvariant()
                    ?? "";

                var events =
                    _logService
                    .GetLatest(750, category)
                    .Where(item =>
                    {
                        if (string.IsNullOrWhiteSpace(search))
                            return true;

                        string haystack =
                            (
                                $"{item.Category} " +
                                $"{item.EventType} " +
                                $"{item.Source} " +
                                $"{item.Title} " +
                                $"{item.Details} " +
                                $"{item.FilePath} " +
                                $"{item.ProcessId}"
                            )
                            .ToLowerInvariant();

                        return haystack.Contains(search);
                    })
                    .Take(500)
                    .ToList();

                var expanded = _logbookItems.Where(i => i.IsExpanded).Select(i => i.Id).ToHashSet();
                _logbookItems.Clear();

                foreach (LogEvent item
                         in events)
                {
                    string details =
                        item.Details ?? "";

                    if (item.ProcessId.HasValue)
                    {
                        string pid =
                            $"PID {item.ProcessId.Value}";

                        details =
                            string.IsNullOrWhiteSpace(details)
                                ? pid
                                : $"{details} • {pid}";
                    }

                    _logbookItems.Add(
                        new LogbookItem
                        {
                            Id = item.Id,
                            IsExpanded = expanded.Contains(item.Id),
                            Icon = EventIcon(item.Category),
                            Accent = EventAccent(item.Severity),
                            Summary = $"{item.Timestamp:dd.MM.yyyy HH:mm:ss} • {GetCategoryDisplayName(item.Category)} • {(item.Category == "Action" ? "NEXUS" : item.Source)} • {item.Severity}",
                            Time =
                                item.Timestamp.Date ==
                                DateTime.Today
                                    ? item.Timestamp.ToString("HH:mm:ss")
                                    : item.Timestamp.ToString("dd.MM HH:mm"),
                            Category =
                                GetCategoryDisplayName(
                                    item.Category),
                            Source =
                                item.Source ?? "",
                            Title =
                                item.Title ?? "",
                            Details =
                                details,
                            FilePath =
                                item.FilePath ?? ""
                        });
                }

                foreach (var entry in _logbookItems) _ = LoadEventIconAsync(entry);

                LogbookCountText.Text =
                    $"{_logbookItems.Count} событий";
            }
            catch (Exception ex)
            {
                _logbookItems.Clear();

                _logbookItems.Add(
                    new LogbookItem
                    {
                        Time = DateTime.Now.ToString("HH:mm:ss"),
                        Category = "SYSTEM",
                        Source = "NEXUS",
                        Title = "Не удалось прочитать журнал",
                        Details = ex.Message
                    });

                LogbookCountText.Text =
                    "Ошибка чтения";
            }
        }

        private static string GetCategoryDisplayName(
            string category)
        {
            return category switch
            {
                "Application" => "ПРОГРАММА",
                "Process" => "ПРОЦЕСС",
                "File" => "ФАЙЛ",
                "Clipboard" => "БУФЕР",
                "System" => "СИСТЕМА",
                "Hardware" => "ЖЕЛЕЗО",
                "Action" => "ИСПРАВЛЕНИЯ",
                "Security" => "БЕЗОПАСНОСТЬ",
                _ => category.ToUpperInvariant()
            };
        }

        private static string FormatBytes(
            long bytes)
        {
            if (bytes < 1024L * 1024L)
            {
                return $"{bytes / 1024d:F0} KB";
            }

            if (bytes < 1024L * 1024L * 1024L)
            {
                return
                    $"{bytes / 1024d / 1024d:F0} MB";
            }

            return
                $"{bytes / 1024d / 1024d / 1024d:F1} GB";
        }

        // ============================================
        // CLIPBOARD LOGGING
        // ============================================

        private async void Clipboard_ContentChanged(
            object sender,
            object e)
        {
            try
            {
                DataPackageView package =
                    Clipboard.GetContent();

                if (package.Contains(
                    StandardDataFormats.Text))
                {
                    string text =
                        await package.GetTextAsync();

                    if (string.IsNullOrWhiteSpace(text))
                        return;

                    string preview =
                        BuildSafeClipboardPreview(text);

                    _logService.Write(
                        "Clipboard",
                        "Copied",
                        "Clipboard",
                        "Скопирован текст",
                        preview);

                    return;
                }

                if (package.Contains(
                    StandardDataFormats.Bitmap))
                {
                    _logService.Write(
                        "Clipboard",
                        "Copied",
                        "Clipboard",
                        "Скопировано изображение",
                        "Содержимое изображения не сохраняется.");

                    return;
                }

                if (package.Contains(
                    StandardDataFormats.StorageItems))
                {
                    var items =
                        await package.GetStorageItemsAsync();

                    string names =
                        string.Join(
                            ", ",
                            items
                            .Take(15)
                            .Select(item => item.Name));

                    if (items.Count > 15)
                    {
                        names +=
                            $" … (+{items.Count - 15})";
                    }

                    _logService.Write(
                        "Clipboard",
                        "Copied",
                        "Clipboard",
                        $"Скопированы файлы: {items.Count}",
                        names);
                }
            }
            catch
            {
                // Буфер обмена не должен ломать NEXUS.
            }
        }

        private static string BuildSafeClipboardPreview(
            string value)
        {
            string text =
                value
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();

            if (LooksSensitive(text))
            {
                return
                    "[СОДЕРЖИМОЕ СКРЫТО: похоже на пароль, токен или секрет]";
            }

            const int maxLength = 300;

            if (text.Length > maxLength)
            {
                return
                    text.Substring(0, maxLength)
                    + "…";
            }

            return text;
        }

        private static bool LooksSensitive(
            string value)
        {
            string lower =
                value.ToLowerInvariant();

            string[] markers =
            {
                "password",
                "passwd",
                "пароль",
                "api_key",
                "apikey",
                "api-key",
                "access_token",
                "refresh_token",
                "bearer ",
                "authorization:",
                "client_secret",
                "secret=",
                "-----begin private key",
                "-----begin rsa private key"
            };

            if (markers.Any(
                marker => lower.Contains(marker)))
            {
                return true;
            }

            // JWT-подобная строка.
            if (value.Length > 80 &&
                value.Count(c => c == '.') == 2 &&
                !value.Contains(' '))
            {
                return true;
            }

            return false;
        }

        // ============================================
        // LOGBOOK VIEW MODELS
        // ============================================

        public sealed class RunningProcessItem
        {
            public string State { get; set; } = "Работает";
            public DateTime StartedUtc { get; set; }
            public string Path { get; set; } = "";
            public double CpuPercent { get; set; }
            public string Cpu { get; set; } = "—";
            public string Name { get; set; } = "";
            public int ProcessId { get; set; }
            public long MemoryBytes { get; set; }
            public string Memory { get; set; } = "";
            public string Details { get; set; } = "";
        }

        public sealed class LogbookItem : System.ComponentModel.INotifyPropertyChanged
        {
            private Microsoft.UI.Xaml.Media.ImageSource? _applicationIcon;
            public Microsoft.UI.Xaml.Media.ImageSource? ApplicationIcon
            {
                get => _applicationIcon;
                set { _applicationIcon = value; PropertyChanged?.Invoke(this, new(nameof(ApplicationIcon))); PropertyChanged?.Invoke(this, new(nameof(FallbackIconVisibility))); }
            }
            public Visibility FallbackIconVisibility => ApplicationIcon == null ? Visibility.Visible : Visibility.Collapsed;
            public bool HasPath => !string.IsNullOrWhiteSpace(FilePath);
            public string PathDisplay => HasPath ? FilePath : "Путь не сохранён или недоступен. Для старого события восстановить его только по PID нельзя.";
            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
            public long Id { get; set; }
            public bool IsExpanded { get; set; }
            public string Icon { get; set; } = "\uE713";
            public Microsoft.UI.Xaml.Media.SolidColorBrush Accent { get; set; } = EventAccent("Info");
            public string Summary { get; set; } = "";
            public string Time { get; set; } = "";
            public string Category { get; set; } = "";
            public string Source { get; set; } = "";
            public string Title { get; set; } = "";
            public string Details { get; set; } = "";
            public string FilePath { get; set; } = "";
        }

        // ============================================
        // CPU LOAD
        // ============================================

        private void UpdateCpu()
        {
            if (!GetSystemTimes(
                    out FILETIME idleTime,
                    out FILETIME kernelTime,
                    out FILETIME userTime))
            {
                return;
            }

            ulong idle =
                FileTimeToUInt64(idleTime);

            ulong kernel =
                FileTimeToUInt64(kernelTime);

            ulong user =
                FileTimeToUInt64(userTime);

            if (_previousKernel != 0)
            {
                ulong idleDelta =
                    idle - _previousIdle;

                ulong kernelDelta =
                    kernel - _previousKernel;

                ulong userDelta =
                    user - _previousUser;

                ulong total =
                    kernelDelta + userDelta;

                if (total > 0)
                {
                    double cpu =
                        (total - idleDelta)
                        * 100.0
                        / total;

                    cpu =
                        Math.Clamp(
                            cpu,
                            0,
                            100);

                    CpuValueText.Text =
                        $"{cpu:F0}%";

                    CpuProgress.Value =
                        cpu;
                }
            }

            _previousIdle = idle;
            _previousKernel = kernel;
            _previousUser = user;
        }

        // ============================================
        // RAM
        // ============================================

        private void UpdateMemory()
        {
            MEMORYSTATUSEX memory =
                new MEMORYSTATUSEX
                {
                    dwLength =
                        (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
                };

            if (!GlobalMemoryStatusEx(ref memory))
                return;

            double totalGb =
                memory.ullTotalPhys
                / 1024d
                / 1024d
                / 1024d;

            double availableGb =
                memory.ullAvailPhys
                / 1024d
                / 1024d
                / 1024d;

            double usedGb =
                totalGb - availableGb;

            double percent =
                usedGb
                / totalGb
                * 100.0;

            RamValueText.Text =
                $"{usedGb:F1} GB";

            RamDetailsText.Text =
                $"{usedGb:F1} / {totalGb:F1} GB";

            RamProgress.Value =
                percent;
        }

        // ============================================
        // DISK C:
        // ============================================

        private void UpdateDisk()
        {
            try
            {
                DriveInfo disk =
                    new DriveInfo(@"C:\");

                if (!disk.IsReady)
                    return;

                double totalGb =
                    disk.TotalSize
                    / 1024d
                    / 1024d
                    / 1024d;

                double freeGb =
                    disk.AvailableFreeSpace
                    / 1024d
                    / 1024d
                    / 1024d;

                double usedGb =
                    totalGb - freeGb;

                double percent =
                    usedGb
                    / totalGb
                    * 100.0;

                DiskValueText.Text =
                    $"{percent:F0}%";

                DiskDetailsText.Text =
                    $"{usedGb:F0} / {totalGb:F0} GB";

                DiskProgress.Value =
                    percent;

                if (percent >= 90)
                {
                    DiskStatusText.Text =
                        "⚠ Критично: мало свободного места";
                }
                else if (percent >= 80)
                {
                    DiskStatusText.Text =
                        "⚠ Внимание: диск почти заполнен";
                }
                else
                {
                    DiskStatusText.Text =
                        "✓ Состояние диска нормальное";
                }
            }
            catch
            {
                DiskStatusText.Text =
                    "Не удалось получить данные диска";
            }
        }

        // ============================================
        // UPTIME
        // ============================================

        private void UpdateUptime()
        {
            TimeSpan uptime =
                TimeSpan.FromMilliseconds(
                    Environment.TickCount64);

            if (uptime.Days > 0)
            {
                UptimeText.Text =
                    $"{uptime.Days}д " +
                    $"{uptime:hh\\:mm\\:ss}";
            }
            else
            {
                UptimeText.Text =
                    uptime.ToString(
                        @"hh\:mm\:ss");
            }
        }

        // ============================================
        // CPU / GPU ДАТЧИКИ
        // ============================================

        private void UpdateHardwareSensors()
        {
            float? cpuTemperature = null;

            float? gpuTemperature = null;
            float? gpuLoad = null;

            string gpuName =
                "GPU не обнаружен";

            foreach (IHardware hardware
                     in _hardwareMonitor.Hardware)
            {
                ReadHardwareRecursive(
                    hardware,
                    ref cpuTemperature,
                    ref gpuTemperature,
                    ref gpuLoad,
                    ref gpuName);
            }

            // ========================================
            // CPU TEMP
            // ========================================

            if (cpuTemperature.HasValue &&
                cpuTemperature.Value > 1)
            {
                double temp =
                    cpuTemperature.Value;

                CpuTempText.Text =
                    $"{temp:F0} °C";

                CpuTempProgress.Value =
                    Math.Clamp(
                        temp,
                        0,
                        100);

                if (temp >= 90)
                {
                    CpuTempStatusText.Text =
                        "⚠ Высокая температура";
                }
                else if (temp >= 80)
                {
                    CpuTempStatusText.Text =
                        "⚠ Повышенная температура";
                }
                else
                {
                    CpuTempStatusText.Text =
                        "✓ Температура нормальная";
                }
            }
            else
            {
                CpuTempText.Text =
                    "-- °C";

                CpuTempProgress.Value = 0;

                CpuTempStatusText.Text =
                    "Датчик CPU недоступен";
            }

            // ========================================
            // GPU
            // ========================================

            GpuNameText.Text =
                gpuName;

            if (gpuLoad.HasValue)
            {
                double load =
                    Math.Clamp(
                        gpuLoad.Value,
                        0,
                        100);

                GpuLoadText.Text =
                    $"{load:F0}%";

                GpuProgress.Value =
                    load;
            }
            else
            {
                GpuLoadText.Text =
                    "--%";

                GpuProgress.Value = 0;
            }

            if (gpuTemperature.HasValue &&
                gpuTemperature.Value > 1)
            {
                GpuTempText.Text =
                    $"Температура: " +
                    $"{gpuTemperature.Value:F0} °C";
            }
            else
            {
                GpuTempText.Text =
                    "Температура: недоступна";
            }
        }

        private void ReadHardwareRecursive(
            IHardware hardware,
            ref float? cpuTemperature,
            ref float? gpuTemperature,
            ref float? gpuLoad,
            ref string gpuName)
        {
            hardware.Update();

            bool isGpu =
                hardware.HardwareType ==
                    HardwareType.GpuAmd
                ||
                hardware.HardwareType ==
                    HardwareType.GpuNvidia
                ||
                hardware.HardwareType ==
                    HardwareType.GpuIntel;

            foreach (ISensor sensor
                     in hardware.Sensors)
            {
                if (!sensor.Value.HasValue)
                    continue;

                float value =
                    sensor.Value.Value;

                string sensorName =
                    sensor.Name.ToLower();

                // ------------------------------------
                // CPU TEMP
                // ------------------------------------

                if (hardware.HardwareType ==
                        HardwareType.Cpu
                    &&
                    sensor.SensorType ==
                        SensorType.Temperature
                    &&
                    value > 1)
                {
                    if (!cpuTemperature.HasValue ||
                        value > cpuTemperature.Value)
                    {
                        cpuTemperature =
                            value;
                    }
                }

                // Некоторые ноутбуки отдают CPU temp
                // через motherboard / EC.
                if (!isGpu
                    &&
                    sensor.SensorType ==
                        SensorType.Temperature
                    &&
                    value > 1
                    &&
                    (
                        sensorName.Contains("cpu")
                        ||
                        sensorName.Contains("tctl")
                        ||
                        sensorName.Contains("tdie")
                        ||
                        sensorName.Contains("package")
                    ))
                {
                    if (!cpuTemperature.HasValue ||
                        value > cpuTemperature.Value)
                    {
                        cpuTemperature =
                            value;
                    }
                }
            }

            // ----------------------------------------
            // GPU
            // ----------------------------------------

            if (isGpu)
            {
                gpuName =
                    hardware.Name;

                foreach (ISensor sensor
                         in hardware.Sensors)
                {
                    if (!sensor.Value.HasValue)
                        continue;

                    float value =
                        sensor.Value.Value;

                    if (sensor.SensorType ==
                            SensorType.Temperature
                        &&
                        value > 1)
                    {
                        if (!gpuTemperature.HasValue ||
                            value >
                            gpuTemperature.Value)
                        {
                            gpuTemperature =
                                value;
                        }
                    }

                    if (sensor.SensorType ==
                        SensorType.Load)
                    {
                        string name =
                            sensor.Name.ToLower();

                        if (name.Contains("core")
                            ||
                            name.Contains("gpu")
                            ||
                            name.Contains("3d"))
                        {
                            if (!gpuLoad.HasValue ||
                                value >
                                gpuLoad.Value)
                            {
                                gpuLoad =
                                    value;
                            }
                        }
                    }
                }
            }

            // ----------------------------------------
            // SUB HARDWARE
            // ----------------------------------------

            foreach (IHardware subHardware
                     in hardware.SubHardware)
            {
                ReadHardwareRecursive(
                    subHardware,
                    ref cpuTemperature,
                    ref gpuTemperature,
                    ref gpuLoad,
                    ref gpuName);
            }
        }

        // ============================================
        // SSD HEALTH
        // ============================================

        private void UpdateStorageHealth()
        {
            string? driveName = null;

            float? temperature = null;

            float? health = null;
            float? lifeUsed = null;

            float? powerOnHours = null;
            float? powerOnCount = null;

            float? usedSpace = null;

            float? freeSpace = null;
            float? totalSpace = null;

            float? dataRead = null;
            float? dataWritten = null;

            foreach (IHardware hardware
                     in _hardwareMonitor.Hardware)
            {
                if (hardware.HardwareType !=
                    HardwareType.Storage)
                {
                    continue;
                }

                hardware.Update();

                driveName =
                    hardware.Name;

                foreach (ISensor sensor
                         in hardware.Sensors)
                {
                    if (!sensor.Value.HasValue)
                        continue;

                    float value =
                        sensor.Value.Value;

                    string name =
                        sensor.Name.ToLower();

                    // --------------------------------
                    // TEMPERATURE
                    // --------------------------------

                    if (sensor.SensorType ==
                        SensorType.Temperature)
                    {
                        if (name.Contains("composite"))
                        {
                            temperature =
                                value;
                        }
                        else if (!temperature.HasValue)
                        {
                            temperature =
                                value;
                        }
                    }

                    // --------------------------------
                    // HEALTH / LIFE
                    // --------------------------------

                    if (sensor.SensorType ==
                        SensorType.Level)
                    {
                        if (name == "life"
                            ||
                            name.Contains("life"))
                        {
                            health =
                                value;
                        }

                        if (name.Contains(
                            "percentage used"))
                        {
                            lifeUsed =
                                value;
                        }
                    }

                    // --------------------------------
                    // HOURS / STARTS
                    // --------------------------------

                    if (sensor.SensorType ==
                        SensorType.Factor)
                    {
                        if (name.Contains(
                            "power on hours"))
                        {
                            powerOnHours =
                                value;
                        }

                        if (name.Contains(
                            "power on count"))
                        {
                            powerOnCount =
                                value;
                        }
                    }

                    // --------------------------------
                    // USED SPACE
                    // --------------------------------

                    if (sensor.SensorType ==
                        SensorType.Load
                        &&
                        name.Contains("used space"))
                    {
                        usedSpace =
                            value;
                    }

                    // --------------------------------
                    // DATA
                    // --------------------------------

                    if (sensor.SensorType ==
                        SensorType.Data)
                    {
                        if (name.Contains(
                            "free space"))
                        {
                            freeSpace =
                                value;
                        }

                        if (name.Contains(
                            "total space"))
                        {
                            totalSpace =
                                value;
                        }

                        if (name.Contains(
                            "data read"))
                        {
                            dataRead =
                                value;
                        }

                        if (name.Contains(
                            "data written"))
                        {
                            dataWritten =
                                value;
                        }
                    }
                }

                // Пока показываем первый физический SSD.
                break;
            }

            // ========================================
            // SSD НЕ НАЙДЕН
            // ========================================

            if (driveName == null)
            {
                StorageNameText.Text =
                    "Накопитель не обнаружен";

                StorageStatusText.Text =
                    "Недоступно";

                StorageTempText.Text =
                    "-- °C";

                StorageHealthText.Text =
                    "--%";

                StorageLifeUsedText.Text =
                    "--%";

                StoragePowerOnText.Text =
                    "-- ч";

                StoragePowerCyclesText.Text =
                    "--";

                StorageSpaceText.Text =
                    "-- / -- GB";

                StorageIoText.Text =
                    "SMART данные недоступны";

                return;
            }

            // ========================================
            // FALLBACK HEALTH
            // ========================================

            if (!health.HasValue &&
                lifeUsed.HasValue)
            {
                health =
                    100 - lifeUsed.Value;
            }

            if (!lifeUsed.HasValue &&
                health.HasValue)
            {
                lifeUsed =
                    100 - health.Value;
            }

            // ========================================
            // UI
            // ========================================

            StorageNameText.Text =
                driveName;

            StorageTempText.Text =
                temperature.HasValue
                    ? $"{temperature.Value:F0} °C"
                    : "-- °C";

            StorageHealthText.Text =
                health.HasValue
                    ? $"{health.Value:F0}%"
                    : "--%";

            StorageLifeUsedText.Text =
                lifeUsed.HasValue
                    ? $"{lifeUsed.Value:F0}%"
                    : "--%";

            StoragePowerOnText.Text =
                powerOnHours.HasValue
                    ? $"{powerOnHours.Value:F0} ч"
                    : "-- ч";

            StoragePowerCyclesText.Text =
                powerOnCount.HasValue
                    ? $"{powerOnCount.Value:F0}"
                    : "--";

            if (freeSpace.HasValue &&
                totalSpace.HasValue)
            {
                StorageSpaceText.Text =
                    $"{freeSpace.Value:F0} / " +
                    $"{totalSpace.Value:F0} GB";
            }
            else
            {
                StorageSpaceText.Text =
                    "-- / -- GB";
            }

            string readText =
                dataRead.HasValue
                    ? $"{dataRead.Value:F0} GB"
                    : "--";

            string writtenText =
                dataWritten.HasValue
                    ? $"{dataWritten.Value:F0} GB"
                    : "--";

            StorageIoText.Text =
                $"Прочитано: {readText}   •   " +
                $"Записано: {writtenText}";

            // ========================================
            // ОБЩИЙ СТАТУС SSD
            // ========================================

            bool critical =
                (temperature.HasValue &&
                 temperature.Value >= 70)
                ||
                (health.HasValue &&
                 health.Value <= 40);

            bool warning =
                (temperature.HasValue &&
                 temperature.Value >= 60)
                ||
                (health.HasValue &&
                 health.Value <= 70)
                ||
                (usedSpace.HasValue &&
                 usedSpace.Value >= 90);

            if (critical)
            {
                StorageStatusText.Text =
                    "⚠ КРИТИЧНО";
            }
            else if (warning)
            {
                StorageStatusText.Text =
                    "⚠ ВНИМАНИЕ";
            }
            else if (usedSpace.HasValue &&
                     usedSpace.Value >= 80)
            {
                StorageStatusText.Text =
                    "✓ SSD исправен • мало места";
            }
            else
            {
                StorageStatusText.Text =
                    "✓ HEALTHY";
            }
        }

        // ============================================
        // SENSOR DUMP
        // ============================================

        private void DumpAllSensors()
        {
            try
            {
                StringBuilder result =
                    new StringBuilder();

                result.AppendLine(
                    "========== NEXUS SENSOR SCAN ==========");

                result.AppendLine(
                    $"PC: {Environment.MachineName}");

                result.AppendLine(
                    $"Time: {DateTime.Now}");

                result.AppendLine();

                foreach (IHardware hardware
                         in _hardwareMonitor.Hardware)
                {
                    DumpHardwareRecursive(
                        hardware,
                        result);
                }

                result.AppendLine();

                result.AppendLine(
                    "========== END SENSOR SCAN ==========");

                string desktop =
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.DesktopDirectory);

                string path =
                    Path.Combine(
                        desktop,
                        "NEXUS-Sensors.txt");

                File.WriteAllText(
                    path,
                    result.ToString(),
                    Encoding.UTF8);
            }
            catch
            {
                // На работу NEXUS ошибка дампа
                // влиять не должна.
            }
        }

        private void DumpHardwareRecursive(
            IHardware hardware,
            StringBuilder result)
        {
            hardware.Update();

            result.AppendLine(
                $"HARDWARE: {hardware.Name}");

            result.AppendLine(
                $"TYPE: {hardware.HardwareType}");

            foreach (ISensor sensor
                     in hardware.Sensors)
            {
                string value =
                    sensor.Value.HasValue
                        ? sensor.Value.Value
                            .ToString("F2")
                        : "NULL";

                result.AppendLine(
                    $"{sensor.SensorType} | " +
                    $"{sensor.Name} | " +
                    $"{value}");
            }

            result.AppendLine();

            foreach (IHardware subHardware
                     in hardware.SubHardware)
            {
                DumpHardwareRecursive(
                    subHardware,
                    result);
            }
        }

        // ============================================
        // CPU NAME
        // ============================================

        private string GetCpuName()
        {
            try
            {
                using RegistryKey? key =
                    Registry.LocalMachine.OpenSubKey(
                        @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");

                string? cpuName =
                    key?
                    .GetValue(
                        "ProcessorNameString")?
                    .ToString();

                if (string.IsNullOrWhiteSpace(
                    cpuName))
                {
                    return "Unknown CPU";
                }

                return cpuName.Trim();
            }
            catch
            {
                return "Unknown CPU";
            }
        }

        // ============================================
        // WINDOWS
        // ============================================

        private string GetWindowsName()
        {
            try
            {
                using RegistryKey? key =
                    Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");

                string product =
                    key?
                    .GetValue("ProductName")?
                    .ToString()
                    ?? "Windows";

                string version =
                    key?
                    .GetValue("DisplayVersion")?
                    .ToString()
                    ?? "";

                string buildText =
                    key?
                    .GetValue(
                        "CurrentBuildNumber")?
                    .ToString()
                    ?? "0";

                int.TryParse(
                    buildText,
                    out int build);

                if (build >= 22000)
                {
                    product =
                        "Windows 11";
                }

                if (!string.IsNullOrWhiteSpace(
                    version))
                {
                    return
                        $"{product} • {version}";
                }

                return product;
            }
            catch
            {
                return "Windows";
            }
        }

        // ============================================
        // WINDOWS API
        // ============================================

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(
            out FILETIME lpIdleTime,
            out FILETIME lpKernelTime,
            out FILETIME lpUserTime);

        [DllImport(
            "kernel32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool
            GlobalMemoryStatusEx(
                ref MEMORYSTATUSEX lpBuffer);

        private static ulong FileTimeToUInt64(
            FILETIME time)
        {
            return
                ((ulong)time.dwHighDateTime << 32)
                |
                time.dwLowDateTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;

            public ulong ullTotalPhys;
            public ulong ullAvailPhys;

            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;

            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;

            public ulong ullAvailExtendedVirtual;
        }
    }
}
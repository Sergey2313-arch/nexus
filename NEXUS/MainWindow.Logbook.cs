using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;

namespace NEXUS;

public sealed partial class MainWindow
{
    private readonly Dictionary<int, (DateTime Started, double Cpu, long Tick)> _processCpuSamples = new();
    private bool _processActionBusy;
    private void EventHistoryNavButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo("events"); ShowEventHistoryButton_Click(sender, e); RefreshLogbook();
    }
    private void ShowEventHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        EventHistoryPanel.Visibility = LogFiltersPanel.Visibility = Visibility.Visible;
        ProcessManagerPanel.Visibility = Visibility.Collapsed;
    }
    private void ShowProcessesButton_Click(object sender, RoutedEventArgs e)
    {
        EventHistoryPanel.Visibility = LogFiltersPanel.Visibility = Visibility.Collapsed;
        ProcessManagerPanel.Visibility = Visibility.Visible;
        RefreshRunningProcesses();
    }
    private void ProcessFilterChanged(object sender, TextChangedEventArgs e) { if (RunningProcessesList != null) RefreshRunningProcesses(); }
    private void ProcessSortChanged(object sender, SelectionChangedEventArgs e) { if (RunningProcessesList != null) RefreshRunningProcesses(); }

    private RunningProcessItem? ReadProcess(Process process)
    {
        try
        {
            var item = new RunningProcessItem { Name = process.ProcessName, ProcessId = process.Id, MemoryBytes = process.WorkingSet64, Details = process.MainWindowTitle ?? "" };
            item.Memory = FormatBytes(item.MemoryBytes);
            try { if (process.MainWindowHandle != IntPtr.Zero && !process.Responding) item.State="Нет ответа"; } catch { }
            try
            {
                item.StartedUtc = process.StartTime.ToUniversalTime();
                item.Path = NEXUS.Services.ProcessPathReader.Read(process.Id);
                double cpu = process.TotalProcessorTime.TotalSeconds; long tick = Stopwatch.GetTimestamp();
                if (_processCpuSamples.TryGetValue(item.ProcessId, out var previous) && previous.Started == item.StartedUtc)
                {
                    double elapsed = (tick - previous.Tick) / (double)Stopwatch.Frequency;
                    if (elapsed > 0.01)
                    {
                        item.CpuPercent = Math.Clamp((cpu - previous.Cpu) / elapsed / Environment.ProcessorCount * 100, 0, 100);
                        item.Cpu = $"{item.CpuPercent:F1}%";
                    }
                }
                _processCpuSamples[item.ProcessId] = (item.StartedUtc, cpu, tick);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            return item;
        }
        catch { return null; }
        finally { process.Dispose(); }
    }

    private static string EventIcon(string category) => category switch { "File" => "\uE8A5", "Process" or "Application" => "\uE7C4", "Clipboard" => "\uE77F", "Security" => "\uEA18", _ => "\uE713" };
    private static SolidColorBrush EventAccent(string severity) => severity == "Critical" ? Brush(0xFF,0x75,0x75) : severity == "Warning" ? Brush(0xFF,0xC8,0x57) : Brush(0x69,0xD4,0xD0);
    private void CopyEventButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (((Button)sender).Tag is not LogbookItem item) return;
            var data = new DataPackage(); data.SetText(item.Summary + "\n" + item.Title + "\n" + item.Details + "\n" + item.FilePath); Clipboard.SetContent(data);
        }
        catch (Exception ex) { LogbookCountText.Text = "Не удалось скопировать: " + ex.Message; }
    }
    private void OpenEventLocationButton_Click(object sender, RoutedEventArgs e) => OpenLocation(((Button)sender).Tag?.ToString() ?? "");
    private void OpenSelectedProcessLocationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedManagedProcess is RunningProcessItem item) OpenLocation(item.Path);
        else ProcessActionStatusText.Text = "Выберите процесс.";
    }
    private void OpenLocation(string path)
    {
        try
        {
            var location = NEXUS.Services.LocalLocationService.Resolve(path);
            Process.Start(NEXUS.Services.LocalLocationService.ExplorerStart(location))?.Dispose();
            ProcessActionStatusText.Text = LogbookCountText.Text = location.SelectedFile != null ? "Открыто расположение файла: " + location.Folder : "Открыта папка: " + location.Folder;
        }
        catch (Exception ex) { ProcessActionStatusText.Text = LogbookCountText.Text = "Расположение не открыто: " + ex.Message; }
    }
    private async void EndSelectedProcessButton_Click(object sender, RoutedEventArgs e)
    {
        if (_processActionBusy) return;
        if (_selectedManagedProcess is not RunningProcessItem item) { ProcessActionStatusText.Text = "Выберите процесс."; return; }
        _processActionBusy = true;
        try
        {
            var dialog = new ContentDialog { XamlRoot = ShellRoot.XamlRoot, RequestedTheme = ElementTheme.Dark, Title = "Завершить задачу?", Content = $"{item.Name} • PID {item.ProcessId}\nНесохранённые данные приложения могут быть потеряны.", PrimaryButtonText = "Завершить", CloseButtonText = "Отмена", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            using var process = Process.GetProcessById(item.ProcessId);
            using var current = Process.GetCurrentProcess();
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = process.MainModule?.FileName ?? "";
            if (process.Id == current.Id || process.SessionId != current.SessionId || item.StartedUtc == default || process.StartTime.ToUniversalTime() != item.StartedUtc || string.IsNullOrEmpty(path) || path.StartsWith(windows, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Системный, защищённый или изменившийся процесс завершить нельзя.");
            process.Kill(entireProcessTree: false);
            ProcessActionStatusText.Text = $"Запрос завершения {item.Name} (PID {item.ProcessId}) отправлен.";
            _logService.Write("Process", "UserTerminate", "ProcessManager", "Пользователь завершил задачу", item.Name, processId: item.ProcessId);
            RefreshRunningProcesses();
        }
        catch (Exception ex) { if (!_securityWindowClosed) ProcessActionStatusText.Text = "Задача не завершена: " + ex.Message; }
        finally { _processActionBusy = false; }
    }
}

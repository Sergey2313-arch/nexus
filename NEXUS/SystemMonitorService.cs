using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace NEXUS
{
    public sealed class SystemMonitorService
    {
        private readonly LogService _logService;

        private sealed record KnownProcess(string Name, string Path, DateTime StartedUtc);
        private readonly Dictionary<int, KnownProcess> _knownProcesses = new();
        private readonly object _processGate = new();

        private readonly List<FileSystemWatcher>
            _fileWatchers = new();

        private Timer? _processTimer;
        private Timer? _windowTimer;

        private string _lastWindowTitle = "";
        private int _lastWindowProcessId;

        public SystemMonitorService(
            LogService logService)
        {
            _logService =
                logService;
        }

        public void Start()
        {
            CaptureExistingProcesses();

            _processTimer =
                new Timer(
                    CheckProcesses,
                    null,
                    1000,
                    2000);

            _windowTimer =
                new Timer(
                    CheckForegroundWindow,
                    null,
                    1000,
                    1000);

            StartFileMonitoring();

            _logService.Write(
                "System",
                "Started",
                "NEXUS",
                "Бортовой журнал запущен");
        }

        public void Stop()
        {
            _processTimer?.Dispose();
            _windowTimer?.Dispose();

            foreach (FileSystemWatcher watcher
                     in _fileWatchers)
            {
                watcher.Dispose();
            }

            _fileWatchers.Clear();

            _logService.Write(
                "System",
                "Stopped",
                "NEXUS",
                "Бортовой журнал остановлен");
        }

        // ============================================
        // PROCESS MONITOR
        // ============================================

        private void CaptureExistingProcesses()
        {
            foreach (Process process
                     in Process.GetProcesses())
            {
                try
                {
                    _knownProcesses[
                        process.Id] =
                        ReadKnownProcess(process);
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static KnownProcess ReadKnownProcess(Process process)
        {
            DateTime started = default;
            try { started = process.StartTime.ToUniversalTime(); } catch { }
            return new(process.ProcessName, Services.ProcessPathReader.Read(process.Id), started);
        }

        private void CheckProcesses(
            object? state)
        {
            lock (_processGate)
            {
            try
            {
                Process[] processes =
                    Process.GetProcesses();

                Dictionary<int, KnownProcess> current =
                    new();

                foreach (Process process
                         in processes)
                {
                    try
                    {
                        current[
                            process.Id] =
                            ReadKnownProcess(process);
                    }
                    catch
                    {
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                // Новые процессы.
                foreach (var process
                         in current)
                {
                    if (_knownProcesses.TryGetValue(process.Key, out var previous) && previous.StartedUtc == process.Value.StartedUtc)
                    {
                        continue;
                    }

                    _logService.Write(
                        "Process",
                        "Started",
                        process.Value.Name,
                        $"Запущен процесс {process.Value.Name}",
                        "",
                        process.Key, process.Value.Path);
                }

                // Завершённые процессы.
                foreach (var process
                         in _knownProcesses.ToArray())
                {
                    if (current.TryGetValue(process.Key, out var active) && active.StartedUtc == process.Value.StartedUtc)
                    {
                        continue;
                    }

                    _logService.Write(
                        "Process",
                        "Stopped",
                        process.Value.Name,
                        $"Процесс {process.Value.Name} завершён",
                        "",
                        process.Key, process.Value.Path);
                }

                _knownProcesses.Clear();

                foreach (var process
                         in current)
                {
                    _knownProcesses[
                        process.Key] =
                        process.Value;
                }
            }
            catch
            {
            }
            }
        }

        // ============================================
        // ACTIVE WINDOW
        // ============================================

        private void CheckForegroundWindow(
            object? state)
        {
            try
            {
                IntPtr handle =
                    GetForegroundWindow();

                if (handle ==
                    IntPtr.Zero)
                {
                    return;
                }

                StringBuilder title =
                    new StringBuilder(1024);

                GetWindowText(
                    handle,
                    title,
                    title.Capacity);

                GetWindowThreadProcessId(
                    handle,
                    out uint processId);

                string windowTitle =
                    title.ToString();

                if (string.IsNullOrWhiteSpace(
                    windowTitle))
                {
                    return;
                }

                if (_lastWindowTitle ==
                        windowTitle
                    &&
                    _lastWindowProcessId ==
                        processId)
                {
                    return;
                }

                _lastWindowTitle =
                    windowTitle;

                _lastWindowProcessId =
                    (int)processId;

                string processPath = "";
                string processName =
                    "Unknown";

                try
                {
                    using Process process =
                        Process.GetProcessById(
                            (int)processId);

                    processName =
                        process.ProcessName;
                    processPath = Services.ProcessPathReader.Read(process.Id);
                }
                catch
                {
                }

                _logService.Write(
                    "Application",
                    "Foreground",
                    processName,
                    $"Активно: {windowTitle}",
                    $"Process: {processName}",
                    (int)processId, processPath);
            }
            catch
            {
            }
        }

        // ============================================
        // FILE MONITOR
        // ============================================

        private void StartFileMonitoring()
        {
            string desktop =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory);

            string documents =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.MyDocuments);

            string downloads =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.UserProfile),
                    "Downloads");

            AddWatcher(desktop);
            AddWatcher(documents);
            AddWatcher(downloads);
        }

        private void AddWatcher(
            string path)
        {
            if (!Directory.Exists(path))
                return;

            FileSystemWatcher watcher =
                new FileSystemWatcher(path);

            watcher.IncludeSubdirectories =
                true;

            watcher.NotifyFilter =
                NotifyFilters.FileName
                |
                NotifyFilters.DirectoryName
                |
                NotifyFilters.LastWrite
                |
                NotifyFilters.Size;

            watcher.Created +=
                FileCreated;

            watcher.Changed +=
                FileChanged;

            watcher.Deleted +=
                FileDeleted;

            watcher.Renamed +=
                FileRenamed;

            watcher.EnableRaisingEvents =
                true;

            _fileWatchers.Add(watcher);
        }

        private void FileCreated(
            object sender,
            FileSystemEventArgs e)
        {
            _logService.Write(
                "File",
                "Created",
                "FileSystem",
                $"Создан: {e.Name}",
                "",
                null,
                e.FullPath);
        }

        private void FileChanged(
            object sender,
            FileSystemEventArgs e)
        {
            _logService.Write(
                "File",
                "Changed",
                "FileSystem",
                $"Изменён: {e.Name}",
                "",
                null,
                e.FullPath);
        }

        private void FileDeleted(
            object sender,
            FileSystemEventArgs e)
        {
            _logService.Write(
                "File",
                "Deleted",
                "FileSystem",
                $"Удалён: {e.Name}",
                "",
                null,
                e.FullPath);
        }

        private void FileRenamed(
            object sender,
            RenamedEventArgs e)
        {
            _logService.Write(
                "File",
                "Renamed",
                "FileSystem",
                $"Переименован: {e.OldName} → {e.Name}",
                $"Old: {e.OldFullPath}",
                null,
                e.FullPath);
        }

        // ============================================
        // WINDOWS API
        // ============================================

        [DllImport("user32.dll")]
        private static extern IntPtr
            GetForegroundWindow();

        [DllImport(
            "user32.dll",
            CharSet = CharSet.Unicode)]
        private static extern int
            GetWindowText(
                IntPtr hWnd,
                StringBuilder text,
                int count);

        [DllImport("user32.dll")]
        private static extern uint
            GetWindowThreadProcessId(
                IntPtr hWnd,
                out uint processId);
    }
}
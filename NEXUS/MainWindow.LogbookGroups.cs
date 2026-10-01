using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace NEXUS
{
    public sealed partial class MainWindow
    {
        private readonly ObservableCollection<RunningProcessItem> _groupedRunningProcesses = new();
        private bool _logbookGroupingInitialized;

        public void InitializeLogbookGrouping()
        {
            if (_logbookGroupingInitialized)
                return;

            _logbookGroupingInitialized = true;
            RunningProcessesList.ItemsSource = _groupedRunningProcesses;

            _logbookTimer.Tick += (_, _) =>
            {
                if (_isLogbookVisible && LiveToggle.IsOn)
                    RefreshGroupedRunningProcesses();
            };

            if (Content is DependencyObject root)
            {
                foreach (Button button in FindDescendants<Button>(root)
                    .Where(button => string.Equals(
                        button.Content?.ToString(),
                        "Logbook",
                        StringComparison.Ordinal)))
                {
                    button.Click += (_, _) => RefreshGroupedRunningProcesses();
                }
            }

            RefreshGroupedRunningProcesses();
            UpdateVersionTo013();
        }

        private void RefreshGroupedRunningProcesses()
        {
            try
            {
                var groups = _runningProcesses
                    .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new
                    {
                        Name = group.Key,
                        Count = group.Count(),
                        MemoryBytes = group.Sum(item => item.MemoryBytes),
                        Pids = group.Select(item => item.ProcessId).OrderBy(id => id).Take(6).ToArray()
                    })
                    .OrderByDescending(item => item.MemoryBytes)
                    .ThenBy(item => item.Name)
                    .ToList();

                _groupedRunningProcesses.Clear();

                foreach (var group in groups)
                {
                    string pidText = string.Join(", ", group.Pids);
                    if (group.Count > group.Pids.Length)
                        pidText += " …";

                    _groupedRunningProcesses.Add(new RunningProcessItem
                    {
                        Name = group.Count > 1
                            ? $"{group.Name} × {group.Count}"
                            : group.Name,
                        ProcessId = group.Pids.FirstOrDefault(),
                        MemoryBytes = group.MemoryBytes,
                        Memory = FormatBytes(group.MemoryBytes),
                        Details = group.Count > 1
                            ? $"{group.Count} процессов • PID {pidText}"
                            : $"PID {pidText}"
                    });
                }

                RunningProcessesCountText.Text =
                    $"{groups.Count} групп • {_runningProcesses.Count} процессов";
            }
            catch
            {
                RunningProcessesCountText.Text = "Не удалось сгруппировать процессы";
            }
        }

        private void UpdateVersionTo013()
        {
            if (Content is not DependencyObject root)
                return;

            foreach (TextBlock textBlock in FindDescendants<TextBlock>(root))
            {
                if (textBlock.Text.StartsWith("NEXUS v", StringComparison.OrdinalIgnoreCase))
                {
                    textBlock.Text = "NEXUS v0.1.3";
                    break;
                }
            }
        }
    }
}

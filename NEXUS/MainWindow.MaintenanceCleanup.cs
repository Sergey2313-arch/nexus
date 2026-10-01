using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NEXUS
{
    public sealed partial class MainWindow
    {
        private TextBlock? _safeCleanupStatusText;
        private bool _safeCleanupInitialized;

        public void InitializeSafeMaintenanceCleanup()
        {
            if (_safeCleanupInitialized || _maintenancePage == null)
                return;

            _safeCleanupInitialized = true;

            StackPanel? body = GetPageBody(_maintenancePage);
            if (body == null)
                return;

            Border card = CreateCard();
            StackPanel panel = new() { Spacing = 10 };

            panel.Children.Add(new TextBlock
            {
                Text = "SAFE TEMP CLEANUP",
                Foreground = Brush(154, 157, 165),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Удаляет только старые файлы из TEMP текущего пользователя. Файлы младше 48 часов, занятые процессы и недоступные объекты пропускаются.",
                Foreground = Brush(115, 119, 127),
                TextWrapping = TextWrapping.Wrap
            });

            _safeCleanupStatusText = new TextBlock
            {
                Text = "Очистка ещё не запускалась.",
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(_safeCleanupStatusText);

            Button clean = new()
            {
                Content = "CLEAN OLD USER TEMP",
                HorizontalAlignment = HorizontalAlignment.Left
            };
            clean.Click += SafeTempCleanup_Click;
            panel.Children.Add(clean);

            card.Child = panel;
            body.Children.Add(card);

            UpdateVersionTo015();
        }

        private async void SafeTempCleanup_Click(object sender, RoutedEventArgs e)
        {
            if (_safeCleanupStatusText == null || _maintenancePage == null)
                return;

            ContentDialog dialog = new()
            {
                Title = "Очистить старые TEMP-файлы?",
                Content = "NEXUS удалит только файлы старше 48 часов из TEMP текущего пользователя. Заблокированные и недоступные файлы будут пропущены.",
                PrimaryButtonText = "Очистить",
                CloseButtonText = "Отмена",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = _maintenancePage.XamlRoot
            };

            if (dialog.XamlRoot == null)
            {
                _safeCleanupStatusText.Text = "Не удалось открыть подтверждение очистки.";
                return;
            }

            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return;

            Button? button = sender as Button;
            if (button != null)
                button.IsEnabled = false;

            _safeCleanupStatusText.Text = "Очистка старых TEMP-файлов...";

            try
            {
                TempCleanupResult cleanup = await Task.Run(CleanOldUserTemp);

                _safeCleanupStatusText.Text =
                    $"Удалено файлов: {cleanup.FilesDeleted}\n" +
                    $"Освобождено: {FormatBytes(cleanup.BytesFreed)}\n" +
                    $"Пропущено/занято: {cleanup.FilesSkipped}";

                _logService.Write(
                    "System",
                    "MaintenanceCleanup",
                    "Maintenance",
                    "Безопасная очистка TEMP завершена",
                    $"Deleted {cleanup.FilesDeleted} files • freed {FormatBytes(cleanup.BytesFreed)} • skipped {cleanup.FilesSkipped}");

                RefreshMaintenanceQuickStatus();
            }
            catch (Exception ex)
            {
                _safeCleanupStatusText.Text = "Ошибка очистки: " + ex.Message;
                _logService.Write(
                    "System",
                    "MaintenanceCleanupError",
                    "Maintenance",
                    "Ошибка очистки TEMP",
                    ex.Message,
                    severity: "Warning");
            }
            finally
            {
                if (button != null)
                    button.IsEnabled = true;
            }
        }

        private static TempCleanupResult CleanOldUserTemp()
        {
            TempCleanupResult result = new();
            string root = Path.GetTempPath();
            DateTime cutoff = DateTime.Now.AddHours(-48);

            if (!Directory.Exists(root))
                return result;

            List<string> directories = new();
            Stack<string> pending = new();
            pending.Push(root);

            while (pending.Count > 0)
            {
                string current = pending.Pop();
                directories.Add(current);

                string[] files = Array.Empty<string>();
                string[] dirs = Array.Empty<string>();

                try { files = Directory.GetFiles(current); }
                catch { }

                foreach (string file in files)
                {
                    try
                    {
                        FileInfo info = new(file);
                        if (info.LastWriteTime > cutoff)
                        {
                            result.FilesSkipped++;
                            continue;
                        }

                        long size = info.Length;
                        info.IsReadOnly = false;
                        info.Delete();
                        result.FilesDeleted++;
                        result.BytesFreed += size;
                    }
                    catch
                    {
                        result.FilesSkipped++;
                    }
                }

                try { dirs = Directory.GetDirectories(current); }
                catch { }

                foreach (string directory in dirs)
                {
                    try
                    {
                        FileAttributes attributes = File.GetAttributes(directory);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            result.FilesSkipped++;
                            continue;
                        }

                        pending.Push(directory);
                    }
                    catch
                    {
                        result.FilesSkipped++;
                    }
                }
            }

            foreach (string directory in directories
                .OrderByDescending(path => path.Length))
            {
                if (string.Equals(directory, root, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    DirectoryInfo info = new(directory);
                    if (info.Exists &&
                        info.LastWriteTime <= cutoff &&
                        !info.EnumerateFileSystemInfos().Any())
                    {
                        info.Delete(false);
                    }
                }
                catch { }
            }

            return result;
        }

        private void UpdateVersionTo015()
        {
            if (Content is not DependencyObject root)
                return;

            foreach (TextBlock textBlock in FindDescendants<TextBlock>(root))
            {
                if (textBlock.Text.StartsWith("NEXUS v", StringComparison.OrdinalIgnoreCase))
                {
                    textBlock.Text = "NEXUS v0.1.5";
                    break;
                }
            }
        }

        private sealed class TempCleanupResult
        {
            public int FilesDeleted { get; set; }
            public int FilesSkipped { get; set; }
            public long BytesFreed { get; set; }
        }
    }
}

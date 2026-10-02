using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Linq;

namespace NEXUS
{
    public sealed partial class MainWindow
    {
        private bool _visualPolishInitialized;

        public void InitializeVisualPolish()
        {
            if (_visualPolishInitialized)
                return;

            _visualPolishInitialized = true;

            if (Content is not Grid root)
                return;

            ApplyControlPolish(root);
            ApplyResponsiveLayout(root, Bounds.Width);

            SizeChanged += (_, args) =>
            {
                ApplyResponsiveLayout(root, args.Size.Width);
                ApplyControlPolish(root);
            };
        }

        private void ApplyControlPolish(DependencyObject root)
        {
            foreach (Button button in FindDescendants<Button>(root))
            {
                if (button.Tag is string tag && tag.StartsWith("nav:", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (button.Content is not string text)
                    continue;

                string normalized = text.Trim().ToLowerInvariant();

                if (IsDangerAction(normalized))
                {
                    TryApplyButtonStyle(button, "NexusDangerButtonStyle");
                }
                else if (IsPrimaryAction(normalized))
                {
                    TryApplyButtonStyle(button, "NexusPrimaryButtonStyle");
                }
                else
                {
                    TryApplyButtonStyle(button, "NexusSecondaryButtonStyle");
                }
            }

            foreach (ListView list in FindDescendants<ListView>(root))
            {
                list.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                list.BorderThickness = new Thickness(0);
            }

            foreach (ProgressBar bar in FindDescendants<ProgressBar>(root))
            {
                bar.MinHeight = 5;
                bar.MaxHeight = 7;
            }

            foreach (ScrollViewer scroll in FindDescendants<ScrollViewer>(root))
            {
                scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }
        }

        private static bool IsPrimaryAction(string text)
        {
            string[] tokens =
            {
                "run ", "scan", "generate", "analyze", "refresh",
                "запустить", "провер", "скан", "сформировать", "анализ", "обновить",
                "добавить", "add"
            };

            return tokens.Any(text.Contains);
        }

        private static bool IsDangerAction(string text)
        {
            string[] tokens =
            {
                "delete", "remove", "clear completed", "clean old temp",
                "удалить", "очистить выполненные", "очистить старый temp"
            };

            return tokens.Any(text.Contains);
        }

        private static void TryApplyButtonStyle(Button button, string key)
        {
            try
            {
                object value = Application.Current.Resources[key];
                if (value is Style style)
                    button.Style = style;
            }
            catch
            {
                // UI polish must never break the application if a resource is missing.
            }
        }

        private static void ApplyResponsiveLayout(Grid root, double width)
        {
            if (root.ColumnDefinitions.Count < 2)
                return;

            double sidebarWidth = width switch
            {
                < 980 => 205,
                < 1220 => 226,
                _ => 252
            };

            root.ColumnDefinitions[0].Width = new GridLength(sidebarWidth);

            Border? sidebar = root.Children
                .OfType<Border>()
                .FirstOrDefault(border => Grid.GetColumn(border) == 0);

            if (sidebar?.Child is Grid sidebarGrid)
            {
                sidebarGrid.Margin = width < 980
                    ? new Thickness(8)
                    : new Thickness(12);

                StackPanel? menu = sidebarGrid.Children
                    .OfType<StackPanel>()
                    .FirstOrDefault(panel => Grid.GetRow(panel) == 1);

                if (menu != null)
                {
                    foreach (Button button in menu.Children.OfType<Button>())
                    {
                        button.Height = width < 980 ? 42 : 44;
                        button.Padding = width < 980
                            ? new Thickness(10, 8, 10, 8)
                            : new Thickness(14, 10, 14, 10);
                    }
                }
            }
        }
    }
}

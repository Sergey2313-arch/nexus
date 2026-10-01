using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NEXUS
{
    public sealed partial class MainWindow
    {
        private ComboBox? _topLanguageCombo;
        private ComboBox? _settingsLanguageCombo;
        private DispatcherTimer? _localizationRefreshTimer;
        private readonly List<Button> _modernNavigationButtons = new();
        private bool _languageSync;
        private bool _modernUiInitialized;

        public void InitializeModernInterface()
        {
            if (_modernUiInitialized)
                return;

            _modernUiInitialized = true;
            LocalizationService.Load();

            if (Content is not Grid root)
                return;

            ApplyModernShell(root);
            BuildLanguageControls(root);
            BuildSettingsLanguageCard();
            ApplyLocalization();
            SelectNavigationVisual("Dashboard");
            StartLocalizationRefresh();
            UpdateVersionTo022();
        }

        private void ApplyModernShell(Grid root)
        {
            root.Background = Brush(11, 13, 17);

            if (root.ColumnDefinitions.Count >= 2)
                root.ColumnDefinitions[0].Width = new GridLength(252);

            Border? sidebar = root.Children
                .OfType<Border>()
                .FirstOrDefault(border => Grid.GetColumn(border) == 0);

            if (sidebar != null)
            {
                sidebar.Background = Brush(17, 20, 25);
                sidebar.BorderBrush = Brush(37, 42, 51);

                if (sidebar.Child is Grid sidebarGrid)
                {
                    sidebarGrid.Margin = new Thickness(12);

                    TextBlock? logo = sidebarGrid.Children
                        .OfType<TextBlock>()
                        .FirstOrDefault(text => Grid.GetRow(text) == 0 && text.Text == "NEXUS");

                    if (logo != null)
                    {
                        logo.FontSize = 25;
                        logo.Margin = new Thickness(8, 20, 0, 28);
                    }

                    StackPanel? menu = sidebarGrid.Children
                        .OfType<StackPanel>()
                        .FirstOrDefault(panel => Grid.GetRow(panel) == 1);

                    if (menu != null)
                    {
                        menu.Spacing = 6;
                        foreach (Button button in menu.Children.OfType<Button>().ToList())
                            ModernizeNavigationButton(button, true);
                    }
                }
            }

            Grid? mainGrid = root.Children
                .OfType<Grid>()
                .FirstOrDefault(grid => Grid.GetColumn(grid) == 1);

            if (mainGrid != null)
            {
                if (mainGrid.RowDefinitions.Count > 0)
                    mainGrid.RowDefinitions[0].Height = new GridLength(64);

                Border? topBar = mainGrid.Children
                    .OfType<Border>()
                    .FirstOrDefault(border => Grid.GetRow(border) == 0);

                if (topBar != null)
                {
                    topBar.Background = Brush(17, 20, 25);
                    topBar.BorderBrush = Brush(37, 42, 51);

                    if (topBar.Child is StackPanel topMenu)
                    {
                        topMenu.Spacing = 6;
                        topMenu.Margin = new Thickness(18, 0, 18, 0);
                        foreach (Button button in topMenu.Children.OfType<Button>().ToList())
                            ModernizeNavigationButton(button, false);
                    }
                }
            }

            foreach (Border border in FindDescendants<Border>(root))
            {
                if (ReferenceEquals(border, sidebar))
                    continue;

                if (border.Padding.Left >= 14 && border.BorderThickness.Left > 0)
                {
                    border.CornerRadius = new CornerRadius(14);
                    border.Background = Brush(21, 24, 30);
                    border.BorderBrush = Brush(39, 44, 53);
                }
            }

            foreach (TextBlock text in FindDescendants<TextBlock>(root))
            {
                if (text.FontSize >= 34)
                    text.FontSize = 32;
            }
        }

        private void ModernizeNavigationButton(Button button, bool sidebar)
        {
            string text = button.Content?.ToString() ?? "";
            string? key = NormalizeNavigationKey(text);
            if (key == null)
                return;

            button.Tag = "nav:" + key;
            button.CornerRadius = new CornerRadius(9);
            button.BorderThickness = new Thickness(0);
            button.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            button.Foreground = Brush(194, 198, 207);
            button.HorizontalContentAlignment = sidebar ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            button.Padding = sidebar ? new Thickness(14, 10, 14, 10) : new Thickness(13, 8, 13, 8);

            if (sidebar)
            {
                button.Height = 44;

                StackPanel content = new()
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12
                };

                content.Children.Add(new FontIcon
                {
                    Glyph = NavigationGlyph(key),
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    FontSize = 17,
                    VerticalAlignment = VerticalAlignment.Center
                });

                TextBlock label = new()
                {
                    Tag = "loc:Nav." + key,
                    Text = LocalizationService.T("Nav." + key),
                    FontSize = 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };

                content.Children.Add(label);
                button.Content = content;
            }

            button.Click += ModernNavigationButton_Click;
            _modernNavigationButtons.Add(button);
        }

        private void ModernNavigationButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tag && tag.StartsWith("nav:", StringComparison.Ordinal))
                SelectNavigationVisual(tag.Substring(4));
        }

        private void SelectNavigationVisual(string key)
        {
            foreach (Button button in _modernNavigationButtons)
            {
                bool selected = button.Tag is string tag &&
                                tag.Equals("nav:" + key, StringComparison.OrdinalIgnoreCase);

                button.Background = selected
                    ? Brush(42, 52, 73)
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
                button.Foreground = selected ? Brush(239, 242, 248) : Brush(194, 198, 207);
            }
        }

        private void BuildLanguageControls(Grid root)
        {
            Grid? mainGrid = root.Children
                .OfType<Grid>()
                .FirstOrDefault(grid => Grid.GetColumn(grid) == 1);

            Border? topBar = mainGrid?.Children
                .OfType<Border>()
                .FirstOrDefault(border => Grid.GetRow(border) == 0);

            if (topBar?.Child is not StackPanel oldMenu)
                return;

            topBar.Child = null;

            Grid topGrid = new();
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(oldMenu, 0);
            topGrid.Children.Add(oldMenu);

            StackPanel right = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 18, 0)
            };

            Border status = new()
            {
                Background = Brush(22, 49, 31),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(11, 6, 11, 6),
                Child = new TextBlock
                {
                    Text = "● NEXUS",
                    Foreground = Brush(103, 209, 122),
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                }
            };

            _topLanguageCombo = CreateLanguageCombo();
            right.Children.Add(status);
            right.Children.Add(_topLanguageCombo);
            Grid.SetColumn(right, 1);
            topGrid.Children.Add(right);

            topBar.Child = topGrid;
        }

        private void BuildSettingsLanguageCard()
        {
            StackPanel? body = GetPageBody(_settingsPage);
            if (body == null)
                return;

            Border card = CreateCard();
            StackPanel content = new() { Spacing = 10 };

            content.Children.Add(new TextBlock
            {
                Tag = "loc:Settings.Language",
                Text = LocalizationService.T("Settings.Language"),
                Foreground = Brush(154, 157, 165),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            content.Children.Add(new TextBlock
            {
                Tag = "loc:Settings.LanguageHint",
                Text = LocalizationService.T("Settings.LanguageHint"),
                Foreground = Brush(115, 119, 127),
                TextWrapping = TextWrapping.Wrap
            });

            _settingsLanguageCombo = CreateLanguageCombo();
            _settingsLanguageCombo.HorizontalAlignment = HorizontalAlignment.Left;
            _settingsLanguageCombo.MinWidth = 180;
            content.Children.Add(_settingsLanguageCombo);

            card.Child = content;
            body.Children.Insert(Math.Min(2, body.Children.Count), card);
        }

        private ComboBox CreateLanguageCombo()
        {
            ComboBox combo = new()
            {
                MinWidth = 120,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            combo.Items.Add("Русский");
            combo.Items.Add("English");
            combo.SelectedIndex = LocalizationService.CurrentLanguage == NexusLanguage.English ? 1 : 0;
            combo.SelectionChanged += LanguageCombo_SelectionChanged;
            return combo;
        }

        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_languageSync || sender is not ComboBox combo || combo.SelectedIndex < 0)
                return;

            LocalizationService.SetLanguage(
                combo.SelectedIndex == 1 ? NexusLanguage.English : NexusLanguage.Russian);

            ApplyLocalization();
            RefreshLiveLocalizedTexts();
        }

        private void ApplyLocalization()
        {
            if (Content is not DependencyObject root)
                return;

            _languageSync = true;
            try
            {
                int languageIndex = LocalizationService.CurrentLanguage == NexusLanguage.English ? 1 : 0;
                if (_topLanguageCombo != null) _topLanguageCombo.SelectedIndex = languageIndex;
                if (_settingsLanguageCombo != null) _settingsLanguageCombo.SelectedIndex = languageIndex;

                foreach (TextBlock text in FindDescendants<TextBlock>(root))
                {
                    if (text.Tag is string tag && tag.StartsWith("loc:", StringComparison.Ordinal))
                    {
                        text.Text = LocalizationService.T(tag.Substring(4));
                        continue;
                    }

                    if (LocalizationService.TryTranslateLiteral(text.Text ?? "", out string translated))
                        text.Text = translated;
                }

                foreach (Button button in FindDescendants<Button>(root))
                {
                    if (button.Content is string content &&
                        LocalizationService.TryTranslateLiteral(content, out string translated))
                    {
                        button.Content = translated;
                    }
                }

                foreach (ToggleSwitch toggle in FindDescendants<ToggleSwitch>(root))
                {
                    if (toggle.Header is string header &&
                        LocalizationService.TryTranslateLiteral(header, out string translated))
                    {
                        toggle.Header = translated;
                    }
                }

                foreach (TextBox textBox in FindDescendants<TextBox>(root))
                {
                    string placeholder = textBox.PlaceholderText ?? "";
                    if (LocalizationService.TryTranslateLiteral(placeholder, out string translated))
                        textBox.PlaceholderText = translated;
                }

                foreach (ComboBox combo in FindDescendants<ComboBox>(root))
                {
                    string placeholder = combo.PlaceholderText ?? "";
                    if (LocalizationService.TryTranslateLiteral(placeholder, out string translated))
                        combo.PlaceholderText = translated;
                }

                foreach (CalendarDatePicker picker in FindDescendants<CalendarDatePicker>(root))
                {
                    string placeholder = picker.PlaceholderText ?? "";
                    if (LocalizationService.TryTranslateLiteral(placeholder, out string translated))
                        picker.PlaceholderText = translated;
                }
            }
            finally
            {
                _languageSync = false;
            }
        }

        private void StartLocalizationRefresh()
        {
            _localizationRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.2)
            };
            _localizationRefreshTimer.Tick += (_, _) => RefreshLiveLocalizedTexts();
            _localizationRefreshTimer.Start();

            Closed += (_, _) =>
            {
                try { _localizationRefreshTimer?.Stop(); } catch { }
            };
        }

        private void RefreshLiveLocalizedTexts()
        {
            DiskStatusText.Text = LocalizationService.TranslateDynamic(DiskStatusText.Text);
            CpuTempStatusText.Text = LocalizationService.TranslateDynamic(CpuTempStatusText.Text);
            GpuTempText.Text = LocalizationService.TranslateDynamic(GpuTempText.Text);
            StorageNameText.Text = LocalizationService.TranslateDynamic(StorageNameText.Text);
            StorageStatusText.Text = LocalizationService.TranslateDynamic(StorageStatusText.Text);
            StoragePowerOnText.Text = LocalizationService.TranslateDynamic(StoragePowerOnText.Text);
            StorageIoText.Text = LocalizationService.TranslateDynamic(StorageIoText.Text);
            RunningProcessesCountText.Text = LocalizationService.TranslateDynamic(RunningProcessesCountText.Text);
            LogbookCountText.Text = LocalizationService.TranslateDynamic(LogbookCountText.Text);
        }

        private static string? NormalizeNavigationKey(string text)
        {
            return text switch
            {
                "Dashboard" or "Главная" => "Dashboard",
                "Devices" or "Устройства" => "Devices",
                "Planner" or "Планировщик" => "Planner",
                "Logbook" or "Журнал" => "Logbook",
                "Diagnostics" or "Диагностика" => "Diagnostics",
                "Security" or "Безопасность" => "Security",
                "Maintenance" or "Обслуживание" => "Maintenance",
                "AI" or "AI Assistant" or "ИИ" or "ИИ-помощник" => "AI",
                "Settings" or "Настройки" => "Settings",
                _ => null
            };
        }

        private static string NavigationGlyph(string key)
        {
            return key switch
            {
                "Dashboard" => "\uE80F",
                "Devices" => "\uE7F4",
                "Planner" => "\uE787",
                "Logbook" => "\uE8A5",
                "Diagnostics" => "\uE90F",
                "Security" => "\uE72E",
                "Maintenance" => "\uE74D",
                "AI" => "\uE945",
                "Settings" => "\uE713",
                _ => "\uE10C"
            };
        }

        private void UpdateVersionTo022()
        {
            if (Content is not DependencyObject root)
                return;

            foreach (TextBlock textBlock in FindDescendants<TextBlock>(root))
            {
                if (textBlock.Text.StartsWith("NEXUS v", StringComparison.OrdinalIgnoreCase))
                {
                    textBlock.Text = "NEXUS v0.2.2";
                    break;
                }
            }
        }
    }
}

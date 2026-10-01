using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NEXUS
{
    public sealed partial class MainWindow
    {
        private readonly Dictionary<string, PlannerTaskMetadata> _plannerMetadata = new();
        private ComboBox? _plannerPriorityCombo;
        private CalendarDatePicker? _plannerDueDatePicker;
        private StackPanel? _plannerUpcomingPanel;
        private TextBlock? _plannerUpcomingSummary;
        private bool _plannerV2Initialized;

        public void InitializePlannerV2()
        {
            if (_plannerV2Initialized || _plannerPage == null)
                return;

            _plannerV2Initialized = true;
            LoadPlannerMetadata();

            StackPanel? body = GetPageBody(_plannerPage);
            if (body == null)
                return;

            Grid? inputRow = body.Children
                .OfType<Grid>()
                .FirstOrDefault(grid => grid.Children.OfType<TextBox>().Any() &&
                                        grid.Children.OfType<Button>().Any(button => button.Content?.ToString() == "Добавить"));

            if (inputRow != null)
                UpgradePlannerInputRow(inputRow);

            AddPlannerUpcomingCard(body);
            CleanupPlannerMetadata();
            RenderPlannerUpcoming();

            if (_pageEnhancementTimer != null)
            {
                _pageEnhancementTimer.Tick += (_, _) =>
                {
                    if (_plannerPage?.Visibility == Visibility.Visible)
                    {
                        CleanupPlannerMetadata();
                        RenderPlannerUpcoming();
                    }
                };
            }

            UpdateVersionTo014();
        }

        private void UpgradePlannerInputRow(Grid inputRow)
        {
            Button? addButton = inputRow.Children
                .OfType<Button>()
                .FirstOrDefault(button => button.Content?.ToString() == "Добавить");

            if (addButton == null)
                return;

            // Было: [текст] [добавить]
            // Стало: [текст] [приоритет] [срок] [добавить]
            inputRow.ColumnDefinitions.Clear();
            inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(185) });
            inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _plannerPriorityCombo = new ComboBox
            {
                PlaceholderText = "Приоритет",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                SelectedIndex = 0
            };
            _plannerPriorityCombo.Items.Add("Обычный");
            _plannerPriorityCombo.Items.Add("Высокий");
            _plannerPriorityCombo.Items.Add("Низкий");
            Grid.SetColumn(_plannerPriorityCombo, 1);

            _plannerDueDatePicker = new CalendarDatePicker
            {
                PlaceholderText = "Срок",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetColumn(_plannerDueDatePicker, 2);

            Grid.SetColumn(addButton, 3);
            addButton.Click -= PlannerAddButton_Click;
            addButton.Click += PlannerV2AddButton_Click;

            inputRow.Children.Add(_plannerPriorityCombo);
            inputRow.Children.Add(_plannerDueDatePicker);
        }

        private void PlannerV2AddButton_Click(object sender, RoutedEventArgs e)
        {
            string text = _plannerInput?.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(text))
                return;

            PlannerTaskItem item = new()
            {
                Id = Guid.NewGuid().ToString("N"),
                Text = text,
                CreatedAt = DateTime.Now,
                Completed = false
            };

            _plannerTasks.Add(item);

            string priority = _plannerPriorityCombo?.SelectedItem?.ToString() ?? "Обычный";
            DateTime? dueDate = _plannerDueDatePicker?.Date?.LocalDateTime.Date;

            _plannerMetadata[item.Id] = new PlannerTaskMetadata
            {
                Priority = priority,
                DueDate = dueDate
            };

            if (_plannerInput != null)
                _plannerInput.Text = "";

            if (_plannerPriorityCombo != null)
                _plannerPriorityCombo.SelectedIndex = 0;

            if (_plannerDueDatePicker != null)
                _plannerDueDatePicker.Date = null;

            SavePlanner();
            SavePlannerMetadata();
            RenderPlanner();
            RefreshPlannerSummary();
            RenderPlannerUpcoming();
        }

        private void AddPlannerUpcomingCard(StackPanel body)
        {
            Border card = CreateCard();
            StackPanel panel = new() { Spacing = 10 };

            panel.Children.Add(new TextBlock
            {
                Text = "DEADLINES & PRIORITIES",
                Foreground = Brush(154, 157, 165),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            _plannerUpcomingSummary = new TextBlock
            {
                Text = "Сроки: --",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            panel.Children.Add(_plannerUpcomingSummary);

            _plannerUpcomingPanel = new StackPanel { Spacing = 7 };
            panel.Children.Add(_plannerUpcomingPanel);

            card.Child = panel;
            body.Children.Add(card);
        }

        private void RenderPlannerUpcoming()
        {
            if (_plannerUpcomingPanel == null || _plannerUpcomingSummary == null)
                return;

            _plannerUpcomingPanel.Children.Clear();

            DateTime today = DateTime.Today;
            var active = _plannerTasks
                .Where(task => !task.Completed)
                .Select(task => new
                {
                    Task = task,
                    Meta = _plannerMetadata.TryGetValue(task.Id, out PlannerTaskMetadata? meta)
                        ? meta
                        : new PlannerTaskMetadata()
                })
                .OrderBy(item => PriorityRank(item.Meta.Priority))
                .ThenBy(item => item.Meta.DueDate ?? DateTime.MaxValue)
                .ThenByDescending(item => item.Task.CreatedAt)
                .ToList();

            int overdue = active.Count(item => item.Meta.DueDate.HasValue && item.Meta.DueDate.Value.Date < today);
            int todayCount = active.Count(item => item.Meta.DueDate.HasValue && item.Meta.DueDate.Value.Date == today);
            int high = active.Count(item => string.Equals(item.Meta.Priority, "Высокий", StringComparison.OrdinalIgnoreCase));

            _plannerUpcomingSummary.Text =
                $"Просрочено: {overdue} • Сегодня: {todayCount} • Высокий приоритет: {high}";

            if (active.Count == 0)
            {
                _plannerUpcomingPanel.Children.Add(new TextBlock
                {
                    Text = "Активных задач нет.",
                    Foreground = Brush(115, 119, 127)
                });
                return;
            }

            foreach (var item in active.Take(10))
            {
                Grid row = new() { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                StackPanel info = new() { Spacing = 2 };
                info.Children.Add(new TextBlock
                {
                    Text = item.Task.Text,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                });

                string due = item.Meta.DueDate.HasValue
                    ? item.Meta.DueDate.Value.ToString("dd.MM.yyyy")
                    : "без срока";

                string state = item.Meta.DueDate.HasValue && item.Meta.DueDate.Value.Date < today
                    ? " • ПРОСРОЧЕНО"
                    : item.Meta.DueDate.HasValue && item.Meta.DueDate.Value.Date == today
                        ? " • СЕГОДНЯ"
                        : "";

                info.Children.Add(new TextBlock
                {
                    Text = $"{item.Meta.Priority} • {due}{state}",
                    Foreground = Brush(115, 119, 127),
                    FontSize = 11
                });

                Button complete = new()
                {
                    Content = "Готово",
                    VerticalAlignment = VerticalAlignment.Center
                };
                complete.Click += (_, _) =>
                {
                    item.Task.Completed = true;
                    SavePlanner();
                    RenderPlanner();
                    RefreshPlannerSummary();
                    RenderPlannerUpcoming();
                };
                Grid.SetColumn(complete, 1);

                row.Children.Add(info);
                row.Children.Add(complete);
                _plannerUpcomingPanel.Children.Add(row);
            }
        }

        private static int PriorityRank(string? priority) =>
            priority switch
            {
                "Высокий" => 0,
                "Обычный" => 1,
                "Низкий" => 2,
                _ => 1
            };

        private string PlannerMetadataPath => Path.Combine(NexusDataFolder, "planner-meta.json");

        private void LoadPlannerMetadata()
        {
            try
            {
                if (!File.Exists(PlannerMetadataPath))
                    return;

                Dictionary<string, PlannerTaskMetadata>? items =
                    JsonSerializer.Deserialize<Dictionary<string, PlannerTaskMetadata>>(
                        File.ReadAllText(PlannerMetadataPath));

                if (items == null)
                    return;

                _plannerMetadata.Clear();
                foreach (var item in items)
                    _plannerMetadata[item.Key] = item.Value;
            }
            catch { }
        }

        private void SavePlannerMetadata()
        {
            try
            {
                Directory.CreateDirectory(NexusDataFolder);
                File.WriteAllText(
                    PlannerMetadataPath,
                    JsonSerializer.Serialize(
                        _plannerMetadata,
                        new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        private void CleanupPlannerMetadata()
        {
            HashSet<string> ids = _plannerTasks
                .Select(task => task.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            string[] stale = _plannerMetadata.Keys
                .Where(id => !ids.Contains(id))
                .ToArray();

            if (stale.Length == 0)
                return;

            foreach (string id in stale)
                _plannerMetadata.Remove(id);

            SavePlannerMetadata();
        }

        private void UpdateVersionTo014()
        {
            if (Content is not DependencyObject root)
                return;

            foreach (TextBlock textBlock in FindDescendants<TextBlock>(root))
            {
                if (textBlock.Text.StartsWith("NEXUS v", StringComparison.OrdinalIgnoreCase))
                {
                    textBlock.Text = "NEXUS v0.1.4";
                    break;
                }
            }
        }

        private sealed class PlannerTaskMetadata
        {
            public string Priority { get; set; } = "Обычный";
            public DateTime? DueDate { get; set; }
        }
    }
}

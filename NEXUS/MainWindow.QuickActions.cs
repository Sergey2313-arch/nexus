using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace NEXUS
{
    public sealed partial class MainWindow
    {
        private bool _quickActionsInitialized;

        public void InitializeQuickActions()
        {
            if (_quickActionsInitialized)
                return;

            _quickActionsInitialized = true;

            if (DashboardView.Content is not StackPanel body)
                return;

            Border card = CreateCard();
            card.Padding = new Thickness(16);

            Grid row = new() { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Button diagnostics = CreateQuickActionButton("Диагностика", "\uE90F");
            diagnostics.Click += (_, _) =>
            {
                if (_diagnosticsDepartmentPage == null) return;
                ShowCustomPage(_diagnosticsDepartmentPage);
                SelectNavigationVisual("Diagnostics");
                RunHardwareDiagnosticsDepartment();
            };

            Button security = CreateQuickActionButton("Безопасность", "\uE72E");
            security.Click += (_, _) =>
            {
                if (_securityDepartmentPage == null) return;
                ShowCustomPage(_securityDepartmentPage);
                SelectNavigationVisual("Security");
            };

            Button maintenance = CreateQuickActionButton("Обслуживание", "\uE74D");
            maintenance.Click += (_, _) =>
            {
                if (_maintenancePage == null) return;
                ShowCustomPage(_maintenancePage);
                SelectNavigationVisual("Maintenance");
            };

            Grid.SetColumn(diagnostics, 0);
            Grid.SetColumn(security, 1);
            Grid.SetColumn(maintenance, 2);
            row.Children.Add(diagnostics);
            row.Children.Add(security);
            row.Children.Add(maintenance);

            card.Child = row;
            body.Children.Insert(Math.Min(2, body.Children.Count), card);
        }

        private static Button CreateQuickActionButton(string text, string glyph)
        {
            StackPanel content = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            content.Children.Add(new FontIcon
            {
                Glyph = glyph,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
                FontSize = 17
            });

            content.Children.Add(new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            return new Button
            {
                Content = content,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = 44,
                CornerRadius = new CornerRadius(10),
                Background = Brush(24, 29, 38),
                BorderBrush = Brush(42, 50, 63),
                BorderThickness = new Thickness(1)
            };
        }
    }
}

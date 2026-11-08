using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Circuiting.Models;
using WpfGrid = System.Windows.Controls.Grid;

namespace Circuiting.UI.Dialogs
{
    public class CircuitEditDialog : Window
    {
        private TextBox _leftDescBox;
        private ComboBox _leftBreakerCombo;
        private ComboBox _leftProtectionCombo;
        private ComboBox _leftLeakageCombo;
        private ComboBox _leftPolesCombo;

        private TextBox _rightDescBox;
        private ComboBox _rightBreakerCombo;
        private ComboBox _rightProtectionCombo;
        private ComboBox _rightLeakageCombo;
        private ComboBox _rightPolesCombo;

        public CircuitData? LeftResult { get; private set; }
        public CircuitData? RightResult { get; private set; }
        public bool Confirmed { get; private set; }

        private bool _allowMultiPole;

        public CircuitEditDialog(
            string leftLabel,
            string rightLabel,
            CircuitData? leftExisting,
            CircuitData? rightExisting,
            bool allowMultiPole = true)
        {
            _allowMultiPole = allowMultiPole;
            Title = $"Edit Circuit Row — {leftLabel} / {rightLabel}";
            Width = 620;
            Height = 380;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(30, 30, 30));

            StackPanel main = new StackPanel
            {
                Margin = new Thickness(16)
            };

            // Two column grid for left and right
            WpfGrid grid = new WpfGrid();
            grid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left column
            StackPanel leftPanel = CreateCircuitPanel(
                $"LEFT — {leftLabel}",
                leftExisting,
                out _leftDescBox,
                out _leftBreakerCombo,
                out _leftProtectionCombo,
                out _leftLeakageCombo,
                out _leftPolesCombo);
            WpfGrid.SetColumn(leftPanel, 0);
            grid.Children.Add(leftPanel);

            // Divider
            Border divider = new Border
            {
                Width = 1,
                Background = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(80, 80, 80)),
                Margin = new Thickness(0, 0, 0, 0)
            };
            WpfGrid.SetColumn(divider, 1);
            grid.Children.Add(divider);

            // Right column
            StackPanel rightPanel = CreateCircuitPanel(
                $"RIGHT — {rightLabel}",
                rightExisting,
                out _rightDescBox,
                out _rightBreakerCombo,
                out _rightProtectionCombo,
                out _rightLeakageCombo,
                out _rightPolesCombo);
            WpfGrid.SetColumn(rightPanel, 2);
            grid.Children.Add(rightPanel);

            main.Children.Add(grid);

            // Buttons
            StackPanel buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            Button cancelButton = new Button
            {
                Content = "Cancel",
                Width = 80,
                Padding = new Thickness(0, 4, 0, 4),
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancelButton.Click += (s, e) =>
            {
                Confirmed = false;
                Close();
            };
            buttonPanel.Children.Add(cancelButton);

            Button saveButton = new Button
            {
                Content = "Save",
                Width = 80,
                Padding = new Thickness(0, 4, 0, 4),
                Background = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0, 120, 212)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0)
            };
            saveButton.Click += (s, e) => OnSave(
                leftLabel, rightLabel, leftExisting, rightExisting);
            buttonPanel.Children.Add(saveButton);

            main.Children.Add(buttonPanel);
            Content = main;
        }

        private StackPanel CreateCircuitPanel(
            string title,
            CircuitData? existing,
            out TextBox descBox,
            out ComboBox breakerCombo,
            out ComboBox protectionCombo,
            out ComboBox leakageCombo,
            out ComboBox polesCombo)
        {
            StackPanel panel = new StackPanel();

            // Title
            panel.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            // Description
            panel.Children.Add(CreateFieldLabel("Description:"));
            descBox = new TextBox
            {
                Text = existing?.Description ?? string.Empty,
                FontSize = 12,
                Padding = new Thickness(4, 3, 4, 3),
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(descBox);

            // Breaker Size
            panel.Children.Add(CreateFieldLabel("Breaker Size:"));
            breakerCombo = CreateCombo(
                CircuitOptions.BreakerSizes,
                existing?.BreakerSize ?? string.Empty);
            panel.Children.Add(breakerCombo);

            // Protection Type
            panel.Children.Add(CreateFieldLabel("Protection:"));
            protectionCombo = CreateCombo(
                CircuitOptions.ProtectionTypes,
                existing?.ProtectionType ?? string.Empty);
            panel.Children.Add(protectionCombo);

            // Earth Leakage
            panel.Children.Add(CreateFieldLabel("Earth Leakage:"));
            leakageCombo = CreateCombo(
                CircuitOptions.EarthLeakages,
                existing?.EarthLeakage ?? string.Empty);
            panel.Children.Add(leakageCombo);

            // Poles
            panel.Children.Add(CreateFieldLabel("Poles:"));
            polesCombo = new ComboBox
            {
                FontSize = 12,
                Padding = new Thickness(4, 3, 4, 3),
                Margin = new Thickness(0, 0, 0, 8)
            };
            if (_allowMultiPole)
            {
                foreach (int p in CircuitOptions.PoleOptions)
                {
                    polesCombo.Items.Add(p);
                }
            }
            else
            {
                polesCombo.Items.Add(1);
            }
            int currentPoles = existing?.Poles ?? 1;
            if (!_allowMultiPole) currentPoles = 1;
            polesCombo.SelectedItem = currentPoles;
            if (polesCombo.SelectedIndex < 0) polesCombo.SelectedIndex = 0;
            polesCombo.IsEnabled = _allowMultiPole;
            panel.Children.Add(polesCombo);

            return panel;
        }

        private TextBlock CreateFieldLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(200, 200, 200)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 3)
            };
        }

        private ComboBox CreateCombo(string[] options, string currentValue)
        {
            ComboBox combo = new ComboBox
            {
                FontSize = 12,
                Padding = new Thickness(4, 3, 4, 3),
                Margin = new Thickness(0, 0, 0, 8)
            };

            foreach (string opt in options)
            {
                combo.Items.Add(string.IsNullOrEmpty(opt) ? "(none)" : opt);
            }

            int idx = Array.IndexOf(options, currentValue);
            combo.SelectedIndex = idx >= 0 ? idx : 0;

            return combo;
        }

        private string GetComboValue(ComboBox combo, string[] options)
        {
            int idx = combo.SelectedIndex;
            if (idx < 0 || idx >= options.Length) return string.Empty;
            return options[idx];
        }

        private CircuitData BuildCircuitData(
            string displayLabel,
            CircuitData? existing,
            TextBox descBox,
            ComboBox breakerCombo,
            ComboBox protectionCombo,
            ComboBox leakageCombo,
            ComboBox polesCombo)
        {
            return new CircuitData
            {
                CircuitGuid = existing?.CircuitGuid ?? Guid.NewGuid(),
                DisplayLabel = displayLabel,
                CircuitNumber = existing?.CircuitNumber ?? 0,
                Description = descBox.Text.Trim(),
                BreakerSize = GetComboValue(
                    breakerCombo, CircuitOptions.BreakerSizes),
                ProtectionType = GetComboValue(
                    protectionCombo, CircuitOptions.ProtectionTypes),
                EarthLeakage = GetComboValue(
                    leakageCombo, CircuitOptions.EarthLeakages),
                Poles = polesCombo.SelectedItem is int p ? p : 1,
                Fixtures = existing?.Fixtures ?? new List<CircuitedFixture>()
            };
        }

        private bool HasData(TextBox descBox, ComboBox breakerCombo)
        {
            return !string.IsNullOrWhiteSpace(descBox.Text)
                || breakerCombo.SelectedIndex > 0;
        }

        private void OnSave(
            string leftLabel,
            string rightLabel,
            CircuitData? leftExisting,
            CircuitData? rightExisting)
        {
            if (HasData(_leftDescBox, _leftBreakerCombo))
            {
                LeftResult = BuildCircuitData(
                    leftLabel, leftExisting,
                    _leftDescBox, _leftBreakerCombo,
                    _leftProtectionCombo, _leftLeakageCombo,
                    _leftPolesCombo);
            }

            if (HasData(_rightDescBox, _rightBreakerCombo))
            {
                RightResult = BuildCircuitData(
                    rightLabel, rightExisting,
                    _rightDescBox, _rightBreakerCombo,
                    _rightProtectionCombo, _rightLeakageCombo,
                    _rightPolesCombo);
            }

            Confirmed = true;
            Close();
        }
    }
}
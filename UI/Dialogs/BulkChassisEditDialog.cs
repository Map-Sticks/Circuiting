using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Circuiting.Models;

namespace Circuiting.UI.Dialogs
{
    public class BulkChassisEditDialog : Window
    {
        private CheckBox _applyBreakerCheck;
        private ComboBox _breakerCombo;
        private CheckBox _applyProtectionCheck;
        private ComboBox _protectionCombo;
        private CheckBox _applyLeakageCheck;
        private ComboBox _leakageCombo;
        private CheckBox _applyDescCheck;
        private TextBox _descBox;
        private RadioButton _applyAll;
        private RadioButton _applySpareOnly;
        private RadioButton _applyLeftOnly;
        private RadioButton _applyRightOnly;

        public bool Confirmed { get; private set; }
        public bool ApplyBreaker { get; private set; }
        public string BreakerSize { get; private set; } = string.Empty;
        public bool ApplyProtection { get; private set; }
        public string ProtectionType { get; private set; } = string.Empty;
        public bool ApplyLeakage { get; private set; }
        public string EarthLeakage { get; private set; } = string.Empty;
        public bool ApplyDescription { get; private set; }
        public string Description { get; private set; } = string.Empty;
        public BulkApplyScope Scope { get; private set; } = BulkApplyScope.All;

        public BulkChassisEditDialog(string chassisName)
        {
            Title = $"Bulk Edit — {chassisName}";
            Width = 400;
            Height = 440;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(30, 30, 30));

            StackPanel main = new StackPanel
            {
                Margin = new Thickness(20)
            };

            // Scope
            main.Children.Add(CreateLabel("Apply to:"));

            _applyAll = new RadioButton
            {
                Content = "All circuits",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
                IsChecked = true,
                Margin = new Thickness(0, 0, 0, 3)
            };
            main.Children.Add(_applyAll);

            _applySpareOnly = new RadioButton
            {
                Content = "Spare circuits only",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 3)
            };
            main.Children.Add(_applySpareOnly);

            _applyLeftOnly = new RadioButton
            {
                Content = "Left side only",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 3)
            };
            main.Children.Add(_applyLeftOnly);

            _applyRightOnly = new RadioButton
            {
                Content = "Right side only",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 16)
            };
            main.Children.Add(_applyRightOnly);

            // Fields with checkboxes
            main.Children.Add(
                CreateCheckedCombo("Breaker Size:",
                    CircuitOptions.BreakerSizes,
                    out _applyBreakerCheck,
                    out _breakerCombo));

            main.Children.Add(
                CreateCheckedCombo("Protection:",
                    CircuitOptions.ProtectionTypes,
                    out _applyProtectionCheck,
                    out _protectionCombo));

            main.Children.Add(
                CreateCheckedCombo("Earth Leakage:",
                    CircuitOptions.EarthLeakages,
                    out _applyLeakageCheck,
                    out _leakageCombo));

            // Description with checkbox
            StackPanel descPanel = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 8)
            };

            _applyDescCheck = new CheckBox
            {
                Content = "Description:",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4)
            };
            descPanel.Children.Add(_applyDescCheck);

            _descBox = new TextBox
            {
                FontSize = 12,
                Padding = new Thickness(4, 3, 4, 3),
                Margin = new Thickness(20, 0, 0, 0)
            };
            descPanel.Children.Add(_descBox);
            main.Children.Add(descPanel);

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

            Button applyButton = new Button
            {
                Content = "Apply",
                Width = 80,
                Padding = new Thickness(0, 4, 0, 4),
                Background = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0, 120, 212)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0)
            };
            applyButton.Click += (s, e) => OnApply();
            buttonPanel.Children.Add(applyButton);

            main.Children.Add(buttonPanel);
            Content = main;
        }

        private TextBlock CreateLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };
        }

        private StackPanel CreateCheckedCombo(
            string label,
            string[] options,
            out CheckBox check,
            out ComboBox combo)
        {
            StackPanel panel = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 8)
            };

            check = new CheckBox
            {
                Content = label,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4)
            };
            panel.Children.Add(check);

            combo = new ComboBox
            {
                FontSize = 12,
                Padding = new Thickness(4, 3, 4, 3),
                Margin = new Thickness(20, 0, 0, 0)
            };

            foreach (string opt in options)
            {
                if (string.IsNullOrEmpty(opt)) continue;
                combo.Items.Add(opt);
            }
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;

            panel.Children.Add(combo);
            return panel;
        }

        private void OnApply()
        {
            ApplyBreaker = _applyBreakerCheck.IsChecked == true;
            BreakerSize = ApplyBreaker && _breakerCombo.SelectedItem != null
                ? _breakerCombo.SelectedItem.ToString() ?? string.Empty
                : string.Empty;

            ApplyProtection = _applyProtectionCheck.IsChecked == true;
            ProtectionType = ApplyProtection && _protectionCombo.SelectedItem != null
                ? _protectionCombo.SelectedItem.ToString() ?? string.Empty
                : string.Empty;

            ApplyLeakage = _applyLeakageCheck.IsChecked == true;
            EarthLeakage = ApplyLeakage && _leakageCombo.SelectedItem != null
                ? _leakageCombo.SelectedItem.ToString() ?? string.Empty
                : string.Empty;

            ApplyDescription = _applyDescCheck.IsChecked == true;
            Description = _descBox.Text.Trim();

            if (_applySpareOnly.IsChecked == true)
                Scope = BulkApplyScope.SpareOnly;
            else if (_applyLeftOnly.IsChecked == true)
                Scope = BulkApplyScope.LeftOnly;
            else if (_applyRightOnly.IsChecked == true)
                Scope = BulkApplyScope.RightOnly;
            else
                Scope = BulkApplyScope.All;

            Confirmed = true;
            Close();
        }
    }

    public enum BulkApplyScope
    {
        All,
        SpareOnly,
        LeftOnly,
        RightOnly
    }
}
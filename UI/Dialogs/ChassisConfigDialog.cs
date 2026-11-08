using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Circuiting.Models;
using Circuiting.Services;

namespace Circuiting.UI.Dialogs
{
    public class ChassisConfigDialog : Window
    {
        private TextBox _nameBox;
        private RadioButton _threePhaseRadio;
        private RadioButton _singlePhaseRadio;
        private ComboBox _polesCombo;
        private TextBox _startCircuitBox;
        private TextBox _prefixBox;
        private RadioButton _sepNone;
        private RadioButton _sepDash;
        private RadioButton _sepDot;
        private CheckBox _showPhaseCheck;
        private TextBox _phaseLabelsBox;
        private TextBlock _previewBlock;
        private StackPanel _phaseConfigPanel;

        public ChassisData? Result { get; private set; }
        public bool Confirmed { get; private set; }

        private bool _allowThreePhase;

        public ChassisConfigDialog(
            ChassisData? existing = null,
            int suggestedStart = 1,
            bool isThreePhase = true,
            bool allowThreePhase = true)
        {
            _allowThreePhase = allowThreePhase;
            Title = existing != null ? "Edit Chassis" : "Add Chassis";
            Width = 420;
            Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(30, 30, 30));

            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            StackPanel main = new StackPanel
            {
                Margin = new Thickness(20)
            };

            // ─── Chassis Name ────────────────────────────────────
            main.Children.Add(CreateLabel("Chassis Name:"));

            _nameBox = new TextBox
            {
                Text = existing?.Name ?? "Main",
                FontSize = 13,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 12)
            };
            main.Children.Add(_nameBox);

            // ─── Chassis Type ────────────────────────────────────
            main.Children.Add(CreateLabel("Chassis Type:"));

            bool existingIsThreePhase = existing != null
                ? (existing.PhaseLabels?.Count ?? 0) >= 3
                : isThreePhase;

            StackPanel typePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 12)
            };

            _threePhaseRadio = new RadioButton
            {
                Content = allowThreePhase ? "3 Phase" : "3 Phase (board is single phase)",
                Foreground = Brushes.White,
                FontSize = 13,
                IsChecked = existingIsThreePhase && allowThreePhase,
                IsEnabled = allowThreePhase,
                Margin = new Thickness(0, 0, 16, 0)
            };
            _threePhaseRadio.Checked += (s, e) => OnChassisTypeChanged();

            _singlePhaseRadio = new RadioButton
            {
                Content = "Single Phase",
                Foreground = Brushes.White,
                FontSize = 13,
                IsChecked = !existingIsThreePhase || !allowThreePhase
            };
            _singlePhaseRadio.Checked += (s, e) => OnChassisTypeChanged();

            typePanel.Children.Add(_threePhaseRadio);
            typePanel.Children.Add(_singlePhaseRadio);
            main.Children.Add(typePanel);

            // ─── Max Poles ───────────────────────────────────────
            main.Children.Add(CreateLabel("Max Poles:"));

            _polesCombo = new ComboBox
            {
                FontSize = 13,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 12)
            };

            int[] poleOptions = { 6, 12, 18, 24, 30, 36, 42, 48 };
            foreach (int p in poleOptions)
            {
                _polesCombo.Items.Add(p);
            }

            int currentPoles = existing?.MaxPoles ?? 24;
            int comboIndex = Array.IndexOf(poleOptions, currentPoles);
            _polesCombo.SelectedIndex = comboIndex >= 0 ? comboIndex : 3;
            _polesCombo.SelectionChanged += (s, e) => UpdatePreview();
            main.Children.Add(_polesCombo);

            // ─── Starting Circuit ────────────────────────────────
            main.Children.Add(CreateLabel("Starting Circuit Number:"));

            _startCircuitBox = new TextBox
            {
                Text = (existing?.StartingCircuit ?? suggestedStart)
                    .ToString(),
                FontSize = 13,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 12)
            };
            _startCircuitBox.TextChanged += (s, e) => UpdatePreview();
            main.Children.Add(_startCircuitBox);

            // ─── Prefix ──────────────────────────────────────────
            main.Children.Add(CreateLabel("Circuit Prefix (optional):"));

            _prefixBox = new TextBox
            {
                Text = existing?.Prefix ?? string.Empty,
                FontSize = 13,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 12)
            };
            _prefixBox.TextChanged += (s, e) => UpdatePreview();
            main.Children.Add(_prefixBox);

            // ─── Separator ───────────────────────────────────────
            main.Children.Add(CreateLabel("Separator:"));

            StackPanel sepPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 12)
            };

            string currentSep = existing?.Separator ?? string.Empty;

            _sepNone = new RadioButton
            {
                Content = "None",
                Foreground = Brushes.White,
                FontSize = 13,
                IsChecked = currentSep == string.Empty,
                Margin = new Thickness(0, 0, 16, 0)
            };
            _sepNone.Checked += (s, e) => UpdatePreview();

            _sepDash = new RadioButton
            {
                Content = "Dash ( - )",
                Foreground = Brushes.White,
                FontSize = 13,
                IsChecked = currentSep == "-",
                Margin = new Thickness(0, 0, 16, 0)
            };
            _sepDash.Checked += (s, e) => UpdatePreview();

            _sepDot = new RadioButton
            {
                Content = "Dot ( . )",
                Foreground = Brushes.White,
                FontSize = 13,
                IsChecked = currentSep == "."
            };
            _sepDot.Checked += (s, e) => UpdatePreview();

            sepPanel.Children.Add(_sepNone);
            sepPanel.Children.Add(_sepDash);
            sepPanel.Children.Add(_sepDot);
            main.Children.Add(sepPanel);

            // ─── Phase Config Panel (hideable) ───────────────────
            _phaseConfigPanel = new StackPanel();

            _showPhaseCheck = new CheckBox
            {
                Content = "Show Phase Labels",
                Foreground = Brushes.White,
                FontSize = 13,
                IsChecked = existing?.ShowPhaseLabel
                    ?? existingIsThreePhase,
                Margin = new Thickness(0, 0, 0, 8)
            };
            _showPhaseCheck.Checked += (s, e) => UpdatePreview();
            _showPhaseCheck.Unchecked += (s, e) => UpdatePreview();
            _phaseConfigPanel.Children.Add(_showPhaseCheck);

            _phaseConfigPanel.Children.Add(CreateLabel(
                "Phase Labels (comma separated):"));

            string defaultLabels = existingIsThreePhase
                ? "R,W,B" : "";
            string existingLabels = existing?.PhaseLabels != null
                ? string.Join(",", existing.PhaseLabels)
                : defaultLabels;

            _phaseLabelsBox = new TextBox
            {
                Text = existingLabels,
                FontSize = 13,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 16)
            };
            _phaseLabelsBox.TextChanged += (s, e) => UpdatePreview();
            _phaseConfigPanel.Children.Add(_phaseLabelsBox);

            main.Children.Add(_phaseConfigPanel);

            // Set initial visibility
            _phaseConfigPanel.Visibility = existingIsThreePhase
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

            // ─── Preview ─────────────────────────────────────────
            main.Children.Add(CreateLabel("Preview:"));

            Border previewBorder = new Border
            {
                Background = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(20, 20, 20)),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 16),
                CornerRadius = new CornerRadius(4)
            };

            _previewBlock = new TextBlock
            {
                Foreground = Brushes.White,
                FontFamily = new System.Windows.Media.FontFamily(
                    "Consolas"),
                FontSize = 12
            };

            previewBorder.Child = _previewBlock;
            main.Children.Add(previewBorder);

            // ─── Buttons ─────────────────────────────────────────
            StackPanel buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
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

            Button okButton = new Button
            {
                Content = existing != null ? "Save" : "Add",
                Width = 80,
                Padding = new Thickness(0, 4, 0, 4),
                Background = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0, 120, 212)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            okButton.Click += (s, e) => OnConfirm(existing);
            buttonPanel.Children.Add(okButton);

            main.Children.Add(buttonPanel);

            scroll.Content = main;
            Content = scroll;

            UpdatePreview();
        }

        private void OnChassisTypeChanged()
        {
            bool isThreePhase = _threePhaseRadio.IsChecked == true;

            if (isThreePhase)
            {
                _phaseConfigPanel.Visibility = System.Windows.Visibility.Visible;
                _showPhaseCheck.IsChecked = true;
                if (string.IsNullOrWhiteSpace(_phaseLabelsBox.Text))
                {
                    _phaseLabelsBox.Text = "R,W,B";
                }
            }
            else
            {
                _phaseConfigPanel.Visibility = System.Windows.Visibility.Collapsed;
                _showPhaseCheck.IsChecked = false;
                _phaseLabelsBox.Text = string.Empty;
            }

            UpdatePreview();
        }

        private TextBlock CreateLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 6)
            };
        }

        private string GetSeparator()
        {
            if (_sepDash.IsChecked == true) return "-";
            if (_sepDot.IsChecked == true) return ".";
            return string.Empty;
        }

        private List<string> GetPhaseLabels()
        {
            if (_singlePhaseRadio.IsChecked == true)
                return new List<string>();

            if (string.IsNullOrWhiteSpace(_phaseLabelsBox.Text))
                return new List<string>();

            return _phaseLabelsBox.Text
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }

        private int GetMaxPoles()
        {
            if (_polesCombo.SelectedItem is int poles)
                return poles;
            return 24;
        }

        private int GetStartCircuit()
        {
            if (int.TryParse(_startCircuitBox.Text, out int start)
                && start > 0)
                return start;
            return 1;
        }

        private void UpdatePreview()
        {
            try
            {
                ChassisData temp = new ChassisData
                {
                    MaxPoles = GetMaxPoles(),
                    StartingCircuit = GetStartCircuit(),
                    Prefix = _prefixBox.Text.Trim(),
                    Separator = GetSeparator(),
                    ShowPhaseLabel = _showPhaseCheck.IsChecked == true,
                    PhaseLabels = GetPhaseLabels()
                };

                List<PoleAssignment> assignments =
                    CircuitLabelCalculator.Calculate(temp);

                int previewRows = Math.Min(temp.MaxPoles / 2, 6);
                System.Text.StringBuilder sb =
                    new System.Text.StringBuilder();
                sb.AppendLine("LEFT        RIGHT");

                for (int row = 0; row < previewRows; row++)
                {
                    PoleAssignment left = assignments.First(a =>
                        a.RowIndex == row && a.Side == Side.Left);
                    PoleAssignment right = assignments.First(a =>
                        a.RowIndex == row && a.Side == Side.Right);

                    sb.AppendLine(
                        $"{left.DisplayLabel,-10}  {right.DisplayLabel}");
                }

                if (temp.MaxPoles / 2 > 6)
                    sb.AppendLine("...");

                _previewBlock.Text = sb.ToString();
            }
            catch
            {
                _previewBlock.Text = "Invalid configuration";
            }
        }

        private void OnConfirm(ChassisData? existing)
        {
            if (string.IsNullOrWhiteSpace(_nameBox.Text))
            {
                MessageBox.Show("Please enter a chassis name.");
                return;
            }

            Result = new ChassisData
            {
                ChassisGuid = existing?.ChassisGuid ?? Guid.NewGuid(),
                Name = _nameBox.Text.Trim(),
                MaxPoles = GetMaxPoles(),
                StartingCircuit = GetStartCircuit(),
                Prefix = _prefixBox.Text.Trim(),
                Separator = GetSeparator(),
                ShowPhaseLabel = _showPhaseCheck.IsChecked == true,
                PhaseLabels = GetPhaseLabels(),
                Circuits = existing?.Circuits ?? new List<CircuitData>()
            };

            Confirmed = true;
            Close();
        }
    }
}
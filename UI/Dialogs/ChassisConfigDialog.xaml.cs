using System.Text;
using System.Windows;
using System.Windows.Controls;
using Circuiting.Models;
using Circuiting.Services;
using Circuiting.ViewModels;

namespace Circuiting.UI.Dialogs
{
    public partial class ChassisConfigDialog : Window
    {
        private readonly BoardData? _board;
        private readonly ChassisData? _existing;
        private string _customSeparator = string.Empty;
        private bool _initializing = true;

        public ChassisConfigDialog(ChassisData? existing = null, int suggestedStart = 1,
            bool isThreePhase = true, bool allowThreePhase = true, BoardData? board = null)
        {
            _board = board;
            _existing = existing;
            InitializeComponent();
            Title = existing != null ? "Edit Chassis" : "Add Chassis";
            ConfirmButton.Content = existing != null ? "Save" : "Add";
            NameBox.Text = existing?.Name ?? "Main";

            bool threePhase = existing != null ? existing.PhaseLabels.Count >= 3 : isThreePhase && allowThreePhase;
            ThreePhaseRadio.Content = allowThreePhase ? "3 Phase" : "3 Phase (board is single phase)";
            ThreePhaseRadio.IsEnabled = allowThreePhase;
            ThreePhaseRadio.IsChecked = threePhase;
            SinglePhaseRadio.IsChecked = !threePhase;
            PhaseConfigPanel.Visibility = threePhase
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            RemoveLegacyPhasesCheck.Visibility = !threePhase && ChassisFieldOptions.HasLegacyPhaseLabels(existing?.PhaseLabels)
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            var poleOptions = new List<int> { 6, 12, 18, 24, 30, 36, 42, 48 };
            int currentPoles = existing?.MaxPoles ?? 24;
            if (!poleOptions.Contains(currentPoles)) poleOptions.Add(currentPoles);
            poleOptions.Sort();
            PolesCombo.ItemsSource = poleOptions;
            PolesCombo.SelectedItem = currentPoles;
            StartCircuitBox.Text = (existing?.StartingCircuit ?? suggestedStart).ToString();
            PrefixBox.Text = existing?.Prefix ?? string.Empty;
            string separator = existing?.Separator ?? string.Empty;
            SepNone.IsChecked = separator == string.Empty;
            SepDash.IsChecked = separator == "-";
            SepDot.IsChecked = separator == ".";
            if (separator != string.Empty && separator != "-" && separator != ".")
            {
                _customSeparator = separator;
                SepCustom.Content = $"Custom ({separator})";
                SepCustom.Visibility = System.Windows.Visibility.Visible;
                SepCustom.IsChecked = true;
            }
            ShowPhaseCheck.IsChecked = existing?.ShowPhaseLabel ?? threePhase;
            ContinuePhaseCheck.IsChecked = existing?.ContinuePhaseFromPrevious ?? false;
            PhaseLabelsBox.Text = existing != null
                ? string.Join(",", existing.PhaseLabels) : threePhase ? "R,W,B" : string.Empty;
            _initializing = false;
            UpdatePreview();
        }

        public ChassisData? Result { get; private set; }
        public bool Confirmed { get; private set; }

        private void ChassisType_Checked(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;
            bool threePhase = ThreePhaseRadio.IsChecked == true;
            PhaseConfigPanel.Visibility = threePhase
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            RemoveLegacyPhasesCheck.Visibility = !threePhase && ChassisFieldOptions.HasLegacyPhaseLabels(_existing?.PhaseLabels)
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            ShowPhaseCheck.IsChecked = threePhase;
            if (threePhase && string.IsNullOrWhiteSpace(PhaseLabelsBox.Text))
                PhaseLabelsBox.Text = "R,W,B";
            else if (!threePhase)
                PhaseLabelsBox.Text = string.Empty;
            UpdatePreview();
        }

        private void PreviewChanged(object sender, RoutedEventArgs e) => UpdatePreview();
        private void PreviewSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();
        private void PreviewTextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

        private string GetSeparator() =>
            SepDash.IsChecked == true ? "-" : SepDot.IsChecked == true ? "." :
            SepCustom.IsChecked == true ? _customSeparator : string.Empty;

        private List<string> GetPhaseLabels() => ChassisFieldOptions.PhaseLabels(
            SinglePhaseRadio.IsChecked == true, PhaseLabelsBox.Text, _existing?.PhaseLabels,
            RemoveLegacyPhasesCheck.IsChecked == true);

        private int GetMaxPoles() => PolesCombo.SelectedItem is int poles
            ? poles : throw new InvalidOperationException("Select a pole count.");

        private void UpdatePreview()
        {
            if (_initializing) return;
            if (!int.TryParse(StartCircuitBox.Text, out int start) || start < 1)
            {
                PreviewBlock.Text = "Enter a positive starting circuit number.";
                return;
            }

            try
            {
                var temp = new ChassisData
                {
                    MaxPoles = GetMaxPoles(),
                    StartingCircuit = start,
                    Prefix = PrefixBox.Text.Trim(),
                    Separator = GetSeparator(),
                    ShowPhaseLabel = ShowPhaseCheck.IsChecked == true,
                    ContinuePhaseFromPrevious = ContinuePhaseCheck.IsChecked == true,
                    PhaseLabels = GetPhaseLabels()
                };
                List<PoleAssignment> assignments;
                if (_board == null)
                {
                    assignments = CircuitLabelCalculator.Calculate(temp);
                }
                else
                {
                    var previewBoard = new BoardData { Chassis = _board.Chassis.ToList() };
                    int index = _existing == null ? -1 : _board.Chassis.IndexOf(_existing);
                    if (index < 0) previewBoard.Chassis.Add(temp);
                    else previewBoard.Chassis[index] = temp;
                    assignments = CircuitLabelCalculator.Calculate(previewBoard, temp);
                }

                int previewRows = Math.Min(temp.MaxPoles / 2, 6);
                var text = new StringBuilder();
                text.AppendLine("LEFT        RIGHT");
                for (int row = 0; row < previewRows; row++)
                    text.AppendLine($"{assignments[row * 2].DisplayLabel,-10}  {assignments[row * 2 + 1].DisplayLabel}");
                if (temp.MaxPoles / 2 > 6) text.AppendLine("...");
                PreviewBlock.Text = text.ToString();
            }
            catch (Exception ex) when (ex is ArgumentException || ex is OverflowException ||
                                       ex is InvalidOperationException)
            {
                PreviewBlock.Text = $"Invalid configuration: {ex.Message}";
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show("Please enter a chassis name.");
                return;
            }
            if (!int.TryParse(StartCircuitBox.Text, out int start) || start < 1)
            {
                MessageBox.Show("Please enter a positive starting circuit number.");
                return;
            }
            List<string> phaseLabels = GetPhaseLabels();
            if (ThreePhaseRadio.IsChecked == true && phaseLabels.Count < 3)
            {
                MessageBox.Show("Please enter at least three phase labels.");
                return;
            }

            var candidate = new ChassisData
            {
                ChassisGuid = _existing?.ChassisGuid ?? Guid.NewGuid(),
                Name = NameBox.Text.Trim(),
                MaxPoles = GetMaxPoles(),
                StartingCircuit = start,
                Prefix = PrefixBox.Text.Trim(),
                Separator = GetSeparator(),
                ShowPhaseLabel = ShowPhaseCheck.IsChecked == true,
                ContinuePhaseFromPrevious = ContinuePhaseCheck.IsChecked == true,
                PhaseLabels = phaseLabels,
                Circuits = _existing?.Circuits ?? new List<CircuitData>()
            };
            try
            {
                CircuitLabelCalculator.Calculate(candidate);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is OverflowException)
            {
                MessageBox.Show(ex.Message, "Invalid Chassis", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Result = candidate;
            Confirmed = true;
            DialogResult = true;
        }
    }
}

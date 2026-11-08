using System.Windows;
using System.Windows.Controls;
using Circuiting.Models;
using Circuiting.Services;

namespace Circuiting.UI.Dialogs
{
    public partial class BulkChassisEditDialog : Window
    {
        public BulkChassisEditDialog(string chassisName)
        {
            InitializeComponent();
            Title = $"Bulk Edit — {chassisName}";
            SetOptions(BreakerCombo, CircuitOptions.BreakerSizes);
            SetOptions(ProtectionCombo, CircuitOptions.ProtectionTypes);
            SetOptions(LeakageCombo, CircuitOptions.EarthLeakages);
            SetOptions(TripCurveCombo, CircuitOptions.TripCurves);
        }

        private const string NoneOption = "(none)";
        private readonly bool _allowNone;

        /// <summary>Edits the selected circuits: no scope options, shared values pre-filled, "(none)" clears a value.</summary>
        public BulkChassisEditDialog(IReadOnlyCollection<CircuitData> circuits)
        {
            InitializeComponent();
            _allowNone = true;
            Title = $"Edit {circuits.Count} Selected Circuits";
            ScopePanel.Visibility = System.Windows.Visibility.Collapsed;
            IntroText.Visibility = System.Windows.Visibility.Visible;
            IntroText.Text = $"Tick the properties to set on all {circuits.Count} selected circuits. " +
                "Unticked properties are left unchanged. Values shared by every selected circuit are pre-filled.";
            SetOptions(BreakerCombo, CircuitOptions.BreakerSizes,
                BulkCircuitEdit.Common(circuits, c => c.BreakerSize));
            SetOptions(ProtectionCombo, CircuitOptions.ProtectionTypes,
                BulkCircuitEdit.Common(circuits, c => c.ProtectionType));
            SetOptions(LeakageCombo, CircuitOptions.EarthLeakages,
                BulkCircuitEdit.Common(circuits, c => c.EarthLeakage));
            SetOptions(TripCurveCombo, CircuitOptions.TripCurves,
                BulkCircuitEdit.Common(circuits, c => c.TripCurve));
            TripSettingBox.Text = BulkCircuitEdit.Common(circuits, c => c.TripSetting) ?? string.Empty;
            DescriptionBox.Text = BulkCircuitEdit.Common(circuits, c => c.Description) ?? string.Empty;
        }

        public bool Confirmed { get; private set; }
        public bool ApplyBreaker { get; private set; }
        public string BreakerSize { get; private set; } = string.Empty;
        public bool ApplyProtection { get; private set; }
        public string ProtectionType { get; private set; } = string.Empty;
        public bool ApplyLeakage { get; private set; }
        public string EarthLeakage { get; private set; } = string.Empty;
        public bool ApplyTripCurve { get; private set; }
        public string TripCurve { get; private set; } = string.Empty;
        public bool ApplyTripSetting { get; private set; }
        public string TripSetting { get; private set; } = string.Empty;
        public bool ApplyDescription { get; private set; }
        public string Description { get; private set; } = string.Empty;
        public BulkApplyScope Scope { get; private set; } = BulkApplyScope.All;

        private static void SetOptions(ComboBox combo, IEnumerable<string> options)
        {
            combo.ItemsSource = options.Where(option => !string.IsNullOrEmpty(option)).ToList();
            combo.SelectedIndex = 0;
        }

        private static void SetOptions(ComboBox combo, IEnumerable<string> options, string? common)
        {
            List<string> items = options.Select(o => string.IsNullOrEmpty(o) ? NoneOption : o).ToList();
            if (!string.IsNullOrEmpty(common) && !items.Contains(common!)) items.Add(common!);
            combo.ItemsSource = items;
            combo.SelectedItem = common == null ? items.FirstOrDefault(i => i != NoneOption)
                : common.Length == 0 ? NoneOption : common;
        }

        private string Value(ComboBox combo)
        {
            string value = (string)combo.SelectedItem;
            return _allowNone && value == NoneOption ? string.Empty : value;
        }

        /// <summary>The ticked values as an edit (null for unticked properties).</summary>
        public BulkCircuitEdit Edit => new BulkCircuitEdit
        {
            Description = ApplyDescription ? Description : null,
            BreakerSize = ApplyBreaker ? BreakerSize : null,
            ProtectionType = ApplyProtection ? ProtectionType : null,
            EarthLeakage = ApplyLeakage ? EarthLeakage : null,
            TripCurve = ApplyTripCurve ? TripCurve : null,
            TripSetting = ApplyTripSetting ? TripSetting : null
        };

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if ((BreakerCheck.IsChecked == true && BreakerCombo.SelectedItem is not string) ||
                (ProtectionCheck.IsChecked == true && ProtectionCombo.SelectedItem is not string) ||
                (LeakageCheck.IsChecked == true && LeakageCombo.SelectedItem is not string) ||
                (TripCurveCheck.IsChecked == true && TripCurveCombo.SelectedItem is not string))
            {
                MessageBox.Show("Select a value for every checked field.", "Bulk Edit",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ApplyBreaker = BreakerCheck.IsChecked == true;
            BreakerSize = ApplyBreaker ? Value(BreakerCombo) : string.Empty;
            ApplyProtection = ProtectionCheck.IsChecked == true;
            ProtectionType = ApplyProtection ? Value(ProtectionCombo) : string.Empty;
            ApplyLeakage = LeakageCheck.IsChecked == true;
            EarthLeakage = ApplyLeakage ? Value(LeakageCombo) : string.Empty;
            ApplyTripCurve = TripCurveCheck.IsChecked == true;
            TripCurve = ApplyTripCurve ? Value(TripCurveCombo) : string.Empty;
            ApplyTripSetting = TripSettingCheck.IsChecked == true;
            TripSetting = TripSettingBox.Text.Trim();
            ApplyDescription = DescriptionCheck.IsChecked == true;
            Description = DescriptionBox.Text.Trim();
            Scope = SpareRadio.IsChecked == true ? BulkApplyScope.SpareOnly
                : LeftRadio.IsChecked == true ? BulkApplyScope.LeftOnly
                : RightRadio.IsChecked == true ? BulkApplyScope.RightOnly
                : BulkApplyScope.All;
            Confirmed = true;
            DialogResult = true;
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

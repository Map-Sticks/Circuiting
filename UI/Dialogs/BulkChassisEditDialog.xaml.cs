using System.Windows;
using System.Windows.Controls;
using Circuiting.Models;

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
        }

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

        private static void SetOptions(ComboBox combo, IEnumerable<string> options)
        {
            combo.ItemsSource = options.Where(option => !string.IsNullOrEmpty(option)).ToList();
            combo.SelectedIndex = 0;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if ((BreakerCheck.IsChecked == true && BreakerCombo.SelectedItem is not string) ||
                (ProtectionCheck.IsChecked == true && ProtectionCombo.SelectedItem is not string) ||
                (LeakageCheck.IsChecked == true && LeakageCombo.SelectedItem is not string))
            {
                MessageBox.Show("Select a value for every checked field.", "Bulk Edit",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ApplyBreaker = BreakerCheck.IsChecked == true;
            BreakerSize = ApplyBreaker ? (string)BreakerCombo.SelectedItem : string.Empty;
            ApplyProtection = ProtectionCheck.IsChecked == true;
            ProtectionType = ApplyProtection ? (string)ProtectionCombo.SelectedItem : string.Empty;
            ApplyLeakage = LeakageCheck.IsChecked == true;
            EarthLeakage = ApplyLeakage ? (string)LeakageCombo.SelectedItem : string.Empty;
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

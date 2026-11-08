using System.Windows;
using Circuiting.Models;

namespace Circuiting.UI.Dialogs
{
    public sealed class CircuitDestination
    {
        public CircuitData Circuit { get; set; } = null!;
        public string DisplayName { get; set; } = string.Empty;
    }

    public partial class ReassignFixturesDialog : Window
    {
        public ReassignFixturesDialog(IEnumerable<CircuitDestination> destinations)
        {
            InitializeComponent();
            DestinationCombo.ItemsSource = destinations.ToList();
            DestinationCombo.SelectedIndex = 0;
        }

        public CircuitData? Destination => (DestinationCombo.SelectedItem as CircuitDestination)?.Circuit;

        private void Move_Click(object sender, RoutedEventArgs e)
        {
            if (Destination == null)
            {
                MessageBox.Show("Select a destination circuit.", "Reassign Fixtures",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }
    }
}

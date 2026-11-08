using System.Windows;
using Circuiting.Models;

namespace Circuiting.UI.Dialogs
{
    public sealed class CircuitMoveDestination
    {
        public BoardData Board { get; set; } = null!;
        public ChassisData Chassis { get; set; } = null!;
        public int PoleIndex { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }

    public partial class MoveCircuitDialog : Window
    {
        public MoveCircuitDialog(IEnumerable<CircuitMoveDestination> destinations)
        {
            InitializeComponent();
            DestinationCombo.ItemsSource = destinations.ToList();
            DestinationCombo.SelectedIndex = 0;
        }

        public CircuitMoveDestination? Destination =>
            DestinationCombo.SelectedItem as CircuitMoveDestination;

        private void Move_Click(object sender, RoutedEventArgs e)
        {
            if (Destination == null)
            {
                MessageBox.Show("Select a destination position.", "Move Circuit",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }
    }
}

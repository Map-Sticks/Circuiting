using System.Windows;
using System.Windows.Controls;
using Circuiting.ViewModels;

namespace Circuiting.UI.Dialogs
{
    public partial class MoveCircuitDialog : Window
    {
        private readonly CircuitMovePicker _picker;
        private readonly bool _ready;

        public MoveCircuitDialog(CircuitMovePicker picker)
        {
            InitializeComponent();
            _picker = picker;
            CircuitText.Text = picker.CircuitSummary;
            _ready = true;
            RefreshBoards();
            BoardList.SelectedItem = BoardList.Items.OfType<MoveBoardOption>().FirstOrDefault(b => b.IsSource)
                ?? BoardList.Items.OfType<MoveBoardOption>().FirstOrDefault();
            ChassisList.SelectedItem = ChassisList.Items.OfType<MoveChassisOption>().FirstOrDefault(c => c.IsSource)
                ?? ChassisList.SelectedItem;
            Loaded += (_, _) => BoardFilterBox.Focus();
        }

        public MovePositionOption? Destination => PositionList.SelectedItem as MovePositionOption;

        private void RefreshBoards()
        {
            object? selected = BoardList.SelectedItem;
            List<MoveBoardOption> boards = _picker.FilterBoards(BoardFilterBox.Text);
            BoardList.ItemsSource = boards;
            BoardList.SelectedItem = selected is MoveBoardOption board && boards.Contains(board)
                ? board : boards.FirstOrDefault();
        }

        private void RefreshPositions()
        {
            if (ChassisList.SelectedItem is not MoveChassisOption chassis)
            {
                PositionList.ItemsSource = null;
                UpdateSelection();
                return;
            }
            object? selected = PositionList.SelectedItem;
            List<MovePositionOption> positions = _picker.PositionsFor(chassis.Chassis,
                AvailableOnlyCheck.IsChecked == true, PositionFilterBox.Text);
            PositionList.ItemsSource = positions;
            if (selected is MovePositionOption position && positions.Contains(position))
                PositionList.SelectedItem = position;
            UpdateSelection();
        }

        private void UpdateSelection()
        {
            MovePositionOption? destination = Destination;
            MoveButton.IsEnabled = destination?.IsAvailable == true;
            if (destination != null)
                SelectionText.Text = $"Move to {destination.Summary}" +
                    (destination.ReplacesSpare ? " — replaces spare" : string.Empty);
            else if (ChassisList.SelectedItem is MoveChassisOption { Available: 0 })
                SelectionText.Text = "This chassis has no positions that fit this circuit.";
            else
                SelectionText.Text = "Select a board, a chassis and then a target position.";
        }

        private void BoardFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_ready) RefreshBoards();
        }

        private void BoardList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready) return;
            List<MoveChassisOption> chassis = BoardList.SelectedItem is MoveBoardOption board
                ? _picker.ChassisFor(board.Board) : new List<MoveChassisOption>();
            ChassisList.ItemsSource = chassis;
            ChassisList.SelectedItem = chassis.FirstOrDefault(c => c.Available > 0) ?? chassis.FirstOrDefault();
        }

        private void ChassisList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_ready) RefreshPositions();
        }

        private void PositionFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (_ready) RefreshPositions();
        }

        private void PositionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_ready) UpdateSelection();
        }

        private void PositionList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (Destination?.IsAvailable == true) DialogResult = true;
        }

        private void Move_Click(object sender, RoutedEventArgs e)
        {
            if (Destination?.IsAvailable != true)
            {
                MessageBox.Show("Select an available target position.", "Move Circuit",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }
    }
}

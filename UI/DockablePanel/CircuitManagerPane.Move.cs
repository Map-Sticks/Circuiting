using System.Windows;
using System.Windows.Input;
using Circuiting.Models;
using Circuiting.Services;
using Circuiting.UI.Dialogs;
using Circuiting.ViewModels;
using Grid = System.Windows.Controls.Grid;

namespace Circuiting.UI.DockablePanel
{
    public partial class CircuitManagerPane
    {
        private sealed class CircuitDrag
        {
            public ProjectData Project { get; set; } = null!;
            public BoardData Board { get; set; } = null!;
            public ChassisData Chassis { get; set; } = null!;
            public CircuitData Circuit { get; set; } = null!;
        }

        private CircuitDrag? _circuitDrag;
        private System.Windows.Point _dragStart;

        private void OnMoveCircuit(BoardData board, ChassisData chassis, CircuitData circuit)
        {
            if (!IsCurrentTarget(board, chassis, circuit)) return;
            ProjectData project = _project;
            var picker = new CircuitMovePicker(_boards, board, chassis, circuit);
            if (picker.TotalAvailable == 0)
            {
                MessageBox.Show("There are no spare or space positions that fit this circuit.",
                    "Move Circuit", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dialog = new MoveCircuitDialog(picker);
            if (dialog.ShowDialog() != true || dialog.Destination == null) return;
            if (!IsCurrentProject(project)) return;
            MovePositionOption destination = dialog.Destination;
            MoveCircuit(board, chassis, circuit, destination.Board, destination.Chassis, destination.Position.PoleIndex);
        }

        private bool MoveCircuit(BoardData board, ChassisData chassis, CircuitData circuit,
            BoardData targetBoard, ChassisData targetChassis, int poleIndex)
        {
            if (!IsCurrentTarget(board, chassis, circuit) || !IsCurrentTarget(targetBoard, targetChassis)) return false;
            string? error = CircuitLayout.GetMoveError(chassis, circuit, targetBoard, targetChassis, poleIndex);
            if (error != null)
            {
                MessageBox.Show(error, "Cannot Move Circuit", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (ReferenceEquals(chassis, targetChassis) && circuit.PoleIndex == poleIndex) return false;
            ProjectData project = _project;
            List<CircuitData> replaced = CircuitLayout.GetSparesReplacedByMove(circuit, targetChassis, poleIndex)
                .Where(CircuitLayout.HasDetails).ToList();
            if (replaced.Count > 0)
            {
                MessageBoxResult confirm = MessageBox.Show(
                    $"Moving {circuit.DisplayLabel} will replace these spare positions and delete their details:\n" +
                    string.Join("\n", replaced.Select(c => "  • " + CircuitLayout.SpareSummary(c))) +
                    "\n\nContinue?", "Replace Spare", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes || !IsCurrentProject(project)) return false;
            }
            PushUndoState();
            CircuitLayout.Move(chassis, circuit, targetBoard, targetChassis, poleIndex);
            SaveAndRefresh();
            return true;
        }

        private static PoleAssignment PositionAt(Grid grid, CircuitRowViewModel row, System.Windows.Point point)
        {
            double leftWidth = grid.ColumnDefinitions.Take(4).Sum(column => column.ActualWidth);
            double dividerWidth = grid.ColumnDefinitions[4].ActualWidth;
            return point.X < leftWidth + dividerWidth / 2 ? row.Left : row.Right;
        }

        private void CircuitRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _circuitDrag = null;
            if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0 ||
                sender is not Grid grid || grid.DataContext is not CircuitRowViewModel row) return;
            e.Handled = true;
            PoleAssignment position = PositionAt(grid, row, e.GetPosition(grid));
            CircuitData? circuit = position.Side == Side.Left
                ? row.LeftConsumedInfo?.Parent ?? row.LeftCircuit
                : row.RightConsumedInfo?.Parent ?? row.RightCircuit;
            if (circuit == null)
            {
                StatusLabel.Text = "Choose an existing circuit to move.";
                return;
            }
            if (!IsCurrentTarget(row.Board, row.Chassis, circuit)) return;
            _dragStart = e.GetPosition(this);
            _circuitDrag = new CircuitDrag
            {
                Project = _project, Board = row.Board, Chassis = row.Chassis, Circuit = circuit
            };
        }

        private void CircuitRow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_circuitDrag == null) return;
            _circuitDrag = null;
            e.Handled = true;
        }

        private void CircuitRow_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            CircuitDrag? drag = _circuitDrag;
            if (drag == null) return;
            if (e.LeftButton != MouseButtonState.Pressed || (Keyboard.Modifiers & ModifierKeys.Alt) == 0)
            {
                _circuitDrag = null;
                return;
            }
            System.Windows.Point point = e.GetPosition(this);
            if (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _circuitDrag = null;
            e.Handled = true;
            if (!IsCurrentProject(drag.Project)) return;
            DragDrop.DoDragDrop((Grid)sender, new DataObject(typeof(CircuitDrag), drag), DragDropEffects.Move);
        }

        private void CircuitRow_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            if (e.Data.GetData(typeof(CircuitDrag)) is not CircuitDrag drag ||
                sender is not Grid grid || grid.DataContext is not CircuitRowViewModel row) return;
            if (!_dataLoaded || _requestPending || !ReferenceEquals(drag.Project, _project)) return;
            PoleAssignment position = PositionAt(grid, row, e.GetPosition(grid));
            string? error = CircuitLayout.GetMoveError(drag.Chassis, drag.Circuit,
                row.Board, row.Chassis, position.PoleIndex);
            bool replacesSpare = error == null && CircuitLayout.GetSparesReplacedByMove(drag.Circuit, row.Chassis,
                position.PoleIndex).Any(CircuitLayout.HasDetails);
            StatusLabel.Text = error ?? $"Move to {row.Board.DisplayName} / {row.Chassis.Name} / " +
                $"{position.DisplayLabel} ({position.Side}, row {position.RowIndex + 1})" +
                (replacesSpare ? " — replaces spare" : string.Empty);
            if (error == null) e.Effects = DragDropEffects.Move;
        }

        private void CircuitRow_Drop(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            if (e.Data.GetData(typeof(CircuitDrag)) is not CircuitDrag drag ||
                sender is not Grid grid || grid.DataContext is not CircuitRowViewModel row) return;
            if (!_dataLoaded || _requestPending || !ReferenceEquals(drag.Project, _project)) return;
            PoleAssignment position = PositionAt(grid, row, e.GetPosition(grid));
            BoardData targetBoard = row.Board;
            ChassisData targetChassis = row.Chassis;
            if (CircuitLayout.GetMoveError(drag.Chassis, drag.Circuit, targetBoard, targetChassis,
                    position.PoleIndex) == null)
                e.Effects = DragDropEffects.Move;
            // Defer until DoDragDrop returns: the move rebuilds the tree and may show dialogs.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsCurrentProject(drag.Project)) return;
                MoveCircuit(drag.Board, drag.Chassis, drag.Circuit, targetBoard, targetChassis, position.PoleIndex);
            }));
        }

        private void BoardTree_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(CircuitDrag))) return;
            DependencyObject? current = e.OriginalSource as DependencyObject;
            while (current != null && current is not System.Windows.Controls.TreeViewItem)
                current = current is System.Windows.Media.Visual
                    ? System.Windows.Media.VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            if (current is System.Windows.Controls.TreeViewItem { IsExpanded: false, HasItems: true } item)
                item.IsExpanded = true;
        }

        private void BoardTree_DragOver(object sender, DragEventArgs e)
        {
            if (e.Handled || !e.Data.GetDataPresent(typeof(CircuitDrag))) return;
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
    }
}

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
            /// <summary>All selected circuits when dragging a multi-selection; null for a single circuit.</summary>
            public List<CircuitRef>? Bulk { get; set; }
        }

        private sealed class FixtureDrag
        {
            public ProjectData Project { get; set; } = null!;
            public List<FixtureRef> Fixtures { get; set; } = null!;
        }

        private FixtureDrag? _fixtureDrag;
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

        // ─── Move Element (fixture) ──────────────────────────

        private void OnMoveFixture(BoardData board, ChassisData chassis, CircuitData circuit, CircuitedFixture fixture)
        {
            if (!IsCurrentTarget(board, chassis, circuit) || !circuit.Fixtures.Contains(fixture)) return;
            OnMoveFixtures(new List<FixtureRef>
            {
                new FixtureRef { Board = board, Chassis = chassis, Circuit = circuit, Fixture = fixture }
            });
        }

        private void OnMoveFixtures(List<FixtureRef> fixtures)
        {
            if (fixtures.Count == 0 || !fixtures.All(IsCurrentFixture)) return;
            ProjectData project = _project;
            CircuitMovePicker picker;
            try
            {
                picker = new CircuitMovePicker(_boards, fixtures);
            }
            catch (System.IO.InvalidDataException ex)
            {
                MessageBox.Show(ex.Message, "Move Element", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var dialog = new MoveCircuitDialog(picker);
            if (dialog.ShowDialog() != true || dialog.Destination == null) return;
            if (!IsCurrentProject(project)) return;
            MovePositionOption destination = dialog.Destination;
            MoveFixtures(fixtures, destination.Board, destination.Chassis, destination.Position.PoleIndex);
        }

        private bool IsCurrentFixture(FixtureRef fixture) =>
            IsCurrentTarget(fixture.Board, fixture.Chassis, fixture.Circuit) &&
            fixture.Circuit.Fixtures.Contains(fixture.Fixture);

        private bool MoveFixtures(List<FixtureRef> fixtures, BoardData targetBoard, ChassisData targetChassis,
            int poleIndex)
        {
            if (!IsCurrentTarget(targetBoard, targetChassis)) return false;
            if (!fixtures.All(IsCurrentFixture))
            {
                MessageBox.Show("An element is no longer assigned to its circuit. Refresh and try again.",
                    "Move Element", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            FixtureMoveTarget target;
            try
            {
                target = FixtureMover.GetTarget(targetBoard, targetChassis, null, poleIndex);
            }
            catch (System.IO.InvalidDataException ex)
            {
                MessageBox.Show(ex.Message, "Cannot Move Element", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (fixtures.All(f => ReferenceEquals(f.Circuit, target.Circuit)))
            {
                StatusLabel.Text = fixtures.Count == 1
                    ? "The element is already on that circuit." : "The elements are already on that circuit.";
                return false;
            }

            ProjectData project = _project;
            CircuitData? details = null;
            if (target.CreatesCircuit)
            {
                string name = fixtures.Count > 1 ? $"{fixtures.Count} elements"
                    : string.IsNullOrWhiteSpace(fixtures[0].Fixture.FamilyName) ? "The element"
                    : fixtures[0].Fixture.FamilyName;
                string intro = target.Kind == FixtureMoveKind.FillSpare
                    ? $"{name} will be moved onto this spare, making it a circuit. Its saved details are shown; amend them if needed."
                    : $"{name} will be moved onto this space, creating a new circuit. Enter its details (all optional).";
                var newCircuit = new NewCircuitDialog($"{targetBoard.DisplayName} / {targetChassis.Name}",
                    target.Position.DisplayLabel, target.Circuit, intro);
                if (newCircuit.ShowDialog() != true || newCircuit.Result == null) return false;
                if (!IsCurrentProject(project)) return false;
                details = newCircuit.Result;
            }

            PushUndoState();
            CircuitData destination = FixtureMover.MoveMany(fixtures, targetBoard, targetChassis, poleIndex, details);
            SaveAndRefresh(new[] { targetBoard.BoardGuid, targetChassis.ChassisGuid, destination.CircuitGuid });
            return true;
        }

        private static FixtureTreeNode? FixtureAt(object sender) =>
            sender is FrameworkElement { DataContext: FixtureTreeNode node } ? node : null;

        private void FixtureRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _fixtureDrag = null;
            if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0 || FixtureAt(sender) is not FixtureTreeNode node) return;
            e.Handled = true;
            if (!IsCurrentTarget(node.Board, node.Chassis, node.Circuit) ||
                !node.Circuit.Fixtures.Contains(node.Fixture)) return;
            List<FixtureRef> fixtures = Selection.Contains(TreeSelectionKind.Elements, node.Fixture.FixtureGuid) &&
                Selection.Count > 1
                ? BulkCircuitOperations.ResolveFixtures(_boards, Selection.Keys.ToList())
                : new List<FixtureRef>
                {
                    new FixtureRef { Board = node.Board, Chassis = node.Chassis, Circuit = node.Circuit, Fixture = node.Fixture }
                };
            _dragStart = e.GetPosition(this);
            _fixtureDrag = new FixtureDrag { Project = _project, Fixtures = fixtures };
        }

        private void FixtureRow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_fixtureDrag == null) return;
            _fixtureDrag = null;
            e.Handled = true;
        }

        private void FixtureRow_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            FixtureDrag? drag = _fixtureDrag;
            if (drag == null) return;
            if (e.LeftButton != MouseButtonState.Pressed || (Keyboard.Modifiers & ModifierKeys.Alt) == 0)
            {
                _fixtureDrag = null;
                return;
            }
            System.Windows.Point point = e.GetPosition(this);
            if (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _fixtureDrag = null;
            e.Handled = true;
            if (!IsCurrentProject(drag.Project)) return;
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(FixtureDrag), drag), DragDropEffects.Move);
        }

        /// <summary>Resolves an element drop target: a circuit row side, or an element row (its circuit).</summary>
        private static (BoardData Board, ChassisData Chassis, int PoleIndex)? FixtureDropTarget(object sender, DragEventArgs e)
        {
            if (sender is Grid { DataContext: CircuitRowViewModel row } grid)
                return (row.Board, row.Chassis, PositionAt(grid, row, e.GetPosition(grid)).PoleIndex);
            if (FixtureAt(sender) is FixtureTreeNode node && node.Circuit.PoleIndex.HasValue)
                return (node.Board, node.Chassis, node.Circuit.PoleIndex.Value);
            return null;
        }

        private string? DescribeFixtureDrop(FixtureDrag drag, BoardData board, ChassisData chassis, int poleIndex,
            out bool allowed)
        {
            allowed = false;
            if (!drag.Fixtures.All(IsCurrentFixture) || !IsCurrentTarget(board, chassis)) return null;
            FixtureMoveTarget target;
            try
            {
                target = FixtureMover.GetTarget(board, chassis, null, poleIndex);
            }
            catch (System.IO.InvalidDataException ex)
            {
                return ex.Message;
            }
            string what = drag.Fixtures.Count == 1 ? "element" : $"{drag.Fixtures.Count} elements";
            if (drag.Fixtures.All(f => ReferenceEquals(f.Circuit, target.Circuit)))
                return $"The {what} {(drag.Fixtures.Count == 1 ? "is" : "are")} already on this circuit.";
            allowed = true;
            string where = $"{board.DisplayName} / {chassis.Name} / {target.Position.DisplayLabel}";
            return target.Kind switch
            {
                FixtureMoveKind.NewCircuit => $"Move {what} to {where} — creates a new circuit",
                FixtureMoveKind.FillSpare => $"Move {what} to {where} — spare becomes a circuit",
                _ => $"Move {what} to {target.Circuit?.DisplayLabel ?? where} ({board.DisplayName} / {chassis.Name})"
            };
        }

        private bool HandleFixtureDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(FixtureDrag)) is not FixtureDrag drag) return false;
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            if (!_dataLoaded || _requestPending || !ReferenceEquals(drag.Project, _project)) return true;
            var target = FixtureDropTarget(sender, e);
            if (target == null) return true;
            StatusLabel.Text = DescribeFixtureDrop(drag, target.Value.Board, target.Value.Chassis,
                target.Value.PoleIndex, out bool allowed) ?? StatusLabel.Text;
            if (allowed) e.Effects = DragDropEffects.Move;
            return true;
        }

        private bool HandleFixtureDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(FixtureDrag)) is not FixtureDrag drag) return false;
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            if (!_dataLoaded || _requestPending || !ReferenceEquals(drag.Project, _project)) return true;
            var target = FixtureDropTarget(sender, e);
            if (target == null) return true;
            DescribeFixtureDrop(drag, target.Value.Board, target.Value.Chassis, target.Value.PoleIndex, out bool allowed);
            if (!allowed) return true;
            e.Effects = DragDropEffects.Move;
            // Defer until DoDragDrop returns: the move may show the new-circuit dialog and rebuilds the tree.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsCurrentProject(drag.Project)) return;
                MoveFixtures(drag.Fixtures, target.Value.Board, target.Value.Chassis, target.Value.PoleIndex);
            }));
            return true;
        }

        private void FixtureRow_DragOver(object sender, DragEventArgs e)
        {
            if (!HandleFixtureDragOver(sender, e))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }
        }

        private void FixtureRow_Drop(object sender, DragEventArgs e)
        {
            if (!HandleFixtureDrop(sender, e)) e.Handled = true;
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
                Project = _project, Board = row.Board, Chassis = row.Chassis, Circuit = circuit,
                Bulk = Selection.Contains(TreeSelectionKind.Circuits, circuit.CircuitGuid) && Selection.Count > 1
                    ? BulkCircuitOperations.ResolveCircuits(_boards, Selection.Keys.ToList()) : null
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
            if (HandleFixtureDragOver(sender, e)) return;
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            if (e.Data.GetData(typeof(CircuitDrag)) is not CircuitDrag drag ||
                sender is not Grid grid || grid.DataContext is not CircuitRowViewModel row) return;
            if (!_dataLoaded || _requestPending || !ReferenceEquals(drag.Project, _project)) return;
            PoleAssignment position = PositionAt(grid, row, e.GetPosition(grid));
            if (drag.Bulk != null)
            {
                BulkMovePlan plan = BulkCircuitOperations.PlanMove(drag.Bulk, row.Board, row.Chassis, position.PoleIndex);
                StatusLabel.Text = plan.Error ?? (plan.IsNoOp ? "The selected circuits are already here."
                    : $"Move {drag.Bulk.Count} circuits to {row.Board.DisplayName} / {row.Chassis.Name} / " +
                      $"{plan.Placements[0].Position.DisplayLabel}…{plan.Placements[plan.Placements.Count - 1].Position.DisplayLabel}" +
                      (plan.ReplacedSpares.Any(CircuitLayout.HasDetails) ? " — replaces spares" : string.Empty));
                if (plan.IsValid && !plan.IsNoOp) e.Effects = DragDropEffects.Move;
                return;
            }
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
            if (HandleFixtureDrop(sender, e)) return;
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            if (e.Data.GetData(typeof(CircuitDrag)) is not CircuitDrag drag ||
                sender is not Grid grid || grid.DataContext is not CircuitRowViewModel row) return;
            if (!_dataLoaded || _requestPending || !ReferenceEquals(drag.Project, _project)) return;
            PoleAssignment position = PositionAt(grid, row, e.GetPosition(grid));
            BoardData targetBoard = row.Board;
            ChassisData targetChassis = row.Chassis;
            if (drag.Bulk != null)
            {
                List<CircuitRef> circuits = drag.Bulk;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (IsCurrentProject(drag.Project))
                        MoveCircuits(circuits, targetBoard, targetChassis, position.PoleIndex);
                }));
                return;
            }
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
            if (!e.Data.GetDataPresent(typeof(CircuitDrag)) && !e.Data.GetDataPresent(typeof(FixtureDrag))) return;
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
            if (e.Handled || (!e.Data.GetDataPresent(typeof(CircuitDrag)) &&
                !e.Data.GetDataPresent(typeof(FixtureDrag)))) return;
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
    }
}

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
        private TreeSelection? _selection;

        private TreeSelection Selection
        {
            get
            {
                if (_selection != null) return _selection;
                _selection = new TreeSelection(OnMoveSelection, OnDeleteSelection);
                // Keeps the highlight in sync, including Clear Selection from the context menu.
                _selection.PropertyChanged += (_, _) => _selection.ApplyTo(_treeNodes);
                return _selection;
            }
        }

        TreeSelection ICircuitTreeActions.Selection => Selection;

        // ─── Click Selection ─────────────────────────────────

        private static (bool Ctrl, bool Shift) SelectionModifiers()
        {
            ModifierKeys modifiers = Keyboard.Modifiers;
            return ((modifiers & ModifierKeys.Control) != 0, (modifiers & ModifierKeys.Shift) != 0);
        }

        private void SelectCircuitAt(Grid grid, CircuitRowViewModel row, MouseButtonEventArgs e)
        {
            (bool ctrl, bool shift) = SelectionModifiers();
            bool right = PositionAt(grid, row, e.GetPosition(grid)).PoleIndex % 2 == 1;
            Guid? key = right ? row.RightKey : row.LeftKey;
            if (key == null)
            {
                if (!ctrl && !shift) ClearSelection();
                return;
            }
            Selection.Click(TreeSelectionKind.Circuits, key.Value, TreeSelection.CircuitOrder(_treeNodes), ctrl, shift);
            ShowSelection();
        }

        private void SelectFixture(FixtureTreeNode node)
        {
            (bool ctrl, bool shift) = SelectionModifiers();
            Selection.Click(TreeSelectionKind.Elements, node.Fixture.FixtureGuid,
                TreeSelection.ElementOrder(_treeNodes), ctrl, shift);
            ShowSelection();
        }

        private void ShowSelection()
        {
            Selection.ApplyTo(_treeNodes);
            if (Selection.Count > 0) StatusLabel.Text = Selection.Summary;
        }

        private void ClearSelection()
        {
            if (Selection.Count == 0) return;
            Selection.Clear();
            Selection.ApplyTo(_treeNodes);
        }

        /// <summary>Drops selected circuits/elements that no longer exist and re-applies the highlight.</summary>
        private void RestoreSelection()
        {
            if (Selection.Count > 0)
            {
                var existing = new HashSet<Guid>(
                    from board in _boards
                    from chassis in board.Chassis
                    from circuit in chassis.Circuits
                    from key in Selection.Kind == TreeSelectionKind.Circuits
                        ? new[] { circuit.CircuitGuid }
                        : circuit.Fixtures.Select(f => f.FixtureGuid)
                    select key);
                Selection.Prune(existing.Contains);
            }
            Selection.ApplyTo(_treeNodes);
        }

        // ─── Bulk Move ───────────────────────────────────────

        private void OnMoveSelection()
        {
            if (Selection.Kind == TreeSelectionKind.Elements)
            {
                List<FixtureRef> fixtures = BulkCircuitOperations.ResolveFixtures(_boards, Selection.Keys.ToList());
                if (fixtures.Count > 0) OnMoveFixtures(fixtures);
                return;
            }
            if (Selection.Kind != TreeSelectionKind.Circuits) return;
            List<CircuitRef> circuits = BulkCircuitOperations.ResolveCircuits(_boards, Selection.Keys.ToList());
            if (circuits.Count == 0) return;
            if (circuits.Count == 1)
            {
                OnMoveCircuit(circuits[0].Board, circuits[0].Chassis, circuits[0].Circuit);
                return;
            }

            ProjectData project = _project;
            var picker = new CircuitMovePicker(_boards, circuits);
            if (picker.TotalAvailable == 0)
            {
                MessageBox.Show($"No chassis has room for the {circuits.Count} selected circuits.",
                    "Move Circuits", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dialog = new MoveCircuitDialog(picker);
            if (dialog.ShowDialog() != true || dialog.Destination == null) return;
            if (!IsCurrentProject(project)) return;
            MovePositionOption destination = dialog.Destination;
            MoveCircuits(circuits, destination.Board, destination.Chassis, destination.Position.PoleIndex);
        }

        private bool MoveCircuits(List<CircuitRef> circuits, BoardData targetBoard, ChassisData targetChassis,
            int poleIndex)
        {
            if (!IsCurrentTarget(targetBoard, targetChassis) ||
                !circuits.All(c => IsCurrentTarget(c.Board, c.Chassis, c.Circuit)))
            {
                MessageBox.Show("A selected circuit no longer exists. Refresh and try again.",
                    "Move Circuits", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            BulkMovePlan plan = BulkCircuitOperations.PlanMove(circuits, targetBoard, targetChassis, poleIndex);
            if (!plan.IsValid)
            {
                MessageBox.Show(plan.Error, "Cannot Move Circuits", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (plan.IsNoOp) return false;

            ProjectData project = _project;
            List<CircuitData> replaced = plan.ReplacedSpares.Where(CircuitLayout.HasDetails).ToList();
            if (replaced.Count > 0)
            {
                MessageBoxResult confirm = MessageBox.Show(
                    $"Moving the {circuits.Count} circuits will replace these spare positions and delete their details:\n" +
                    string.Join("\n", replaced.Select(c => "  • " + CircuitLayout.SpareSummary(c))) +
                    "\n\nContinue?", "Replace Spare", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes || !IsCurrentProject(project)) return false;
            }
            PushUndoState();
            BulkCircuitOperations.ApplyMove(plan, targetBoard, targetChassis);
            SaveAndRefresh(new[] { targetBoard.BoardGuid, targetChassis.ChassisGuid });
            StatusLabel.Text = $"Moved {circuits.Count} circuits to {targetBoard.DisplayName} / {targetChassis.Name}.";
            return true;
        }

        // ─── Bulk Delete ─────────────────────────────────────

        private void OnDeleteSelection()
        {
            if (Selection.Kind == TreeSelectionKind.Elements)
            {
                DeleteSelectedFixtures();
                return;
            }
            if (Selection.Kind != TreeSelectionKind.Circuits) return;
            List<CircuitRef> circuits = BulkCircuitOperations.ResolveCircuits(_boards, Selection.Keys.ToList());
            if (circuits.Count == 0) return;
            if (circuits.Count == 1)
            {
                OnDeleteCircuit(circuits[0].Chassis, circuits[0].Circuit);
                return;
            }
            if (!circuits.All(c => IsCurrentTarget(c.Board, c.Chassis, c.Circuit))) return;

            ProjectData project = _project;
            int elements = circuits.Sum(c => c.Circuit.Fixtures.Count);
            const int listed = 10;
            string list = string.Join("\n", circuits.Take(listed).Select(c =>
                $"  • {c.Board.DisplayName} / {c.Chassis.Name} / {c.Circuit.DisplayLabel}" +
                (string.IsNullOrWhiteSpace(c.Circuit.Description) ? string.Empty : $" ({c.Circuit.Description})")));
            if (circuits.Count > listed) list += $"\n  … and {circuits.Count - listed} more";
            MessageBoxResult confirm = MessageBox.Show(
                $"Delete {circuits.Count} circuits?\n\n{list}\n\n" +
                "Their positions become spaces and all their data is cleared." +
                (elements > 0 ? $"\n{elements} element(s) will be unassigned (they stay in the model)." : string.Empty),
                "Delete Circuits", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes || !IsCurrentProject(project)) return;

            PushUndoState();
            BulkCircuitOperations.DeleteCircuits(circuits);
            Selection.Clear();
            SaveAndRefresh();
            StatusLabel.Text = $"Deleted {circuits.Count} circuits" +
                (elements > 0 ? $"; {elements} element(s) unassigned." : ".");
        }

        private void DeleteSelectedFixtures()
        {
            List<FixtureRef> fixtures = BulkCircuitOperations.ResolveFixtures(_boards, Selection.Keys.ToList());
            if (fixtures.Count == 0 || !fixtures.All(IsCurrentFixture)) return;
            ProjectData project = _project;
            int circuits = fixtures.Select(f => f.Circuit).Distinct().Count();
            MessageBoxResult confirm = MessageBox.Show(
                $"Unassign {fixtures.Count} element(s) from {circuits} circuit(s)?\n\n" +
                "The elements stay in the model; only their circuit assignment is removed. " +
                "A circuit left without elements becomes a spare if it has saved details, otherwise a space.",
                "Unassign Elements", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes || !IsCurrentProject(project)) return;

            PushUndoState();
            int removed = BulkCircuitOperations.UnassignFixtures(fixtures);
            Selection.Clear();
            SaveAndRefresh();
            StatusLabel.Text = $"Unassigned {removed} element(s).";
        }

        // ─── Keyboard ────────────────────────────────────────

        /// <summary>Esc clears the selection, Delete deletes/unassigns it. Returns true when handled.</summary>
        private bool HandleSelectionKey(Key key)
        {
            if (SearchBox.IsKeyboardFocusWithin || Selection.Count == 0) return false;
            if (key == Key.Escape)
            {
                ClearSelection();
                StatusLabel.Text = "Selection cleared.";
                return true;
            }
            if (key == Key.Delete)
            {
                OnDeleteSelection();
                return true;
            }
            return false;
        }
    }
}

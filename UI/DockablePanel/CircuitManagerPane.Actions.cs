using System.Windows;
using Circuiting.Commands;
using Circuiting.Models;
using Circuiting.Services;
using Circuiting.Storage;
using Circuiting.UI.Dialogs;
using Circuiting.ViewModels;
using Newtonsoft.Json;

namespace Circuiting.UI.DockablePanel
{
    public partial class CircuitManagerPane
    {
        // ─── Circuit Row Double Click ────────────────────────────

        private void CircuitRow_MouseLeftButtonDown(object sender,
            System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not CircuitRowViewModel row) return;
            if (e.ClickCount == 1)
            {
                if (element is System.Windows.Controls.Grid grid) SelectCircuitAt(grid, row, e);
                return;
            }
            if (e.ClickCount != 2) return;
            e.Handled = true;
            if (row.EditCommand == null)
            {
                System.Diagnostics.Trace.TraceError("A circuit row has no edit command.");
                MessageBox.Show("The circuit row could not be opened. Refresh the panel and try again.",
                    "Circuit Manager", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            row.EditCommand.Execute(null);
        }

        private void OnEditRow(CircuitRowViewModel tag)
        {
            if (!IsCurrentTarget(tag.Board, tag.Chassis)) return;
            ProjectData project = _project;

            CircuitData? leftExisting = tag.LeftConsumed
                ? tag.LeftConsumedInfo?.Parent
                : tag.Chassis.Circuits.FirstOrDefault(c => c.PoleIndex == tag.Left.PoleIndex);
            CircuitData? rightExisting = tag.RightConsumed
                ? tag.RightConsumedInfo?.Parent
                : tag.Chassis.Circuits.FirstOrDefault(c => c.PoleIndex == tag.Right.PoleIndex);
            CircuitEditDialog dialog = new CircuitEditDialog(
                leftExisting?.DisplayLabel ?? tag.Left.DisplayLabel,
                rightExisting?.DisplayLabel ?? tag.Right.DisplayLabel,
                leftExisting, rightExisting, tag.Chassis.PhaseLabels.Count >= 3);
            dialog.ShowDialog();
            if (!dialog.Confirmed) return;
            if (!IsCurrentProject(project)) return;

            List<PoleAssignment> assignments =
                CircuitLabelCalculator.Calculate(tag.Board, tag.Chassis);
            var updates = new[]
            {
                (Result: dialog.LeftResult, Existing: leftExisting, Position: tag.Left),
                (Result: dialog.RightResult, Existing: rightExisting, Position: tag.Right)
            };
            List<CircuitData> candidates = tag.Chassis.Circuits.ToList();
            bool changed = false;
            foreach (var update in updates)
            {
                if (update.Result == null) continue;
                PoleAssignment position = update.Existing == null
                    ? update.Position
                    : assignments.Single(a => a.PoleIndex == update.Existing.PoleIndex);
                if (update.Result.Poles > 1 &&
                    (update.Existing == null || update.Existing.Poles != update.Result.Poles) &&
                    !CircuitLayout.IsValidMultiPoleStart(position, tag.Chassis, update.Result.Poles, assignments))
                {
                    MessageBox.Show("The circuit must start on the first phase and have enough following poles.",
                        "Invalid Position", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                update.Result.PoleIndex = position.PoleIndex;
                if (update.Existing != null) candidates.Remove(update.Existing);
                candidates.Add(update.Result);
                changed = true;
            }

            if (!changed) return;
            var replacedSpares = new List<CircuitData>();
            foreach (var update in updates)
            {
                if (update.Result == null || update.Result.Poles < 2) continue;
                List<int> poles;
                try { poles = CircuitLayout.PolesOf(update.Result).ToList(); }
                catch (System.IO.InvalidDataException) { continue; }
                replacedSpares.AddRange(CircuitLayout.GetSparesAt(candidates, poles, update.Result)
                    .Where(spare => !ReferenceEquals(spare, updates[0].Result) &&
                        !ReferenceEquals(spare, updates[1].Result) && !replacedSpares.Contains(spare)));
            }
            List<CircuitData> discarded = replacedSpares.Where(CircuitLayout.HasDetails).ToList();
            candidates.RemoveAll(replacedSpares.Contains);
            try
            {
                CircuitLayout.ValidateAndUpdate(tag.Board, tag.Chassis, candidates);
            }
            catch (System.IO.InvalidDataException ex)
            {
                MessageBox.Show(ex.Message + "\n\nMove or delete the circuits with fixtures assigned on those " +
                    "positions before creating a multi-pole circuit.",
                    "Invalid Circuit Layout", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (discarded.Count > 0 && MessageBox.Show(
                    "The multi-pole circuit will replace these spare positions and delete their details:\n" +
                    string.Join("\n", discarded.Select(c => "  • " + CircuitLayout.SpareSummary(c))) +
                    "\n\nContinue?", "Replace Spares", MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes) return;
            if (!IsCurrentProject(project)) return;

            PushUndoState();
            tag.Chassis.Circuits = candidates;
            SaveAndRefresh();
        }

        private void OnAssignFixturesToSpare(
            BoardData board, ChassisData chassis, PoleAssignment pole)
        {
            if (!IsCurrentTarget(board, chassis)) return;
            CircuitData newCircuit = new CircuitData
            {
                CircuitGuid = Guid.NewGuid(),
                DisplayLabel = pole.DisplayLabel,
                CircuitNumber = pole.CircuitNumber,
                PoleIndex = pole.PoleIndex,
                Poles = 1
            };
            ProjectData desired = ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(_project));
            ChassisData target = desired.Boards.Single(b => b.BoardGuid == board.BoardGuid)
                .Chassis.Single(c => c.ChassisGuid == chassis.ChassisGuid);
            target.Circuits.Add(newCircuit);
            QueueRequest(RevitAction.AssignFixtures, ProjectDataSerializer.Serialize(desired),
                board.BoardGuid, chassis.ChassisGuid, newCircuit.CircuitGuid);
        }

        // ─── Convert To Three-Phase ──────────────────────────

        private void OnConvertToThreePhase(BoardData board, ChassisData chassis, PoleAssignment pole)
        {
            if (!IsCurrentTarget(board, chassis)) return;
            ProjectData project = _project;
            string? error = CircuitLayout.GetThreePhaseConversion(board, chassis, pole.PoleIndex,
                out ThreePhaseConversion? plan);
            if (error != null || plan == null)
            {
                MessageBox.Show(error, "Cannot Convert to Three-Phase", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            List<CircuitData> discarded = plan.Replaced.Where(CircuitLayout.HasDetails).ToList();
            if (discarded.Count > 0)
            {
                string kept = plan.Kept == null ? "a new empty circuit"
                    : CircuitLayout.IsOccupied(plan.Kept) ? $"{plan.Kept.DisplayLabel} (it has fixtures assigned)"
                    : plan.Kept.DisplayLabel;
                MessageBoxResult confirm = MessageBox.Show(
                    $"The three-phase circuit will keep the details of {kept}.\n\n" +
                    "The details saved on these spare positions will be deleted:\n" +
                    string.Join("\n", discarded.Select(c => "  • " + CircuitLayout.SpareSummary(c))) +
                    "\n\nContinue?",
                    "Convert to Three-Phase", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes || !IsCurrentProject(project)) return;
            }

            string snapshot = ProjectDataSerializer.Serialize(_project);
            try
            {
                CircuitLayout.ConvertToThreePhase(board, chassis, pole.PoleIndex);
            }
            catch (System.IO.InvalidDataException ex)
            {
                MessageBox.Show(ex.Message, "Cannot Convert to Three-Phase", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _history.Push(snapshot);
            SaveAndRefresh();
        }

        // ─── Delete Single Circuit ───────────────────────────

        private void OnDeleteCircuit(
            ChassisData chassis, CircuitData circuit)
        {
            if (!IsCurrentTarget(null, chassis, circuit)) return;
            ProjectData project = _project;
            int fixtureCount = circuit.Fixtures.Count;
            CircuitData? destination = null;

            if (fixtureCount > 0)
            {
                MessageBoxResult result = MessageBox.Show(
                    $"Circuit {circuit.DisplayLabel} " +
                    $"({circuit.Description}) has " +
                    $"{fixtureCount} fixture(s) assigned.\n\n" +
                    "Deleting this circuit will remove all " +
                    "fixture assignments.\n\n" +
                    "Would you like to assign these fixtures " +
                    "to a different circuit first?",
                    "Fixtures Assigned",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Cancel) return;
                if (!IsCurrentProject(project)) return;

                if (result == MessageBoxResult.Yes)
                {
                    List<CircuitDestination> destinations = (
                        from board in _boards
                        from targetChassis in board.Chassis
                        from target in targetChassis.Circuits
                        where target.CircuitGuid != circuit.CircuitGuid
                        select new CircuitDestination
                        {
                            Circuit = target,
                            DisplayName = $"{board.DisplayName} / {targetChassis.Name} / " +
                                $"{target.DisplayLabel} {target.Description}"
                        }).ToList();
                    if (destinations.Count == 0)
                    {
                        MessageBox.Show("There are no other circuits. Create one before reassigning fixtures.",
                            "Reassign Fixtures", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    var dialog = new ReassignFixturesDialog(destinations);
                    if (dialog.ShowDialog() != true || dialog.Destination == null) return;
                    if (!IsCurrentProject(project)) return;
                    destination = dialog.Destination;
                }
            }
            else
            {
                MessageBoxResult confirm = MessageBox.Show(
                    $"Delete circuit {circuit.DisplayLabel} " +
                    $"({circuit.Description})?\n\n" +
                    "This will clear all data and make it a space.",
                    "Delete Circuit",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes) return;
                if (!IsCurrentProject(project)) return;
            }

            PushUndoState();
            if (destination != null)
                destination.Fixtures.AddRange(circuit.Fixtures);
            chassis.Circuits.Remove(circuit);
            SaveAndRefresh();
        }

        // ─── Clear All Circuits in Chassis ───────────────────

        private void OnClearAllCircuits(ChassisData chassis)
        {
            if (!IsCurrentTarget(null, chassis)) return;
            ProjectData project = _project;
            if (chassis.Circuits.Count == 0)
            {
                StatusLabel.Text = "No circuits to clear.";
                return;
            }

            int totalCircuits = chassis.Circuits.Count;
            int totalFixtures = chassis.Circuits
                .Sum(c => c.Fixtures?.Count ?? 0);

            string message = $"Clear all {totalCircuits} circuit(s) " +
                $"from chassis '{chassis.Name}'?\n\n" +
                "All descriptions, breaker sizes, and protection " +
                "settings will be removed.";

            if (totalFixtures > 0)
            {
                message += $"\n\n⚠ {totalFixtures} fixture(s) " +
                    "across all circuits will be unassigned.";
            }

            MessageBoxResult result = MessageBox.Show(
                message,
                "Clear All Circuits",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;
            if (!IsCurrentProject(project)) return;

            PushUndoState();
            chassis.Circuits.Clear();
            SaveAndRefresh();
        }
        // ─── Board Actions ───────────────────────────────────────

        private void OnRenameBoard(BoardData board)
        {
            if (!IsCurrentTarget(board)) return;
            ProjectData project = _project;
            RenameDialog dialog = new RenameDialog(
                "Rename Board", board.DisplayName);
            dialog.ShowDialog();
            if (!dialog.Confirmed) return;
            if (!IsCurrentProject(project)) return;
            PushUndoState();
            board.DisplayName = dialog.NewName;
            SaveAndRefresh();
        }

        private void OnDeleteBoard(BoardData board)
        {
            if (!IsCurrentTarget(board)) return;
            ProjectData project = _project;
            int fixtures = board.Chassis.Sum(c => c.Circuits.Sum(circuit => circuit.Fixtures.Count));
            MessageBoxResult result = MessageBox.Show(
                $"Delete board '{board.DisplayName}'?\n\n{fixtures} fixture assignment(s) will be removed.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
            if (!IsCurrentProject(project)) return;
            PushUndoState();
            _boards.Remove(board);
            SaveAndRefresh();
        }

        // ─── Chassis Actions ─────────────────────────────────────

        private static int CalculateSuggestedStart(BoardData board) =>
            BoardSetup.NextStartingCircuit(board.Chassis, board.Chassis.Count);

        private void OnEditBoard(BoardData board)
        {
            if (!IsCurrentTarget(board)) return;
            ProjectData project = _project;
            var dialog = new AddBoardDialog(_boards, editing: board);
            dialog.ShowDialog();
            if (!dialog.Confirmed || dialog.Result == null) return;
            if (!IsCurrentProject(project) || !IsCurrentTarget(board)) return;
            string? error = BoardSetup.ValidateLayout(dialog.Result, board);
            if (error != null)
            {
                MessageBox.Show(error, "Invalid Board Layout", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            PushUndoState();
            _boards[_boards.IndexOf(board)] = dialog.Result;
            SaveAndRefresh();
        }

        private void OnDuplicateBoard(BoardData board)
        {
            if (!IsCurrentTarget(board)) return;
            ProjectData project = _project;
            var dialog = new DuplicateBoardDialog(board, _boards.Select(b => b.DisplayName));
            if (dialog.ShowDialog() != true || dialog.Names.Count == 0) return;
            if (!IsCurrentProject(project) || !IsCurrentTarget(board)) return;
            if (_search.IsActive) ClearSearch();
            PushUndoState();
            _boards.InsertRange(_boards.IndexOf(board) + 1,
                dialog.Names.Select(name => BoardSetup.CopySetup(board, name)));
            SaveAndRefresh();
        }

        private void OnAddChassis(BoardData board)
        {
            if (!IsCurrentTarget(board)) return;
            ProjectData project = _project;
            int suggestedStart;
            try
            {
                suggestedStart = CalculateSuggestedStart(board);
            }
            catch (OverflowException)
            {
                MessageBox.Show("No higher circuit number is available for a new chassis.",
                    "Add Chassis", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            bool isThreePhase =
                board.Supply == SupplyType.ThreePhase;
            bool allowThreePhase =
                board.Supply == SupplyType.ThreePhase;

            ChassisConfigDialog dialog = new ChassisConfigDialog(
                existing: null,
                suggestedStart: suggestedStart,
                isThreePhase: isThreePhase,
                allowThreePhase: allowThreePhase,
                board: board);
            dialog.ShowDialog();
            if (!dialog.Confirmed || dialog.Result == null) return;
            if (!IsCurrentProject(project)) return;
            PushUndoState();
            board.Chassis.Add(dialog.Result);
            SaveAndRefresh();
        }

        private void OnEditChassis(
            BoardData board, ChassisData chassis)
        {
            if (!IsCurrentTarget(board, chassis)) return;
            ProjectData project = _project;
            bool isThreePhase =
                board.Supply == SupplyType.ThreePhase;
            bool allowThreePhase =
                board.Supply == SupplyType.ThreePhase;

            ChassisConfigDialog dialog = new ChassisConfigDialog(
                chassis, chassis.StartingCircuit, isThreePhase,
                allowThreePhase, board);
            dialog.ShowDialog();
            if (!dialog.Confirmed || dialog.Result == null) return;
            if (!IsCurrentProject(project)) return;
            int index = board.Chassis.IndexOf(chassis);
            if (index < 0) throw new InvalidOperationException("The chassis is no longer in the board.");
            if (dialog.Result.PhaseLabels.Count < 3 && chassis.Circuits.Any(c => c.Poles > 1))
            {
                MessageBox.Show("A single-phase chassis cannot contain multi-pole circuits. Change those circuits to single-pole before saving.",
                    "Invalid Chassis Layout", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            dialog.Result.Circuits = JsonConvert.DeserializeObject<List<CircuitData>>(
                JsonConvert.SerializeObject(chassis.Circuits, _jsonSettings), _jsonSettings)
                ?? throw new InvalidOperationException("Unable to copy the circuit data.");
            BoardData candidate = JsonConvert.DeserializeObject<BoardData>(
                JsonConvert.SerializeObject(board, _jsonSettings), _jsonSettings)
                ?? throw new InvalidOperationException("Unable to copy the board data.");
            candidate.Chassis[index] = dialog.Result;
            try
            {
                foreach (ChassisData candidateChassis in candidate.Chassis)
                    CircuitLayout.ValidateAndUpdate(candidate, candidateChassis);
            }
            catch (Exception ex) when (ex is System.IO.InvalidDataException ||
                                       ex is ArgumentException || ex is OverflowException)
            {
                MessageBox.Show(ex.Message + "\n\nMove or delete affected circuits before changing the chassis.",
                    "Invalid Chassis Layout", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PushUndoState();
            board.Chassis[index] = dialog.Result;
            foreach (ChassisData boardChassis in board.Chassis)
                CircuitLayout.ValidateAndUpdate(board, boardChassis);
            SaveAndRefresh();
        }

        private void OnDeleteChassis(
            BoardData board, ChassisData chassis)
        {
            if (!IsCurrentTarget(board, chassis)) return;
            ProjectData project = _project;
            int fixtures = chassis.Circuits.Sum(c => c.Fixtures.Count);
            MessageBoxResult result = MessageBox.Show(
                $"Delete chassis '{chassis.Name}'?\n\n{fixtures} fixture assignment(s) will be removed.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
            if (!IsCurrentProject(project)) return;
            PushUndoState();
            board.Chassis.Remove(chassis);
            SaveAndRefresh();
        }

        // ─── Bulk Edit ───────────────────────────────────────────

        private void OnBulkEditChassis(
            BoardData board, ChassisData chassis)
        {
            if (!IsCurrentTarget(board, chassis)) return;
            ProjectData project = _project;
            BulkChassisEditDialog dialog =
                new BulkChassisEditDialog(chassis.Name);
            dialog.ShowDialog();
            if (!dialog.Confirmed) return;
            if (!IsCurrentProject(project)) return;
            if (!dialog.ApplyBreaker && !dialog.ApplyProtection &&
                !dialog.ApplyLeakage && !dialog.ApplyDescription)
            {
                StatusLabel.Text = "No bulk edit fields selected.";
                return;
            }

            List<PoleAssignment> assignments =
                CircuitLabelCalculator.Calculate(board, chassis);
            Dictionary<int, CircuitData> consumedPoles = CircuitLayout.GetConsumedPoles(chassis);

            PushUndoState();
            foreach (PoleAssignment assignment in assignments)
            {
                if (consumedPoles.ContainsKey(
                    assignment.PoleIndex)) continue;
                if (dialog.Scope == BulkApplyScope.LeftOnly
                    && assignment.Side != Side.Left) continue;
                if (dialog.Scope == BulkApplyScope.RightOnly
                    && assignment.Side != Side.Right) continue;

                CircuitData? existing = chassis.Circuits
                    .FirstOrDefault(c => c.PoleIndex == assignment.PoleIndex);

                if (dialog.Scope == BulkApplyScope.SpareOnly
                    && existing != null
                    && (CircuitLayout.HasDetails(existing) || CircuitLayout.IsOccupied(existing))) continue;

                if (existing == null)
                {
                    existing = new CircuitData
                    {
                        CircuitGuid = Guid.NewGuid(),
                        DisplayLabel = assignment.DisplayLabel,
                        CircuitNumber = assignment.CircuitNumber,
                        PoleIndex = assignment.PoleIndex
                    };
                    chassis.Circuits.Add(existing);
                }

                if (dialog.ApplyBreaker)
                    existing.BreakerSize = dialog.BreakerSize;
                if (dialog.ApplyProtection)
                    existing.ProtectionType =
                        dialog.ProtectionType;
                if (dialog.ApplyLeakage)
                    existing.EarthLeakage = dialog.EarthLeakage;
                if (dialog.ApplyDescription)
                    existing.Description = dialog.Description;
            }

            SaveAndRefresh();
        }

        // ─── Fixture Actions ─────────────────────────────────

        private void OnAssignFixtures(
            BoardData board, ChassisData chassis,
            CircuitData circuit)
        {
            if (!IsCurrentTarget(board, chassis, circuit)) return;
            QueueRequest(RevitAction.AssignFixtures, ProjectDataSerializer.Serialize(_project),
                board.BoardGuid, chassis.ChassisGuid, circuit.CircuitGuid);
        }

        private void OnSelectFixtures(
            BoardData board, ChassisData chassis,
            CircuitData circuit)
        {
            if (!IsCurrentTarget(board, chassis, circuit)) return;
            QueueRequest(RevitAction.SelectFixtures, ProjectDataSerializer.Serialize(_project),
                board.BoardGuid, chassis.ChassisGuid, circuit.CircuitGuid);
        }

        private void OnRemoveFixtures(
            BoardData board, ChassisData chassis,
            CircuitData circuit)
        {
            if (!IsCurrentTarget(board, chassis, circuit)) return;
            QueueRequest(RevitAction.RemoveFixtures, ProjectDataSerializer.Serialize(_project),
                board.BoardGuid, chassis.ChassisGuid, circuit.CircuitGuid);
        }

        private void OnShowFixture(BoardData board, ChassisData chassis, CircuitData circuit,
            CircuitedFixture fixture, bool zoom)
        {
            if (!IsCurrentTarget(board, chassis, circuit)) return;
            if (!circuit.Fixtures.Contains(fixture))
            {
                MessageBox.Show("The element is no longer assigned to this circuit. Reopen the menu and try again.",
                    "Circuit Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            QueueRequest(zoom ? RevitAction.ZoomToFixture : RevitAction.SelectFixture,
                ProjectDataSerializer.Serialize(_project), board.BoardGuid, chassis.ChassisGuid,
                circuit.CircuitGuid, fixtureGuid: fixture.FixtureGuid);
        }

        private void FixtureRow_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not FixtureTreeNode fixture) return;
            if (e.ClickCount == 1)
            {
                SelectFixture(fixture);
                return;
            }
            if (e.ClickCount != 2 || fixture.ZoomCommand == null) return;
            e.Handled = true;
            fixture.ZoomCommand.Execute(null);
        }

        void ICircuitTreeActions.ShowFixture(BoardData board, ChassisData chassis, CircuitData circuit,
            CircuitedFixture fixture, bool zoom) => OnShowFixture(board, chassis, circuit, fixture, zoom);
        void ICircuitTreeActions.MoveFixture(BoardData board, ChassisData chassis, CircuitData circuit,
            CircuitedFixture fixture) => OnMoveFixture(board, chassis, circuit, fixture);
        FixtureDetails? ICircuitTreeActions.GetFixtureDetails(CircuitedFixture fixture) =>
            _fixtureDetails.TryGetValue(fixture.FixtureGuid, out FixtureDetails? details) ? details : null;

        void ICircuitTreeActions.AddChassis(BoardData board) => OnAddChassis(board);
        void ICircuitTreeActions.RenameBoard(BoardData board) => OnRenameBoard(board);
        void ICircuitTreeActions.DeleteBoard(BoardData board) => OnDeleteBoard(board);
        void ICircuitTreeActions.EditChassis(BoardData board, ChassisData chassis) => OnEditChassis(board, chassis);
        void ICircuitTreeActions.BulkEditChassis(BoardData board, ChassisData chassis) => OnBulkEditChassis(board, chassis);
        void ICircuitTreeActions.ClearChassis(ChassisData chassis) => OnClearAllCircuits(chassis);
        void ICircuitTreeActions.EditBoard(BoardData board) => OnEditBoard(board);
        void ICircuitTreeActions.DuplicateBoard(BoardData board) => OnDuplicateBoard(board);
        void ICircuitTreeActions.DeleteChassis(BoardData board, ChassisData chassis) => OnDeleteChassis(board, chassis);
        void ICircuitTreeActions.EditRow(CircuitRowViewModel row) => OnEditRow(row);
        void ICircuitTreeActions.DeleteCircuit(ChassisData chassis, CircuitData circuit) => OnDeleteCircuit(chassis, circuit);
        void ICircuitTreeActions.MoveCircuit(BoardData board, ChassisData chassis, CircuitData circuit) =>
            OnMoveCircuit(board, chassis, circuit);
        void ICircuitTreeActions.ConvertToThreePhase(BoardData board, ChassisData chassis, PoleAssignment pole) =>
            OnConvertToThreePhase(board, chassis, pole);
        void ICircuitTreeActions.AssignFixtures(BoardData board, ChassisData chassis, CircuitData circuit) =>
            OnAssignFixtures(board, chassis, circuit);
        void ICircuitTreeActions.AssignFixturesToSpare(BoardData board, ChassisData chassis, PoleAssignment pole) =>
            OnAssignFixturesToSpare(board, chassis, pole);
        void ICircuitTreeActions.SelectFixtures(BoardData board, ChassisData chassis, CircuitData circuit) =>
            OnSelectFixtures(board, chassis, circuit);
        void ICircuitTreeActions.RemoveFixtures(BoardData board, ChassisData chassis, CircuitData circuit) =>
            OnRemoveFixtures(board, chassis, circuit);

    }
}

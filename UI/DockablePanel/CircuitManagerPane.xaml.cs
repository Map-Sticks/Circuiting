using Autodesk.Revit.UI;
using Circuiting.Models;
using Circuiting.Services;
using Circuiting.UI.Dialogs;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfGrid = System.Windows.Controls.Grid;

namespace Circuiting.UI.DockablePanel
{
    public partial class CircuitManagerPane : UserControl, IDockablePaneProvider
    {
        private UIApplication? _uiApp;
        private List<BoardData> _boards = new List<BoardData>();
        private Stack<string> _undoStack = new Stack<string>();
        private Stack<string> _redoStack = new Stack<string>();
        private bool _dataLoaded = false;

        private static readonly JsonSerializerSettings _jsonSettings =
            new JsonSerializerSettings
            {
                ObjectCreationHandling = ObjectCreationHandling.Replace
            };

        public CircuitManagerPane()
        {
            InitializeComponent();
            PreviewKeyDown += OnPreviewKeyDown;
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = this;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Tabbed,
                TabBehind = DockablePanes.BuiltInDockablePanes.ProjectBrowser
            };
        }

        public void SetApplication(UIApplication uiApp)
        {
            _uiApp = uiApp;
        }

        public void SetBoards(List<BoardData> boards)
        {
            _boards = boards;
            _dataLoaded = true;
            _undoStack.Clear();
            _redoStack.Clear();
            RefreshTree();
        }

        public void UpdateStatus(string message)
        {
            StatusLabel.Text = message;
        }
        // ─── Auto-Save + Undo/Redo ──────────────────────────

        private void PushUndoState()
        {
            string snapshot = JsonConvert.SerializeObject(
                _boards, _jsonSettings);
            _undoStack.Push(snapshot);
            _redoStack.Clear();

            if (_undoStack.Count > 50)
            {
                // Keep stack manageable — convert to array,
                // keep last 50, rebuild
                string[] items = _undoStack.ToArray();
                _undoStack.Clear();
                for (int i = Math.Min(49, items.Length - 1);
                    i >= 0; i--)
                {
                    _undoStack.Push(items[i]);
                }
            }
        }

        private void SaveAndRefresh()
        {
            RefreshTree();
            TriggerAutoSave();
        }

        private void TriggerAutoSave()
        {
            if (!_dataLoaded) return;

            try
            {
                Circuiting.Application.EventHandler.CurrentAction =
                    Commands.RevitAction.Save;
                Circuiting.Application.EventHandler.BoardsToSave =
                    _boards;
                Circuiting.Application.RevitEvent.Raise();
            }
            catch
            {
                StatusLabel.Text = "Auto-save failed";
            }
        }

        private void Undo()
        {
            if (_undoStack.Count == 0)
            {
                StatusLabel.Text = "Nothing to undo";
                return;
            }

            string currentState = JsonConvert.SerializeObject(
                _boards, _jsonSettings);
            _redoStack.Push(currentState);

            string previousState = _undoStack.Pop();
            _boards = JsonConvert.DeserializeObject<List<BoardData>>(
                previousState, _jsonSettings)
                ?? new List<BoardData>();

            SaveAndRefresh();
            StatusLabel.Text =
                $"Undo ({_undoStack.Count} step(s) remaining)";
        }

        private void Redo()
        {
            if (_redoStack.Count == 0)
            {
                StatusLabel.Text = "Nothing to redo";
                return;
            }

            string currentState = JsonConvert.SerializeObject(
                _boards, _jsonSettings);
            _undoStack.Push(currentState);

            string nextState = _redoStack.Pop();
            _boards = JsonConvert.DeserializeObject<List<BoardData>>(
                nextState, _jsonSettings)
                ?? new List<BoardData>();

            SaveAndRefresh();
            StatusLabel.Text =
                $"Redo ({_redoStack.Count} step(s) remaining)";
        }

        private void OnPreviewKeyDown(object sender,
            System.Windows.Input.KeyEventArgs e)
        {
            bool ctrl = (System.Windows.Input.Keyboard.Modifiers &
                System.Windows.Input.ModifierKeys.Control) != 0;

            if (!ctrl) return;

            if (e.Key == System.Windows.Input.Key.Z)
            {
                Undo();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Y)
            {
                Redo();
                e.Handled = true;
            }
        }
        // ─── Tag for circuit rows ────────────────────────────────

        private class CircuitRowTag
        {
            public BoardData Board { get; set; } = null!;
            public ChassisData Chassis { get; set; } = null!;
            public PoleAssignment Left { get; set; } = null!;
            public PoleAssignment Right { get; set; } = null!;
            public bool LeftConsumed { get; set; }
            public bool RightConsumed { get; set; }
            public ConsumedPoleInfo? LeftConsumedInfo { get; set; }
            public ConsumedPoleInfo? RightConsumedInfo { get; set; }
        }
        private class ConsumedPoleInfo
        {
            public CircuitData Parent { get; set; } = null!;
            public bool IsDescriptionRow { get; set; }
        }
        private Dictionary<string, ConsumedPoleInfo> BuildConsumedPoles(
            ChassisData chassis, List<PoleAssignment> assignments)
        {
            Dictionary<string, ConsumedPoleInfo> consumed =
                new Dictionary<string, ConsumedPoleInfo>();

            foreach (CircuitData circuit in chassis.Circuits)
            {
                if (circuit.Poles <= 1) continue;

                PoleAssignment? startPole = assignments
                    .FirstOrDefault(a =>
                        a.DisplayLabel == circuit.DisplayLabel);
                if (startPole == null) continue;

                List<PoleAssignment> following = assignments
                    .Where(a => a.Side == startPole.Side
                        && a.RowIndex > startPole.RowIndex)
                    .OrderBy(a => a.RowIndex)
                    .Take(circuit.Poles - 1)
                    .ToList();

                bool isFirst = true;
                foreach (PoleAssignment pole in following)
                {
                    consumed[pole.DisplayLabel] = new ConsumedPoleInfo
                    {
                        Parent = circuit,
                        IsDescriptionRow = isFirst
                    };
                    isFirst = false;
                }
            }

            return consumed;
        }
        // ─── Tree State Preservation ─────────────────────────────

        private HashSet<Guid> GetExpandedGuids()
        {
            HashSet<Guid> expanded = new HashSet<Guid>();

            foreach (object obj in BoardTree.Items)
            {
                if (obj is not TreeViewItem boardItem) continue;
                if (boardItem.Tag is BoardData board
                    && boardItem.IsExpanded)
                {
                    expanded.Add(board.BoardGuid);

                    foreach (object childObj in boardItem.Items)
                    {
                        if (childObj is not TreeViewItem chassisItem)
                            continue;
                        if (chassisItem.Tag is ChassisData chassis
                            && chassisItem.IsExpanded)
                        {
                            expanded.Add(chassis.ChassisGuid);
                        }
                    }
                }
            }

            return expanded;
        }

        private void RestoreExpandedState(HashSet<Guid> expanded)
        {
            foreach (object obj in BoardTree.Items)
            {
                if (obj is not TreeViewItem boardItem) continue;
                if (boardItem.Tag is BoardData board)
                {
                    boardItem.IsExpanded =
                        expanded.Contains(board.BoardGuid);

                    foreach (object childObj in boardItem.Items)
                    {
                        if (childObj is not TreeViewItem chassisItem)
                            continue;
                        if (chassisItem.Tag is ChassisData chassis)
                        {
                            chassisItem.IsExpanded =
                                expanded.Contains(chassis.ChassisGuid);
                        }
                    }
                }
            }
        }

        // ─── Multi-Pole Helpers ──────────────────────────────────

        private bool IsValidMultiPoleStart(
            PoleAssignment assignment,
            ChassisData chassis,
            int poles,
            List<PoleAssignment> allAssignments)
        {
            int phaseCount = chassis.PhaseLabels?.Count ?? 1;
            if (phaseCount <= 1) return true;

            if (assignment.RowIndex % phaseCount != 0)
                return false;

            int remaining = allAssignments
                .Count(a => a.Side == assignment.Side
                    && a.RowIndex >= assignment.RowIndex);

            return remaining >= poles;
        }

        // ─── Display Formatting ──────────────────────────────────

        private string FormatProtection(CircuitData circuit)
        {
            if (string.IsNullOrEmpty(circuit.ProtectionType))
                return string.Empty;
            if (circuit.ProtectionType == "MCB")
                return "MCB";
            if (string.IsNullOrEmpty(circuit.EarthLeakage))
                return circuit.ProtectionType;
            return $"{circuit.EarthLeakage} {circuit.ProtectionType}";
        }

        // ─── Grid Row Builder ────────────────────────────────────

        private void AddCell(
            WpfGrid grid, int column, string text,
            bool wrap = false,
            System.Windows.Media.Brush? foreground = null)
        {
            TextBlock tb = new TextBlock
            {
                Text = text,
                Foreground = foreground ?? new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(170, 170, 170)),
                FontFamily =
                    new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11,
                TextWrapping = wrap
                    ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TextTrimming = wrap
                    ? TextTrimming.None
                    : TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 1, 2, 1)
            };
            WpfGrid.SetColumn(tb, column);
            grid.Children.Add(tb);
        }

        private WpfGrid CreateCircuitRowGrid(
            PoleAssignment left, PoleAssignment right,
            CircuitData? leftCircuit, CircuitData? rightCircuit,
            bool leftConsumed, bool rightConsumed,
            ConsumedPoleInfo? leftConsumedInfo,
            ConsumedPoleInfo? rightConsumedInfo)
        {
            WpfGrid grid = new WpfGrid();

            int[] widths = { 45, 130, 75, 35, 15, 35, 75, 130, 45 };
            foreach (int w in widths)
            {
                grid.ColumnDefinitions.Add(
                    new ColumnDefinition
                    { Width = new GridLength(w) });
            }

            SolidColorBrush dimBrush = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(100, 100, 100));
            SolidColorBrush labelBrush = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(200, 200, 200));

            // ── Left side ──
            AddCell(grid, 0, left.DisplayLabel,
                foreground: labelBrush);

            if (leftConsumed && leftConsumedInfo != null)
            {
                if (leftConsumedInfo.IsDescriptionRow)
                {
                    AddCell(grid, 1,
                        leftConsumedInfo.Parent.Description,
                        wrap: true);
                    AddCell(grid, 2,
                        FormatProtection(leftConsumedInfo.Parent));
                    AddCell(grid, 3,
                        leftConsumedInfo.Parent.BreakerSize);
                }
                // Non-description consumed rows: blank
            }
            else if (leftCircuit != null
                && !string.IsNullOrEmpty(leftCircuit.Description))
            {
                if (leftCircuit.Poles > 1)
                {
                    // Start of multi-pole: blank, description
                    // will appear on the middle consumed row
                }
                else
{
                    string leftDesc = leftCircuit.Description;
                    if (leftCircuit.Fixtures.Count > 0)
                        leftDesc += $" [{leftCircuit.Fixtures.Count}]";

                    AddCell(grid, 1, leftDesc, wrap: true);
                    AddCell(grid, 2,
                        FormatProtection(leftCircuit));
                    AddCell(grid, 3, leftCircuit.BreakerSize);
                }
            }
            else if (!leftConsumed)
            {
                AddCell(grid, 1, "Spare", foreground: dimBrush);
            }

            // ── Separator ──
            AddCell(grid, 4, "│", foreground: dimBrush);

            // ── Right side (mirrored) ──
            if (rightConsumed && rightConsumedInfo != null)
            {
                if (rightConsumedInfo.IsDescriptionRow)
                {
                    AddCell(grid, 5,
                        rightConsumedInfo.Parent.BreakerSize);
                    AddCell(grid, 6,
                        FormatProtection(rightConsumedInfo.Parent));
                    AddCell(grid, 7,
                        rightConsumedInfo.Parent.Description,
                        wrap: true);
                }
                // Non-description consumed rows: blank
            }
            else if (rightCircuit != null
                && !string.IsNullOrEmpty(rightCircuit.Description))
            {
                if (rightCircuit.Poles > 1)
                {
                    // Start of multi-pole: blank
                }
                else
                {
                    string rightDesc = rightCircuit.Description;
                    if (rightCircuit.Fixtures.Count > 0)
                        rightDesc += $" [{rightCircuit.Fixtures.Count}]";

                    AddCell(grid, 5, rightCircuit.BreakerSize);
                    AddCell(grid, 6,
                        FormatProtection(rightCircuit));
                    AddCell(grid, 7, rightDesc, wrap: true);
                }
            }
            else if (!rightConsumed)
            {
                AddCell(grid, 7, "Spare", foreground: dimBrush);
            }

            AddCell(grid, 8, right.DisplayLabel,
                foreground: labelBrush);

            return grid;
        }

        // ─── Tree Building ───────────────────────────────────────

        public void RefreshTree()
        {
            HashSet<Guid> expandedState = GetExpandedGuids();

            BoardTree.Items.Clear();

            foreach (BoardData board in _boards)
            {
                TreeViewItem boardNode = new TreeViewItem
                {
                    Header = CreateBoardHeader(board),
                    IsExpanded = true,
                    Tag = board,
                    Foreground = new SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(
                            255, 255, 255)),
                    ContextMenu = CreateBoardContextMenu(board)
                };

                foreach (ChassisData chassis in board.Chassis)
                {
                    bool chassisIsThreePhase =
                        (chassis.PhaseLabels?.Count ?? 0) >= 3;
                    string chassisIcon = chassisIsThreePhase ? "⚡" : "💡";
                    string phaseInfo = chassisIsThreePhase ? "3φ" : "1φ";

                    TreeViewItem chassisNode = new TreeViewItem
                    {
                        Header = $"{chassisIcon} {chassis.Name}  " +
                                 $"({phaseInfo}, Poles: {chassis.MaxPoles}, " +
                                 $"Start: {chassis.StartingCircuit})",
                        IsExpanded = false,
                        Tag = chassis,
                        Foreground = new SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(
                                200, 200, 200)),
                        ContextMenu = CreateChassisContextMenu(
                            board, chassis)
                    };

                    List<PoleAssignment> assignments =
                        CircuitLabelCalculator.Calculate(chassis);
                    Dictionary<string, ConsumedPoleInfo> consumedPoles =
                                            BuildConsumedPoles(chassis, assignments);
                    int totalRows = chassis.MaxPoles / 2;

                    for (int row = 0; row < totalRows; row++)
                    {
                        PoleAssignment left = assignments.First(
                            a => a.RowIndex == row
                                && a.Side == Side.Left);
                        PoleAssignment right = assignments.First(
                            a => a.RowIndex == row
                                && a.Side == Side.Right);

                        bool leftConsumed = consumedPoles
                            .ContainsKey(left.DisplayLabel);
                        bool rightConsumed = consumedPoles
                            .ContainsKey(right.DisplayLabel);

                        CircuitData? leftCircuit = leftConsumed
                            ? null
                            : chassis.Circuits.FirstOrDefault(c =>
                                c.DisplayLabel ==
                                left.DisplayLabel);
                        CircuitData? rightCircuit = rightConsumed
                            ? null
                            : chassis.Circuits.FirstOrDefault(c =>
                                c.DisplayLabel ==
                                right.DisplayLabel);

                        ConsumedPoleInfo? leftInfo = leftConsumed
                            ? consumedPoles[left.DisplayLabel]
                            : null;
                        ConsumedPoleInfo? rightInfo = rightConsumed
                            ? consumedPoles[right.DisplayLabel]
                            : null;

                        WpfGrid rowGrid = CreateCircuitRowGrid(
                            left, right,
                            leftCircuit, rightCircuit,
                            leftConsumed, rightConsumed,
                            leftInfo, rightInfo);

                        CircuitRowTag rowTag = new CircuitRowTag
                        {
                            Board = board,
                            Chassis = chassis,
                            Left = left,
                            Right = right,
                            LeftConsumed = leftConsumed,
                            RightConsumed = rightConsumed,
                            LeftConsumedInfo = leftInfo,
                            RightConsumedInfo = rightInfo
                        };

                        TreeViewItem circuitNode = new TreeViewItem
                        {
                            Header = rowGrid,
                            Tag = rowTag,
                            ContextMenu = CreateCircuitRowContextMenu(rowTag)
                        };

                        circuitNode.MouseDoubleClick +=
                            CircuitRow_DoubleClick;

                        chassisNode.Items.Add(circuitNode);
                    }

                    boardNode.Items.Add(chassisNode);
                }

                BoardTree.Items.Add(boardNode);
            }

            RestoreExpandedState(expandedState);

            StatusLabel.Text = $"{_boards.Count} board(s) loaded";
        }

        // ─── Board Header ────────────────────────────────────────

        private StackPanel CreateBoardHeader(BoardData board)
        {
            StackPanel panel = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            panel.Children.Add(new TextBlock
            {
                Text = "📋 ",
                FontSize = 13
            });

            panel.Children.Add(new TextBlock
            {
                Text = board.DisplayName,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(
                        255, 255, 255))
            });

            string phaseText = board.Supply == SupplyType.ThreePhase
                ? "3 Phase" : "1 Phase";

            panel.Children.Add(new TextBlock
            {
                Text = $"  ({phaseText})",
                FontSize = 11,
                Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(
                        150, 150, 150)),
                VerticalAlignment = VerticalAlignment.Center
            });

            return panel;
        }

        // ─── Context Menus ───────────────────────────────────────

        private WpfContextMenu CreateBoardContextMenu(BoardData board)
        {
            WpfContextMenu menu = new WpfContextMenu();

            WpfMenuItem addChassis = new WpfMenuItem
            { Header = "➕  Add Chassis" };
            addChassis.Click += (s, e) => OnAddChassis(board);
            menu.Items.Add(addChassis);

            menu.Items.Add(new Separator());

            WpfMenuItem rename = new WpfMenuItem
            { Header = "✏️  Rename Board" };
            rename.Click += (s, e) => OnRenameBoard(board);
            menu.Items.Add(rename);

            WpfMenuItem delete = new WpfMenuItem
            { Header = "❌  Delete Board" };
            delete.Click += (s, e) => OnDeleteBoard(board);
            menu.Items.Add(delete);

            return menu;
        }

        private WpfContextMenu CreateChassisContextMenu(
            BoardData board, ChassisData chassis)
        {
            WpfContextMenu menu = new WpfContextMenu();

            WpfMenuItem edit = new WpfMenuItem
            { Header = "⚙️  Edit Chassis" };
            edit.Click += (s, e) => OnEditChassis(board, chassis);
            menu.Items.Add(edit);

            WpfMenuItem bulkEdit = new WpfMenuItem
            { Header = "📝  Bulk Edit Circuits" };
            bulkEdit.Click += (s, e) =>
                OnBulkEditChassis(board, chassis);
            menu.Items.Add(bulkEdit);

            WpfMenuItem clearCircuits = new WpfMenuItem
            { Header = "🧹  Clear All Circuits" };
            clearCircuits.Click += (s, e) =>
                OnClearAllCircuits(chassis);
            menu.Items.Add(clearCircuits);

            menu.Items.Add(new Separator());

            WpfMenuItem delete = new WpfMenuItem
            { Header = "❌  Delete Chassis" };
            delete.Click += (s, e) =>
                OnDeleteChassis(board, chassis);
            menu.Items.Add(delete);

            return menu;
        }

        // ─── Circuit Row Double Click ────────────────────────────

        private void CircuitRow_DoubleClick(object sender,
    System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not TreeViewItem item) return;
            if (item.Tag is not CircuitRowTag tag) return;

            e.Handled = true;

            // Resolve what to edit on each side
            // If consumed, edit the parent circuit instead
            string leftLabel;
            CircuitData? leftExisting;
            if (tag.LeftConsumed && tag.LeftConsumedInfo != null)
            {
                leftLabel = tag.LeftConsumedInfo.Parent.DisplayLabel;
                leftExisting = tag.LeftConsumedInfo.Parent;
            }
            else
            {
                leftLabel = tag.Left.DisplayLabel;
                leftExisting = tag.Chassis.Circuits
                    .FirstOrDefault(c =>
                        c.DisplayLabel == tag.Left.DisplayLabel);
            }

            string rightLabel;
            CircuitData? rightExisting;
            if (tag.RightConsumed && tag.RightConsumedInfo != null)
            {
                rightLabel = tag.RightConsumedInfo.Parent.DisplayLabel;
                rightExisting = tag.RightConsumedInfo.Parent;
            }
            else
            {
                rightLabel = tag.Right.DisplayLabel;
                rightExisting = tag.Chassis.Circuits
                    .FirstOrDefault(c =>
                        c.DisplayLabel == tag.Right.DisplayLabel);
            }

            bool allowMultiPole =
                (tag.Chassis.PhaseLabels?.Count ?? 0) >= 3;

            CircuitEditDialog dialog = new CircuitEditDialog(
                leftLabel, rightLabel,
                leftExisting, rightExisting,
                allowMultiPole);
            dialog.ShowDialog();

            if (!dialog.Confirmed) return;

            PushUndoState();

            List<PoleAssignment> assignments =
                CircuitLabelCalculator.Calculate(tag.Chassis);

            // Save left
            if (dialog.LeftResult != null)
            {
                if (dialog.LeftResult.Poles > 1)
                {
                    PoleAssignment? leftPole = assignments
                        .FirstOrDefault(a =>
                            a.DisplayLabel == leftLabel);

                    if (leftPole != null && !IsValidMultiPoleStart(
                        leftPole, tag.Chassis,
                        dialog.LeftResult.Poles, assignments))
                    {
                        string firstPhase =
                            tag.Chassis.PhaseLabels?.Count > 0
                            ? tag.Chassis.PhaseLabels[0] : "R";
                        MessageBox.Show(
                            $"A {dialog.LeftResult.Poles}-pole " +
                            $"circuit must start at a " +
                            $"{firstPhase} position.",
                            "Invalid Position",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    if (leftPole != null)
                        RemoveConsumedCircuits(tag.Chassis,
                            leftPole, dialog.LeftResult.Poles,
                            assignments);
                }

                dialog.LeftResult.DisplayLabel = leftLabel;
                dialog.LeftResult.CircuitNumber =
                    leftExisting?.CircuitNumber ?? 0;

                CircuitData? existingInList = tag.Chassis.Circuits
                    .FirstOrDefault(c => c.DisplayLabel == leftLabel);

                if (existingInList != null)
                {
                    int idx = tag.Chassis.Circuits
                        .IndexOf(existingInList);
                    if (idx >= 0) tag.Chassis.Circuits[idx] =
                        dialog.LeftResult;
                }
                else
                {
                    tag.Chassis.Circuits.Add(dialog.LeftResult);
                }
            }

            // Save right
            if (dialog.RightResult != null)
            {
                if (dialog.RightResult.Poles > 1)
                {
                    PoleAssignment? rightPole = assignments
                        .FirstOrDefault(a =>
                            a.DisplayLabel == rightLabel);

                    if (rightPole != null && !IsValidMultiPoleStart(
                        rightPole, tag.Chassis,
                        dialog.RightResult.Poles, assignments))
                    {
                        string firstPhase =
                            tag.Chassis.PhaseLabels?.Count > 0
                            ? tag.Chassis.PhaseLabels[0] : "R";
                        MessageBox.Show(
                            $"A {dialog.RightResult.Poles}-pole " +
                            $"circuit must start at a " +
                            $"{firstPhase} position.",
                            "Invalid Position",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    if (rightPole != null)
                        RemoveConsumedCircuits(tag.Chassis,
                            rightPole, dialog.RightResult.Poles,
                            assignments);
                }

                dialog.RightResult.DisplayLabel = rightLabel;
                dialog.RightResult.CircuitNumber =
                    rightExisting?.CircuitNumber ?? 0;

                CircuitData? existingInList = tag.Chassis.Circuits
                    .FirstOrDefault(c =>
                        c.DisplayLabel == rightLabel);

                if (existingInList != null)
                {
                    int idx = tag.Chassis.Circuits
                        .IndexOf(existingInList);
                    if (idx >= 0) tag.Chassis.Circuits[idx] =
                        dialog.RightResult;
                }
                else
                {
                    tag.Chassis.Circuits.Add(dialog.RightResult);
                }
            }

            SaveAndRefresh();
        }

        private void RemoveConsumedCircuits(
            ChassisData chassis, PoleAssignment startPole,
            int poles, List<PoleAssignment> allAssignments)
        {
            List<PoleAssignment> consumed = allAssignments
                .Where(a => a.Side == startPole.Side
                    && a.RowIndex > startPole.RowIndex)
                .OrderBy(a => a.RowIndex)
                .Take(poles - 1)
                .ToList();

            foreach (PoleAssignment pole in consumed)
            {
                CircuitData? existing = chassis.Circuits
                    .FirstOrDefault(c =>
                        c.DisplayLabel == pole.DisplayLabel);
                if (existing != null)
                    chassis.Circuits.Remove(existing);
            }
        }

        // ─── Circuit Row Context Menu ────────────────────────

        private WpfContextMenu CreateCircuitRowContextMenu(
    CircuitRowTag tag)
        {
            WpfContextMenu menu = new WpfContextMenu();

            // ── Left side ──
            CircuitData? leftCircuit;
            string leftLabel;

            if (tag.LeftConsumed && tag.LeftConsumedInfo != null)
            {
                leftCircuit = tag.LeftConsumedInfo.Parent;
                leftLabel = leftCircuit.DisplayLabel;
            }
            else
            {
                leftLabel = tag.Left.DisplayLabel;
                leftCircuit = tag.Chassis.Circuits
                    .FirstOrDefault(c =>
                        c.DisplayLabel == leftLabel);
            }

            if (leftCircuit != null
                && !string.IsNullOrEmpty(leftCircuit.Description))
            {
                string suffix = tag.LeftConsumed
                    ? $" (3φ from {leftLabel})" : "";

                WpfMenuItem deleteLeft = new WpfMenuItem
                {
                    Header = $"🗑️  Delete Left — " +
                        $"{leftLabel}{suffix}"
                };
                deleteLeft.Click += (s, e) =>
                    OnDeleteCircuit(tag.Chassis, leftCircuit);
                menu.Items.Add(deleteLeft);

                WpfMenuItem assignLeft = new WpfMenuItem
                {
                    Header = $"📌  Assign Fixtures — {leftLabel}"
                };
                assignLeft.Click += (s, e) =>
                    OnAssignFixtures(tag.Board, tag.Chassis,
                        leftCircuit);
                menu.Items.Add(assignLeft);

                if (leftCircuit.Fixtures.Count > 0)
                {
                    WpfMenuItem selectLeft = new WpfMenuItem
                    {
                        Header = $"👁️  Select in Model — " +
                            $"{leftLabel} " +
                            $"({leftCircuit.Fixtures.Count})"
                    };
                    selectLeft.Click += (s, e) =>
                        OnSelectFixtures(tag.Board, tag.Chassis,
                            leftCircuit);
                    menu.Items.Add(selectLeft);

                    WpfMenuItem removeLeft = new WpfMenuItem
                    {
                        Header = $"🔗  Pick Fixtures to Remove — " +
                            $"{leftLabel}"
                    };
                    removeLeft.Click += (s, e) =>
                        OnRemoveFixtures(tag.Board, tag.Chassis,
                            leftCircuit);
                    menu.Items.Add(removeLeft);
                }
            }
            else if (!tag.LeftConsumed)
            {
                WpfMenuItem assignLeftSpare = new WpfMenuItem
                {
                    Header = $"📌  Assign Fixtures — " +
                        $"{tag.Left.DisplayLabel} (spare)"
                };
                assignLeftSpare.Click += (s, e) =>
                    OnAssignFixturesToSpare(tag.Board, tag.Chassis,
                        tag.Left);
                menu.Items.Add(assignLeftSpare);
            }
            else
            {
                WpfMenuItem leftInfo = new WpfMenuItem
                {
                    Header = $"⬅  {tag.Left.DisplayLabel} " +
                        $"(part of multi-pole)",
                    IsEnabled = false
                };
                menu.Items.Add(leftInfo);
            }

            menu.Items.Add(new Separator());

            // ── Right side ──
            CircuitData? rightCircuit;
            string rightLabel;

            if (tag.RightConsumed && tag.RightConsumedInfo != null)
            {
                rightCircuit = tag.RightConsumedInfo.Parent;
                rightLabel = rightCircuit.DisplayLabel;
            }
            else
            {
                rightLabel = tag.Right.DisplayLabel;
                rightCircuit = tag.Chassis.Circuits
                    .FirstOrDefault(c =>
                        c.DisplayLabel == rightLabel);
            }

            if (rightCircuit != null
                && !string.IsNullOrEmpty(rightCircuit.Description))
            {
                string suffix = tag.RightConsumed
                    ? $" (3φ from {rightLabel})" : "";

                WpfMenuItem deleteRight = new WpfMenuItem
                {
                    Header = $"🗑️  Delete Right — " +
                        $"{rightLabel}{suffix}"
                };
                deleteRight.Click += (s, e) =>
                    OnDeleteCircuit(tag.Chassis, rightCircuit);
                menu.Items.Add(deleteRight);

                WpfMenuItem assignRight = new WpfMenuItem
                {
                    Header = $"📌  Assign Fixtures — {rightLabel}"
                };
                assignRight.Click += (s, e) =>
                    OnAssignFixtures(tag.Board, tag.Chassis,
                        rightCircuit);
                menu.Items.Add(assignRight);

                if (rightCircuit.Fixtures.Count > 0)
                {
                    WpfMenuItem selectRight = new WpfMenuItem
                    {
                        Header = $"👁️  Select in Model — " +
                            $"{rightLabel} " +
                            $"({rightCircuit.Fixtures.Count})"
                    };
                    selectRight.Click += (s, e) =>
                        OnSelectFixtures(tag.Board, tag.Chassis,
                            rightCircuit);
                    menu.Items.Add(selectRight);

                    WpfMenuItem removeRight = new WpfMenuItem
                    {
                        Header = $"🔗  Pick Fixtures to Remove — " +
                            $"{rightLabel}"
                    };
                    removeRight.Click += (s, e) =>
                        OnRemoveFixtures(tag.Board, tag.Chassis,
                            rightCircuit);
                    menu.Items.Add(removeRight);
                }
            }
            else if (!tag.RightConsumed)
            {
                WpfMenuItem assignRightSpare = new WpfMenuItem
                {
                    Header = $"📌  Assign Fixtures — " +
                        $"{tag.Right.DisplayLabel} (spare)"
                };
                assignRightSpare.Click += (s, e) =>
                    OnAssignFixturesToSpare(tag.Board, tag.Chassis,
                        tag.Right);
                menu.Items.Add(assignRightSpare);
            }
            else
            {
                WpfMenuItem rightInfo = new WpfMenuItem
                {
                    Header = $"➡  {tag.Right.DisplayLabel} " +
                        $"(part of multi-pole)",
                    IsEnabled = false
                };
                menu.Items.Add(rightInfo);
            }

            return menu;
        }

        private void OnAssignFixturesToSpare(
    BoardData board, ChassisData chassis,
    PoleAssignment pole)
        {
            PushUndoState();

            CircuitData newCircuit = new CircuitData
            {
                CircuitGuid = Guid.NewGuid(),
                DisplayLabel = pole.DisplayLabel,
                CircuitNumber = pole.CircuitNumber,
                Poles = 1
            };

            chassis.Circuits.Add(newCircuit);

            OnAssignFixtures(board, chassis, newCircuit);
        }

        // ─── Delete Single Circuit ───────────────────────────

        private void OnDeleteCircuit(
            ChassisData chassis, CircuitData circuit)
        {
            int fixtureCount = circuit.Fixtures?.Count ?? 0;

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

                if (result == MessageBoxResult.Yes)
                {
                    // TODO: circuit reassignment picker
                    StatusLabel.Text =
                        "Fixture reassignment not yet implemented. " +
                        "Fixtures will be unassigned.";
                }

                // TODO: clear fixture ES data when implemented
            }
            else
            {
                MessageBoxResult confirm = MessageBox.Show(
                    $"Delete circuit {circuit.DisplayLabel} " +
                    $"({circuit.Description})?\n\n" +
                    "This will clear all data and make it spare.",
                    "Delete Circuit",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes) return;
            }

            // If multi-pole, also free consumed poles
            if (circuit.Poles > 1)
            {
                List<PoleAssignment> assignments =
                    CircuitLabelCalculator.Calculate(chassis);

                PoleAssignment? startPole = assignments
                    .FirstOrDefault(a =>
                        a.DisplayLabel == circuit.DisplayLabel);

                if (startPole != null)
                {
                    // Consumed poles are already just references
                    // in the dictionary — removing the parent
                    // circuit removes them automatically on refresh
                }
            }

            PushUndoState();
            chassis.Circuits.Remove(circuit);
            SaveAndRefresh();
        }

        // ─── Clear All Circuits in Chassis ───────────────────

        private void OnClearAllCircuits(ChassisData chassis)
        {
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

            // TODO: clear fixture ES data when implemented

            PushUndoState();
            chassis.Circuits.Clear();
            SaveAndRefresh();

            StatusLabel.Text =
                $"Cleared {totalCircuits} circuit(s) from " +
                $"'{chassis.Name}'.";
        }
        // ─── Board Actions ───────────────────────────────────────

        private void OnRenameBoard(BoardData board)
        {
            RenameDialog dialog = new RenameDialog(
                "Rename Board", board.DisplayName);
            dialog.ShowDialog();
            if (!dialog.Confirmed) return;
            PushUndoState();
            board.DisplayName = dialog.NewName;
            SaveAndRefresh();
        }

        private void OnDeleteBoard(BoardData board)
        {
            MessageBoxResult result = MessageBox.Show(
                $"Delete board '{board.DisplayName}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
            PushUndoState();
            _boards.Remove(board);
            SaveAndRefresh();
        }

        // ─── Chassis Actions ─────────────────────────────────────

        private int CalculateSuggestedStart(BoardData board)
        {
            if (board.Chassis.Count == 0) return 1;
            ChassisData last = board.Chassis[board.Chassis.Count - 1];
            int phaseCount = last.PhaseLabels?.Count ?? 0;
            if (phaseCount <= 0)
                return last.StartingCircuit + last.MaxPoles;
            return last.StartingCircuit
                + (last.MaxPoles / phaseCount);
        }

        private void OnAddChassis(BoardData board)
        {
            int suggestedStart = CalculateSuggestedStart(board);
            bool isThreePhase =
                board.Supply == SupplyType.ThreePhase;
            bool allowThreePhase =
                board.Supply == SupplyType.ThreePhase;

            ChassisConfigDialog dialog = new ChassisConfigDialog(
                existing: null,
                suggestedStart: suggestedStart,
                isThreePhase: isThreePhase,
                allowThreePhase: allowThreePhase);
            dialog.ShowDialog();
            if (!dialog.Confirmed || dialog.Result == null) return;
            PushUndoState();
            board.Chassis.Add(dialog.Result);
            SaveAndRefresh();
        }

        private void OnEditChassis(
            BoardData board, ChassisData chassis)
        {
            bool isThreePhase =
                board.Supply == SupplyType.ThreePhase;
            bool allowThreePhase =
                board.Supply == SupplyType.ThreePhase;

            ChassisConfigDialog dialog = new ChassisConfigDialog(
                chassis, chassis.StartingCircuit, isThreePhase,
                allowThreePhase);
            dialog.ShowDialog();
            if (!dialog.Confirmed || dialog.Result == null) return;
            int index = board.Chassis.IndexOf(chassis);
            PushUndoState();
            if (index >= 0) board.Chassis[index] = dialog.Result;
            SaveAndRefresh();
        }

        private void OnDeleteChassis(
            BoardData board, ChassisData chassis)
        {
            MessageBoxResult result = MessageBox.Show(
                $"Delete chassis '{chassis.Name}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
            PushUndoState();
            board.Chassis.Remove(chassis);
            SaveAndRefresh();
        }

        // ─── Bulk Edit ───────────────────────────────────────────

        private void OnBulkEditChassis(
            BoardData board, ChassisData chassis)
        {
            BulkChassisEditDialog dialog =
                new BulkChassisEditDialog(chassis.Name);
            dialog.ShowDialog();
            if (!dialog.Confirmed) return;

            List<PoleAssignment> assignments =
                CircuitLabelCalculator.Calculate(chassis);
            Dictionary<string, ConsumedPoleInfo> consumedPoles =
                BuildConsumedPoles(chassis, assignments);

            foreach (PoleAssignment assignment in assignments)
            {
                if (consumedPoles.ContainsKey(
                    assignment.DisplayLabel)) continue;
                if (dialog.Scope == BulkApplyScope.LeftOnly
                    && assignment.Side != Side.Left) continue;
                if (dialog.Scope == BulkApplyScope.RightOnly
                    && assignment.Side != Side.Right) continue;

                CircuitData? existing = chassis.Circuits
                    .FirstOrDefault(c =>
                        c.DisplayLabel ==
                        assignment.DisplayLabel);

                if (dialog.Scope == BulkApplyScope.SpareOnly
                    && existing != null
                    && !string.IsNullOrEmpty(
                        existing.Description)) continue;

                if (existing == null)
                {
                    existing = new CircuitData
                    {
                        CircuitGuid = Guid.NewGuid(),
                        DisplayLabel = assignment.DisplayLabel,
                        CircuitNumber = assignment.CircuitNumber
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

            // After: if (!dialog.Confirmed) return;
            PushUndoState();

            // ... existing bulk edit logic stays ...

            SaveAndRefresh();
        }

        // ─── Fixture Actions ─────────────────────────────────

        private void OnAssignFixtures(
            BoardData board, ChassisData chassis,
            CircuitData circuit)
        {
            PushUndoState();

            Circuiting.Application.EventHandler.CurrentAction =
                Commands.RevitAction.AssignFixtures;
            Circuiting.Application.EventHandler.BoardsToSave =
                _boards;
            Circuiting.Application.EventHandler.AssignRequest =
                new Commands.AssignFixturesRequest
                {
                    BoardGuid = board.BoardGuid,
                    ChassisGuid = chassis.ChassisGuid,
                    CircuitDisplayLabel = circuit.DisplayLabel
                };
            Circuiting.Application.RevitEvent.Raise();

            StatusLabel.Text =
                $"Pick fixtures for {circuit.DisplayLabel}...";
        }

        private void OnSelectFixtures(
            BoardData board, ChassisData chassis,
            CircuitData circuit)
        {
            Circuiting.Application.EventHandler.CurrentAction =
                Commands.RevitAction.SelectFixtures;
            Circuiting.Application.EventHandler.BoardsToSave =
                _boards;
            Circuiting.Application.EventHandler.AssignRequest =
                new Commands.AssignFixturesRequest
                {
                    BoardGuid = board.BoardGuid,
                    ChassisGuid = chassis.ChassisGuid,
                    CircuitDisplayLabel = circuit.DisplayLabel
                };
            Circuiting.Application.RevitEvent.Raise();
        }

        private void OnRemoveFixtures(
            BoardData board, ChassisData chassis,
            CircuitData circuit)
        {
            int count = circuit.Fixtures.Count;

            MessageBoxResult result = MessageBox.Show(
                $"Remove all {count} fixture(s) from " +
                $"{circuit.DisplayLabel}?\n\n" +
                "The fixtures will remain in the model " +
                "but will no longer be assigned to this circuit.",
                "Remove Fixtures",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            PushUndoState();

            Circuiting.Application.EventHandler.CurrentAction =
                Commands.RevitAction.RemoveFixtures;
            Circuiting.Application.EventHandler.BoardsToSave =
                _boards;
            Circuiting.Application.EventHandler.AssignRequest =
                new Commands.AssignFixturesRequest
                {
                    BoardGuid = board.BoardGuid,
                    ChassisGuid = chassis.ChassisGuid,
                    CircuitDisplayLabel = circuit.DisplayLabel
                };
            Circuiting.Application.RevitEvent.Raise();
        }

        // ─── Toolbar Handlers ────────────────────────────────────

        private void AddBoardButton_Click(
            object sender, RoutedEventArgs e)
        {
            AddBoardDialog dialog = new AddBoardDialog();
            dialog.ShowDialog();
            if (!dialog.Confirmed) return;

            BoardData board = new BoardData
            {
                BoardGuid = Guid.NewGuid(),
                DisplayName = dialog.BoardName,
                Supply = dialog.IsThreePhase
                    ? SupplyType.ThreePhase
                    : SupplyType.SinglePhase
            };

            ChassisData defaultChassis = new ChassisData
            {
                ChassisGuid = Guid.NewGuid(),
                Name = "Main",
                MaxPoles = 24,
                StartingCircuit = 1,
                Prefix = string.Empty,
                Separator = string.Empty,
                ShowPhaseLabel =
                    board.Supply == SupplyType.ThreePhase,
                PhaseLabels =
                    board.Supply == SupplyType.ThreePhase
                    ? new List<string> { "R", "W", "B" }
                    : new List<string>()
            };

            PushUndoState();
            board.Chassis.Add(defaultChassis);
            _boards.Add(board);
            SaveAndRefresh();
        }

        private void UndoButton_Click(
            object sender, RoutedEventArgs e)
        {
            Undo();
        }

        private void RedoButton_Click(
            object sender, RoutedEventArgs e)
        {
            Redo();
        }

        private void RefreshButton_Click(
            object sender, RoutedEventArgs e)
        {
            RefreshTree();
        }

        private void ExpandAllButton_Click(
            object sender, RoutedEventArgs e)
        {
            foreach (object obj in BoardTree.Items)
            {
                if (obj is TreeViewItem item)
                    ExpandAll(item);
            }
        }

        private void CollapseAllButton_Click(
            object sender, RoutedEventArgs e)
        {
            foreach (object obj in BoardTree.Items)
            {
                if (obj is TreeViewItem item)
                    CollapseAll(item);
            }
        }

        private void ExpandAll(TreeViewItem item)
        {
            item.IsExpanded = true;
            foreach (object child in item.Items)
            {
                if (child is TreeViewItem childItem)
                    ExpandAll(childItem);
            }
        }

        private void CollapseAll(TreeViewItem item)
        {
            item.IsExpanded = false;
            foreach (object child in item.Items)
            {
                if (child is TreeViewItem childItem)
                    CollapseAll(childItem);
            }
        }

        private void ModeToggle_Checked(
            object sender, RoutedEventArgs e)
        {
            AutoModeLabel.Text = "MANUAL";
            AutoModeLabel.Foreground = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(255, 165, 0));
        }

        private void ModeToggle_Unchecked(
            object sender, RoutedEventArgs e)
        {
            AutoModeLabel.Text = "AUTO";
            AutoModeLabel.Foreground = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(78, 201, 78));
        }
    }
}
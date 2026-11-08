using Circuiting.Models;
using Circuiting.Services;
using CommunityToolkit.Mvvm.Input;

namespace Circuiting.ViewModels
{
    public sealed class ConsumedPoleInfo
    {
        public CircuitData Parent { get; set; } = null!;
        public bool IsDescriptionRow { get; set; }
    }

    public sealed class CircuitRowViewModel : CircuitTreeNode
    {
        public BoardData Board { get; set; } = null!;
        public ChassisData Chassis { get; set; } = null!;
        public PoleAssignment Left { get; set; } = null!;
        public PoleAssignment Right { get; set; } = null!;
        public CircuitData? LeftCircuit { get; set; }
        public CircuitData? RightCircuit { get; set; }
        public ConsumedPoleInfo? LeftConsumedInfo { get; set; }
        public ConsumedPoleInfo? RightConsumedInfo { get; set; }
        public CircuitSideViewModel LeftDisplay { get; set; } = null!;
        public CircuitSideViewModel RightDisplay { get; set; } = null!;
        public CircuitSideCommands? LeftCommands { get; set; }
        public CircuitSideCommands? RightCommands { get; set; }
        public IRelayCommand? EditCommand { get; set; }
        /// <summary>Circuited elements of the left then right circuit started on this row.</summary>
        public List<FixtureTreeNode> Fixtures { get; set; } = new List<FixtureTreeNode>();
        public bool HasFixtures => Fixtures.Count > 0;
        public bool LeftConsumed => LeftConsumedInfo != null;
        public bool RightConsumed => RightConsumedInfo != null;
        /// <summary>GUID of the circuit on each side (the parent circuit for a consumed pole).</summary>
        public Guid? LeftKey => (LeftConsumedInfo?.Parent ?? LeftCircuit)?.CircuitGuid;
        public Guid? RightKey => (RightConsumedInfo?.Parent ?? RightCircuit)?.CircuitGuid;
        public TreeSelection? Selection { get; set; }

        private bool _isLeftSelected;
        public bool IsLeftSelected
        {
            get => _isLeftSelected;
            set => SetProperty(ref _isLeftSelected, value);
        }

        private bool _isRightSelected;
        public bool IsRightSelected
        {
            get => _isRightSelected;
            set => SetProperty(ref _isRightSelected, value);
        }
    }

    public sealed class CircuitSideViewModel
    {
        public string Label { get; private set; } = string.Empty;
        public string Description { get; private set; } = string.Empty;
        public string Protection { get; private set; } = string.Empty;
        public string Breaker { get; private set; } = string.Empty;
        /// <summary>Trip curve and setting for the breaker cell's tooltip; null when neither is set.</summary>
        public string? TripToolTip { get; private set; }
        /// <summary>True for unoccupied positions (spare or space); they are shown greyed out.</summary>
        public bool IsSpare { get; private set; }

        internal static CircuitSideViewModel Create(
            PoleAssignment pole, CircuitData? circuit, ConsumedPoleInfo? consumed)
        {
            var side = new CircuitSideViewModel { Label = pole.DisplayLabel };
            if (consumed != null)
            {
                circuit = consumed.Parent;
                side.IsSpare = !CircuitLayout.IsOccupied(circuit);
                if (!consumed.IsDescriptionRow) return side;
            }
            else if (circuit == null)
            {
                side.Description = "Space";
                side.IsSpare = true;
                return side;
            }
            else if (circuit.Poles > 1)
            {
                side.IsSpare = !CircuitLayout.IsOccupied(circuit);
                return side;
            }

            if (CircuitLayout.IsOccupied(circuit))
            {
                string description = string.IsNullOrWhiteSpace(circuit.Description)
                    ? "(no description)" : circuit.Description;
                side.Description = $"{description} [{circuit.Fixtures.Count}]";
            }
            else
            {
                side.IsSpare = true;
                if (!CircuitLayout.HasDetails(circuit))
                {
                    side.Description = "Space";
                    return side;
                }
                side.Description = string.IsNullOrWhiteSpace(circuit.Description)
                    ? "Spare" : $"Spare — {circuit.Description}";
            }

            side.Breaker = CircuitLayout.BreakerText(circuit);
            var trip = new List<string>();
            if (!string.IsNullOrEmpty(circuit.TripCurve)) trip.Add($"Trip curve: {circuit.TripCurve}");
            if (!string.IsNullOrWhiteSpace(circuit.TripSetting)) trip.Add($"Trip setting: {circuit.TripSetting.Trim()}");
            side.TripToolTip = trip.Count == 0 ? null : string.Join("\n", trip);
            if (!string.IsNullOrEmpty(circuit.ProtectionType))
                side.Protection = circuit.ProtectionType == "MCB" || string.IsNullOrEmpty(circuit.EarthLeakage)
                    ? circuit.ProtectionType : $"{circuit.EarthLeakage} {circuit.ProtectionType}";
            return side;
        }
    }

    public static class CircuitTreeProjection
    {
        public static List<BoardTreeNode> Build(
            IEnumerable<BoardData> boards, ICircuitTreeActions actions, ISet<Guid> expanded)
        {
            return boards.Select(board => new BoardTreeNode(board, actions, expanded)).ToList();
        }

        /// <summary>
        /// Builds only the boards, chassis and circuit rows that match <paramref name="search"/>.
        /// A matching board or chassis keeps all of its contents; otherwise only rows holding a
        /// matching circuit (including every pole of a multi-pole circuit) are kept. Ancestors of
        /// matches are expanded; matched boards' chassis keep their <paramref name="expanded"/> state.
        /// </summary>
        public static List<BoardTreeNode> Build(IEnumerable<BoardData> boards, ICircuitTreeActions actions,
            ISet<Guid> expanded, CircuitSearch search, out int matchCount)
        {
            matchCount = 0;
            if (!search.IsActive) return Build(boards, actions, expanded);
            var nodes = new List<BoardTreeNode>();
            var matchedCircuits = new HashSet<CircuitData>();
            foreach (BoardData board in boards)
            {
                if (search.MatchesBoard(board))
                {
                    matchCount++;
                    nodes.Add(new BoardTreeNode(board, actions, expanded) { IsExpanded = true, IsSearchMatch = true });
                    continue;
                }
                var chassisNodes = new List<ChassisTreeNode>();
                foreach (ChassisData chassis in board.Chassis)
                {
                    if (search.MatchesChassis(chassis))
                    {
                        matchCount++;
                        chassisNodes.Add(new ChassisTreeNode(board, chassis, actions, expanded) { IsSearchMatch = true });
                        continue;
                    }
                    var rows = new List<CircuitRowViewModel>();
                    foreach (CircuitRowViewModel row in BuildRows(board, chassis, actions, expanded))
                    {
                        bool left = Matches(row.LeftConsumedInfo?.Parent ?? row.LeftCircuit, search, matchedCircuits);
                        bool right = Matches(row.RightConsumedInfo?.Parent ?? row.RightCircuit, search, matchedCircuits);
                        if (!left && !right) continue;
                        row.IsSearchMatch = true;
                        rows.Add(row);
                    }
                    if (rows.Count > 0) chassisNodes.Add(new ChassisTreeNode(board, chassis, actions, true, rows));
                }
                if (chassisNodes.Count > 0) nodes.Add(new BoardTreeNode(board, actions, true, chassisNodes));
            }
            matchCount += matchedCircuits.Count;
            return nodes;
        }

        private static bool Matches(CircuitData? circuit, CircuitSearch search, HashSet<CircuitData> matched)
        {
            if (circuit == null || !search.MatchesCircuit(circuit)) return false;
            matched.Add(circuit);
            return true;
        }

        /// <summary>
        /// Builds the rows of a chassis. A row with circuited elements has them as children and is expanded
        /// when the GUID of its left or right circuit is in <paramref name="expanded"/>.
        /// </summary>
        public static List<CircuitRowViewModel> BuildRows(
            BoardData board, ChassisData chassis, ICircuitTreeActions? actions = null, ISet<Guid>? expanded = null)
        {
            List<PoleAssignment> assignments = CircuitLabelCalculator.Calculate(board, chassis);
            Dictionary<int, CircuitData> consumed = CircuitLayout.GetConsumedPoles(chassis);
            Dictionary<int, CircuitData> circuits = chassis.Circuits.ToDictionary(
                circuit => circuit.PoleIndex ?? throw new InvalidOperationException("A circuit has no pole position."));
            var rows = new List<CircuitRowViewModel>();

            for (int row = 0; row < chassis.MaxPoles / 2; row++)
            {
                PoleAssignment left = assignments[row * 2];
                PoleAssignment right = assignments[row * 2 + 1];
                consumed.TryGetValue(left.PoleIndex, out CircuitData? leftParent);
                consumed.TryGetValue(right.PoleIndex, out CircuitData? rightParent);
                circuits.TryGetValue(left.PoleIndex, out CircuitData? leftCircuit);
                circuits.TryGetValue(right.PoleIndex, out CircuitData? rightCircuit);
                ConsumedPoleInfo? leftInfo = Info(leftParent, left.PoleIndex);
                ConsumedPoleInfo? rightInfo = Info(rightParent, right.PoleIndex);
                CircuitData? leftStart = leftParent == null ? leftCircuit : null;
                CircuitData? rightStart = rightParent == null ? rightCircuit : null;
                var rowModel = new CircuitRowViewModel
                {
                    Board = board,
                    Chassis = chassis,
                    Left = left,
                    Right = right,
                    LeftCircuit = leftStart,
                    RightCircuit = rightStart,
                    LeftConsumedInfo = leftInfo,
                    RightConsumedInfo = rightInfo,
                    LeftDisplay = CircuitSideViewModel.Create(left, leftStart, leftInfo),
                    RightDisplay = CircuitSideViewModel.Create(right, rightStart, rightInfo)
                };
                rowModel.Fixtures = FixtureNodes(board, chassis, leftStart, Side.Left, actions)
                    .Concat(FixtureNodes(board, chassis, rightStart, Side.Right, actions)).ToList();
                rowModel.IsExpanded = rowModel.HasFixtures && expanded != null &&
                    ((leftStart != null && expanded.Contains(leftStart.CircuitGuid)) ||
                     (rightStart != null && expanded.Contains(rightStart.CircuitGuid)));
                if (actions != null)
                {
                    rowModel.LeftCommands = new CircuitSideCommands(board, chassis, left, leftStart, leftInfo, actions);
                    rowModel.RightCommands = new CircuitSideCommands(board, chassis, right, rightStart, rightInfo, actions);
                    rowModel.EditCommand = new RelayCommand(() => actions.EditRow(rowModel));
                    rowModel.Selection = actions.Selection;
                }
                rows.Add(rowModel);
            }
            return rows;
        }

        private static IEnumerable<FixtureTreeNode> FixtureNodes(BoardData board, ChassisData chassis,
            CircuitData? circuit, Side side, ICircuitTreeActions? actions)
        {
            if (circuit == null) return Enumerable.Empty<FixtureTreeNode>();
            return circuit.Fixtures
                .Select(fixture => new FixtureTreeNode(board, chassis, circuit, fixture, side, actions))
                .OrderBy(node => node.Space, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(node => node.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static ConsumedPoleInfo? Info(CircuitData? parent, int poleIndex) =>
            parent == null ? null : new ConsumedPoleInfo
            {
                Parent = parent,
                IsDescriptionRow = poleIndex == parent.PoleIndex + 2
            };
    }
}

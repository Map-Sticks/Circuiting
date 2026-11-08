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
        public bool LeftConsumed => LeftConsumedInfo != null;
        public bool RightConsumed => RightConsumedInfo != null;
    }

    public sealed class CircuitSideViewModel
    {
        public string Label { get; private set; } = string.Empty;
        public string Description { get; private set; } = string.Empty;
        public string Protection { get; private set; } = string.Empty;
        public string Breaker { get; private set; } = string.Empty;
        public bool IsSpare { get; private set; }

        internal static CircuitSideViewModel Create(
            PoleAssignment pole, CircuitData? circuit, ConsumedPoleInfo? consumed)
        {
            var side = new CircuitSideViewModel { Label = pole.DisplayLabel };
            if (consumed != null)
            {
                if (!consumed.IsDescriptionRow) return side;
                circuit = consumed.Parent;
                side.Description = circuit.Description ?? string.Empty;
            }
            else if (circuit == null)
            {
                side.Description = "Spare";
                side.IsSpare = true;
                return side;
            }
            else
            {
                if (circuit.Poles > 1) return side;
                side.Description = string.IsNullOrEmpty(circuit.Description) ? "Spare" : circuit.Description;
                if (circuit.Fixtures.Count > 0) side.Description += $" [{circuit.Fixtures.Count}]";
            }

            side.Breaker = circuit.BreakerSize ?? string.Empty;
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
                    foreach (CircuitRowViewModel row in BuildRows(board, chassis, actions))
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

        public static List<CircuitRowViewModel> BuildRows(
            BoardData board, ChassisData chassis, ICircuitTreeActions? actions = null)
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
                if (actions != null)
                {
                    rowModel.LeftCommands = new CircuitSideCommands(board, chassis, left, leftStart, leftInfo, actions);
                    rowModel.RightCommands = new CircuitSideCommands(board, chassis, right, rightStart, rightInfo, actions);
                    rowModel.EditCommand = new RelayCommand(() => actions.EditRow(rowModel));
                }
                rows.Add(rowModel);
            }
            return rows;
        }

        private static ConsumedPoleInfo? Info(CircuitData? parent, int poleIndex) =>
            parent == null ? null : new ConsumedPoleInfo
            {
                Parent = parent,
                IsDescriptionRow = poleIndex == parent.PoleIndex + 2
            };
    }
}

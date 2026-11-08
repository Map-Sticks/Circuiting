using Circuiting.Models;
using Circuiting.Services;

namespace Circuiting.ViewModels
{
    public sealed class MoveBoardOption
    {
        public BoardData Board { get; internal set; } = null!;
        public int Available { get; internal set; }
        public bool IsSource { get; internal set; }
        public string Header => $"{Board.DisplayName}  ({Available} available)" + (IsSource ? "  • current" : string.Empty);
    }

    public sealed class MoveChassisOption
    {
        public BoardData Board { get; internal set; } = null!;
        public ChassisData Chassis { get; internal set; } = null!;
        public int Available { get; internal set; }
        public bool IsSource { get; internal set; }
        public string Header
        {
            get
            {
                string phase = Chassis.PhaseLabels.Count >= 3 ? "3φ" : "1φ";
                return $"{Chassis.Name}  ({phase}, {Available} available)" + (IsSource ? "  • current" : string.Empty);
            }
        }
    }

    public sealed class MovePositionOption
    {
        public BoardData Board { get; internal set; } = null!;
        public ChassisData Chassis { get; internal set; } = null!;
        public PoleAssignment Position { get; internal set; } = null!;
        public string Label => Position.DisplayLabel;
        public string Side => Position.Side == Services.Side.Left ? "Left" : "Right";
        public int Row => Position.RowIndex + 1;
        public string Contents { get; internal set; } = string.Empty;
        public string? Error { get; internal set; }
        public bool IsCurrent { get; internal set; }
        public bool ReplacesSpare { get; internal set; }
        /// <summary>Element moves only: the destination is a spare or space and becomes a new circuit.</summary>
        public bool CreatesCircuit { get; internal set; }
        /// <summary>Bulk circuit moves only: where each selected circuit lands when starting here.</summary>
        public BulkMovePlan? BulkPlan { get; internal set; }
        /// <summary>Element moves only: how the element would be placed at this position.</summary>
        public FixtureMoveTarget? FixtureTarget { get; internal set; }
        public bool IsAvailable => Error == null && !IsCurrent;
        public string Status => IsCurrent ? (FixtureTarget != null ? "Current circuit" : "Current position")
            : Error != null ? "Unavailable"
            : FixtureTarget?.Kind == FixtureMoveKind.NewCircuit ? "New circuit"
            : FixtureTarget?.Kind == FixtureMoveKind.FillSpare ? "Spare → circuit"
            : FixtureTarget != null ? "Add to circuit"
            : ReplacesSpare ? "Replaces spare" : "Available";
        public string ToolTip => Error ?? (ReplacesSpare
            ? "The spare's saved details will be deleted after confirmation."
            : FixtureTarget?.Kind == FixtureMoveKind.NewCircuit
                ? "A new circuit is created here; you can enter its description and protection."
            : FixtureTarget?.Kind == FixtureMoveKind.FillSpare
                ? "The spare becomes a circuit; its saved details are kept and can be amended."
            : Status);
        public string Note => BulkPlan is { IsValid: true } plan && plan.Placements.Count > 0
                ? $" — {plan.Placements.Count} circuits to {plan.Placements[0].Position.DisplayLabel}" +
                  (plan.Placements.Count > 1 ? $"…{plan.Placements[plan.Placements.Count - 1].Position.DisplayLabel}" : string.Empty) +
                  (ReplacesSpare ? ", replaces spares" : string.Empty)
            : ReplacesSpare ? " — replaces spare"
            : FixtureTarget?.Kind == FixtureMoveKind.NewCircuit ? " — creates a new circuit"
            : FixtureTarget?.Kind == FixtureMoveKind.FillSpare ? " — spare becomes a circuit"
            : FixtureTarget?.Kind == FixtureMoveKind.AddToCircuit && FixtureTarget.Circuit != null
                ? $" — joins {FixtureTarget.Circuit.DisplayLabel} ({FixtureTarget.Circuit.Fixtures.Count} element(s))"
            : string.Empty;
        public string Summary => $"{Board.DisplayName} / {Chassis.Name} / {Label} ({Side}, row {Row})";
    }

    /// <summary>
    /// Destination picker for moving a circuit, or a single circuited element: choose a board, then a chassis,
    /// then a position. Circuit moves are validated with <see cref="CircuitLayout.GetMoveErrors"/>;
    /// element moves are classified with <see cref="FixtureMover.GetTargets"/>.
    /// </summary>
    public sealed class CircuitMovePicker
    {
        private readonly Dictionary<ChassisData, List<MovePositionOption>> _positions =
            new Dictionary<ChassisData, List<MovePositionOption>>();

        public CircuitMovePicker(IEnumerable<BoardData> boards, BoardData sourceBoard,
            ChassisData source, CircuitData circuit)
            : this(boards, new[] { new CircuitRef { Board = sourceBoard, Chassis = source, Circuit = circuit } },
                Array.Empty<FixtureRef>())
        {
        }

        /// <summary>Picker for moving several circuits: the first lands on the chosen position, the rest follow.</summary>
        public CircuitMovePicker(IEnumerable<BoardData> boards, IReadOnlyList<CircuitRef> circuits)
            : this(boards, circuits, Array.Empty<FixtureRef>())
        {
        }

        /// <summary>Picker for moving one or more circuited elements to a single destination circuit.</summary>
        public CircuitMovePicker(IEnumerable<BoardData> boards, IReadOnlyList<FixtureRef> fixtures)
            : this(boards, Array.Empty<CircuitRef>(), fixtures)
        {
        }

        private CircuitMovePicker(IEnumerable<BoardData> boards, IReadOnlyList<CircuitRef> circuits,
            IReadOnlyList<FixtureRef> fixtures)
        {
            if (circuits.Count == 0 && fixtures.Count == 0)
                throw new ArgumentException("Nothing was selected to move.");
            Circuits = circuits;
            Fixtures = fixtures;
            SourceBoard = fixtures.Count > 0 ? fixtures[0].Board : circuits[0].Board;
            Source = fixtures.Count > 0 ? fixtures[0].Chassis : circuits[0].Chassis;
            Circuit = fixtures.Count > 0 ? fixtures[0].Circuit : circuits[0].Circuit;
            if (fixtures.Count > 0)
            {
                CurrentCircuit = fixtures.All(f => ReferenceEquals(f.Circuit, Circuit)) ? Circuit : null;
                if (CurrentCircuit != null) _moving.Add(CurrentCircuit);
            }
            else
            {
                foreach (CircuitRef selected in circuits) _moving.Add(selected.Circuit);
            }
            foreach (BoardData board in boards)
            {
                var chassisOptions = new List<MoveChassisOption>();
                foreach (ChassisData chassis in board.Chassis)
                {
                    List<MovePositionOption> positions = BuildPositions(board, chassis);
                    _positions[chassis] = positions;
                    chassisOptions.Add(new MoveChassisOption
                    {
                        Board = board, Chassis = chassis, IsSource = ReferenceEquals(chassis, Source),
                        Available = positions.Count(p => p.IsAvailable)
                    });
                }
                ChassisByBoard[board] = chassisOptions;
                Boards.Add(new MoveBoardOption
                {
                    Board = board, IsSource = ReferenceEquals(board, SourceBoard),
                    Available = chassisOptions.Sum(c => c.Available)
                });
            }
        }

        private readonly HashSet<CircuitData> _moving = new HashSet<CircuitData>();
        public BoardData SourceBoard { get; }
        public ChassisData Source { get; }
        /// <summary>The (first) circuit being moved, or the circuit of the (first) element being moved.</summary>
        public CircuitData Circuit { get; }
        public IReadOnlyList<CircuitRef> Circuits { get; }
        public IReadOnlyList<FixtureRef> Fixtures { get; }
        /// <summary>Element moves: the circuit holding every moved element, or null when they come from several.</summary>
        public CircuitData? CurrentCircuit { get; }
        public bool IsElementMove => Fixtures.Count > 0;
        public bool IsBulkCircuitMove => Circuits.Count > 1;
        public string Title => IsElementMove ? (Fixtures.Count > 1 ? "Move Elements to Circuit" : "Move Element to Circuit")
            : IsBulkCircuitMove ? "Move Circuits" : "Move Circuit";
        public List<MoveBoardOption> Boards { get; } = new List<MoveBoardOption>();
        private Dictionary<BoardData, List<MoveChassisOption>> ChassisByBoard { get; } =
            new Dictionary<BoardData, List<MoveChassisOption>>();
        public int TotalAvailable => Boards.Sum(b => b.Available);

        public string CircuitSummary
        {
            get
            {
                if (Fixtures.Count > 1)
                {
                    int circuits = Fixtures.Select(f => f.Circuit).Distinct().Count();
                    return $"Move {Fixtures.Count} elements (from {circuits} circuit(s)) to one circuit. " +
                        "Choosing a spare or space creates a new circuit.";
                }
                if (IsElementMove)
                {
                    CircuitedFixture fixture = Fixtures[0].Fixture;
                    string name = string.IsNullOrWhiteSpace(fixture.FamilyName) ? "element" : fixture.FamilyName;
                    return $"Move {name} from {Circuit.DisplayLabel} on {SourceBoard.DisplayName} / {Source.Name} " +
                        "to another circuit. Choosing a spare or space creates a new circuit.";
                }
                if (IsBulkCircuitMove)
                {
                    string labels = string.Join(", ", Circuits.Take(8).Select(c => c.Circuit.DisplayLabel)) +
                        (Circuits.Count > 8 ? ", ..." : string.Empty);
                    return $"Move {Circuits.Count} circuits ({labels}). The first lands on the chosen position; " +
                        "the others fill the next free positions in order.";
                }
                string description = string.IsNullOrWhiteSpace(Circuit.Description) ? string.Empty
                    : $" — {Circuit.Description}";
                string poles = Circuit.Poles > 1 ? $" ({Circuit.Poles}-pole)" : string.Empty;
                return $"Move {Circuit.DisplayLabel}{description}{poles} from {SourceBoard.DisplayName} / {Source.Name}";
            }
        }

        /// <summary>Boards whose name, or one of whose chassis names, contains every word of <paramref name="filter"/>.</summary>
        public List<MoveBoardOption> FilterBoards(string? filter)
        {
            string[] words = Words(filter);
            if (words.Length == 0) return Boards.ToList();
            return Boards.Where(option => words.All(word => Contains(option.Board.DisplayName, word) ||
                option.Board.Chassis.Any(chassis => Contains(chassis.Name, word)))).ToList();
        }

        public List<MoveChassisOption> ChassisFor(BoardData board) =>
            ChassisByBoard.TryGetValue(board, out List<MoveChassisOption>? options)
                ? options.ToList() : new List<MoveChassisOption>();

        /// <summary>Positions in position order, optionally only valid destinations and matching every filter word.</summary>
        public List<MovePositionOption> PositionsFor(ChassisData chassis, bool availableOnly, string? filter = null)
        {
            if (!_positions.TryGetValue(chassis, out List<MovePositionOption>? positions))
                return new List<MovePositionOption>();
            string[] words = Words(filter);
            return positions.Where(p => (!availableOnly || p.IsAvailable) &&
                words.All(word => Contains(p.Label, word) || Contains(p.Contents, word) ||
                    Contains(p.Side, word) || Contains(p.Status, word) ||
                    p.Position.CircuitNumber.ToString() == word)).ToList();
        }

        private List<MovePositionOption> BuildPositions(BoardData board, ChassisData chassis)
        {
            if (IsElementMove) return BuildFixturePositions(board, chassis);
            if (IsBulkCircuitMove) return BuildBulkPositions(board, chassis);
            List<PoleAssignment> assignments = CircuitLabelCalculator.Calculate(board, chassis);
            string? chassisError = CircuitLayout.GetMoveErrors(Source, Circuit, board, chassis,
                out Dictionary<int, string?> errors);
            var owners = new Dictionary<int, CircuitData>();
            foreach (CircuitData other in chassis.Circuits)
            {
                if (!other.PoleIndex.HasValue) continue;
                for (int i = 0; i < other.Poles; i++)
                    owners[other.PoleIndex.Value + i * 2] = other;
            }

            var options = new List<MovePositionOption>();
            foreach (PoleAssignment position in assignments)
            {
                owners.TryGetValue(position.PoleIndex, out CircuitData? owner);
                bool isCurrent = ReferenceEquals(chassis, Source) && Circuit.PoleIndex == position.PoleIndex;
                string? error = chassisError ?? (errors.TryGetValue(position.PoleIndex, out string? e)
                    ? e : "The destination position is outside the chassis.");
                options.Add(new MovePositionOption
                {
                    Board = board, Chassis = chassis, Position = position,
                    Contents = Describe(owner, position.PoleIndex),
                    Error = error, IsCurrent = isCurrent,
                    ReplacesSpare = error == null && !isCurrent && CircuitLayout
                        .GetSparesReplacedByMove(Circuit, chassis, position.PoleIndex).Any(CircuitLayout.HasDetails)
                });
            }
            return options;
        }

        private List<MovePositionOption> BuildFixturePositions(BoardData board, ChassisData chassis)
        {
            Dictionary<int, FixtureMoveTarget> targets;
            string? chassisError = null;
            try
            {
                targets = FixtureMover.GetTargets(board, chassis, CurrentCircuit);
            }
            catch (System.IO.InvalidDataException ex)
            {
                targets = new Dictionary<int, FixtureMoveTarget>();
                chassisError = ex.Message;
            }
            if (chassisError != null)
                return CircuitLabelCalculator.Calculate(board, chassis).Select(position => new MovePositionOption
                {
                    Board = board, Chassis = chassis, Position = position, Contents = "?", Error = chassisError
                }).ToList();
            return CircuitLabelCalculator.Calculate(board, chassis).Select(position => targets[position.PoleIndex])
                .Select(target => new MovePositionOption
                {
                    Board = board, Chassis = chassis, Position = target.Position,
                    Contents = Describe(target.Circuit, target.Position.PoleIndex),
                    IsCurrent = !target.IsAvailable, FixtureTarget = target, CreatesCircuit = target.CreatesCircuit
                }).ToList();
        }

        private List<MovePositionOption> BuildBulkPositions(BoardData board, ChassisData chassis)
        {
            Dictionary<int, BulkMovePlan> plans = BulkCircuitOperations.PlanMoves(Circuits, board, chassis);
            Dictionary<int, CircuitData> owners = Owners(chassis);
            return CircuitLabelCalculator.Calculate(board, chassis).Select(position =>
            {
                owners.TryGetValue(position.PoleIndex, out CircuitData? owner);
                BulkMovePlan plan = plans[position.PoleIndex];
                return new MovePositionOption
                {
                    Board = board, Chassis = chassis, Position = position,
                    Contents = Describe(owner, position.PoleIndex),
                    Error = plan.Error, IsCurrent = plan.IsValid && plan.IsNoOp, BulkPlan = plan,
                    ReplacesSpare = plan.IsValid && !plan.IsNoOp && plan.ReplacedSpares.Any(CircuitLayout.HasDetails)
                };
            }).ToList();
        }

        private static Dictionary<int, CircuitData> Owners(ChassisData chassis)
        {
            var owners = new Dictionary<int, CircuitData>();
            foreach (CircuitData other in chassis.Circuits)
            {
                if (!other.PoleIndex.HasValue) continue;
                for (int i = 0; i < other.Poles; i++)
                    owners[other.PoleIndex.Value + i * 2] = other;
            }
            return owners;
        }

        private string Describe(CircuitData? owner, int poleIndex)
        {
            if (owner == null) return "Space";
            if (_moving.Contains(owner)) return _moving.Count > 1 ? "(selected circuit)" : "(this circuit)";
            if (owner.PoleIndex != poleIndex) return $"(pole of {owner.DisplayLabel})";
            string poles = owner.Poles > 1 ? $" ({owner.Poles}-pole)" : string.Empty;
            if (CircuitLayout.IsOccupied(owner))
            {
                string description = string.IsNullOrWhiteSpace(owner.Description) ? "(no description)" : owner.Description;
                return $"{description} [{owner.Fixtures.Count}]{poles}";
            }
            if (!CircuitLayout.HasDetails(owner)) return "Space";
            return (string.IsNullOrWhiteSpace(owner.Description) ? "Spare" : $"Spare — {owner.Description}") + poles;
        }

        private static string[] Words(string? filter) =>
            (filter ?? string.Empty).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        private static bool Contains(string? value, string word) =>
            value != null && value.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

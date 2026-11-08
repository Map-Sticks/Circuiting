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
        public string Note => ReplacesSpare ? " — replaces spare"
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
            ChassisData source, CircuitData circuit, CircuitedFixture? fixture = null)
        {
            SourceBoard = sourceBoard;
            Source = source;
            Circuit = circuit;
            Fixture = fixture;
            foreach (BoardData board in boards)
            {
                var chassisOptions = new List<MoveChassisOption>();
                foreach (ChassisData chassis in board.Chassis)
                {
                    List<MovePositionOption> positions = BuildPositions(board, chassis);
                    _positions[chassis] = positions;
                    chassisOptions.Add(new MoveChassisOption
                    {
                        Board = board, Chassis = chassis, IsSource = ReferenceEquals(chassis, source),
                        Available = positions.Count(p => p.IsAvailable)
                    });
                }
                ChassisByBoard[board] = chassisOptions;
                Boards.Add(new MoveBoardOption
                {
                    Board = board, IsSource = ReferenceEquals(board, sourceBoard),
                    Available = chassisOptions.Sum(c => c.Available)
                });
            }
        }

        public BoardData SourceBoard { get; }
        public ChassisData Source { get; }
        public CircuitData Circuit { get; }
        public CircuitedFixture? Fixture { get; }
        public string Title => Fixture != null ? "Move Element to Circuit" : "Move Circuit";
        public List<MoveBoardOption> Boards { get; } = new List<MoveBoardOption>();
        private Dictionary<BoardData, List<MoveChassisOption>> ChassisByBoard { get; } =
            new Dictionary<BoardData, List<MoveChassisOption>>();
        public int TotalAvailable => Boards.Sum(b => b.Available);

        public string CircuitSummary
        {
            get
            {
                if (Fixture != null)
                {
                    string name = string.IsNullOrWhiteSpace(Fixture.FamilyName) ? "element" : Fixture.FamilyName;
                    return $"Move {name} from {Circuit.DisplayLabel} on {SourceBoard.DisplayName} / {Source.Name} " +
                        "to another circuit. Choosing a spare or space creates a new circuit.";
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
            if (Fixture != null) return BuildFixturePositions(board, chassis);
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
                targets = FixtureMover.GetTargets(board, chassis, Circuit);
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

        private string Describe(CircuitData? owner, int poleIndex)
        {
            if (owner == null) return "Space";
            if (ReferenceEquals(owner, Circuit)) return "(this circuit)";
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

using Circuiting.Models;
using Circuiting.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Circuiting.ViewModels
{
    public interface ICircuitTreeActions
    {
        void AddChassis(BoardData board);
        void RenameBoard(BoardData board);
        void DeleteBoard(BoardData board);
        void EditBoard(BoardData board);
        void DuplicateBoard(BoardData board);
        void EditChassis(BoardData board, ChassisData chassis);
        void BulkEditChassis(BoardData board, ChassisData chassis);
        void ClearChassis(ChassisData chassis);
        void DeleteChassis(BoardData board, ChassisData chassis);
        void EditRow(CircuitRowViewModel row);
        void DeleteCircuit(ChassisData chassis, CircuitData circuit);
        void MoveCircuit(BoardData board, ChassisData chassis, CircuitData circuit);
        void ConvertToThreePhase(BoardData board, ChassisData chassis, PoleAssignment pole);
        void AssignFixtures(BoardData board, ChassisData chassis, CircuitData circuit);
        void AssignFixturesToSpare(BoardData board, ChassisData chassis, PoleAssignment pole);
        void SelectFixtures(BoardData board, ChassisData chassis, CircuitData circuit);
        void RemoveFixtures(BoardData board, ChassisData chassis, CircuitData circuit);
        void ShowFixture(BoardData board, ChassisData chassis, CircuitData circuit, CircuitedFixture fixture, bool zoom);
        void MoveFixture(BoardData board, ChassisData chassis, CircuitData circuit, CircuitedFixture fixture);
        FixtureDetails? GetFixtureDetails(CircuitedFixture fixture);
        TreeSelection Selection { get; }
    }

    public abstract class CircuitTreeNode : ObservableObject
    {
        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public bool IsSearchMatch { get; internal set; }
    }

    public sealed class BoardTreeNode : CircuitTreeNode
    {
        internal BoardTreeNode(BoardData board, ICircuitTreeActions actions, ISet<Guid> expanded)
            : this(board, actions, expanded.Contains(board.BoardGuid),
                board.Chassis.Select(c => new ChassisTreeNode(board, c, actions, expanded)).ToList())
        {
        }

        internal BoardTreeNode(BoardData board, ICircuitTreeActions actions, bool isExpanded,
            List<ChassisTreeNode> chassis)
        {
            Board = board;
            IsExpanded = isExpanded;
            Chassis = chassis;
            AddChassisCommand = new RelayCommand(() => actions.AddChassis(board));
            RenameCommand = new RelayCommand(() => actions.RenameBoard(board));
            EditCommand = new RelayCommand(() => actions.EditBoard(board));
            DuplicateCommand = new RelayCommand(() => actions.DuplicateBoard(board));
            DeleteCommand = new RelayCommand(() => actions.DeleteBoard(board));
            Selection = actions.Selection;
        }

        public BoardData Board { get; }
        public TreeSelection Selection { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
        public string Name => Board.DisplayName;
        public string PhaseText => Board.Supply == SupplyType.ThreePhase ? "3 Phase" : "1 Phase";
        public List<ChassisTreeNode> Chassis { get; }
        public IRelayCommand AddChassisCommand { get; }
        public IRelayCommand RenameCommand { get; }
        public IRelayCommand EditCommand { get; }
        public IRelayCommand DuplicateCommand { get; }
        public IRelayCommand DeleteCommand { get; }
    }

    public sealed class ChassisTreeNode : CircuitTreeNode
    {
        internal ChassisTreeNode(BoardData board, ChassisData chassis,
            ICircuitTreeActions actions, ISet<Guid> expanded)
            : this(board, chassis, actions, expanded.Contains(chassis.ChassisGuid),
                CircuitTreeProjection.BuildRows(board, chassis, actions, expanded))
        {
        }

        internal ChassisTreeNode(BoardData board, ChassisData chassis, ICircuitTreeActions actions,
            bool isExpanded, List<CircuitRowViewModel> rows)
        {
            Chassis = chassis;
            IsExpanded = isExpanded;
            Rows = rows;
            EditCommand = new RelayCommand(() => actions.EditChassis(board, chassis));
            BulkEditCommand = new RelayCommand(() => actions.BulkEditChassis(board, chassis));
            ClearCommand = new RelayCommand(() => actions.ClearChassis(chassis));
            DeleteCommand = new RelayCommand(() => actions.DeleteChassis(board, chassis));
        }

        public ChassisData Chassis { get; }
        public string Header
        {
            get
            {
                bool threePhase = Chassis.PhaseLabels.Count >= 3;
                string icon = threePhase ? "⚡" : "💡";
                string phase = threePhase ? "3φ" : "1φ";
                return $"{icon} {Chassis.Name}  ({phase}, Poles: {Chassis.MaxPoles}, Start: {Chassis.StartingCircuit})";
            }
        }
        public List<CircuitRowViewModel> Rows { get; }
        public IRelayCommand EditCommand { get; }
        public IRelayCommand BulkEditCommand { get; }
        public IRelayCommand ClearCommand { get; }
        public IRelayCommand DeleteCommand { get; }
    }

    public sealed class CircuitSideCommands
    {
        internal CircuitSideCommands(BoardData board, ChassisData chassis, PoleAssignment pole,
            CircuitData? start, ConsumedPoleInfo? consumed, ICircuitTreeActions actions)
        {
            CircuitData? circuit = consumed?.Parent ?? start;
            string label = circuit?.DisplayLabel ?? pole.DisplayLabel;
            string side = pole.Side == Side.Left ? "Left" : "Right";
            string suffix = consumed == null ? string.Empty : $" (multi-pole from {label})";
            DeleteLabel = $"🗑️  Delete {side} — {label}{suffix}";
            MoveLabel = $"Move {side} - {label}{suffix}";
            AssignLabel = $"📌  Assign Fixtures — {label}" + (circuit == null ? " (space)"
                : CircuitLayout.IsOccupied(circuit) ? string.Empty : " (spare)");
            ConvertLabel = $"3φ  Convert to Three-Phase Circuit — {pole.DisplayLabel}";
            CanConvertToThreePhase = chassis.PhaseLabels.Count >= CircuitLayout.ThreePhasePoles &&
                pole.PhaseIndex < CircuitLayout.ThreePhasePoles && circuit?.Poles != CircuitLayout.ThreePhasePoles;
            ConvertCommand = new RelayCommand(() => actions.ConvertToThreePhase(board, chassis, pole));
            SelectLabel = $"👁️  Select in Model — {label} ({circuit?.Fixtures.Count ?? 0})";
            RemoveLabel = $"🔗  Pick Fixtures to Remove — {label}";
            HasCircuit = circuit != null;
            HasFixtures = circuit != null && circuit.Fixtures.Count > 0;
            AssignCommand = circuit == null
                ? new RelayCommand(() => actions.AssignFixturesToSpare(board, chassis, pole))
                : new RelayCommand(() => actions.AssignFixtures(board, chassis, circuit));
            if (circuit == null) return;
            DeleteCommand = new RelayCommand(() => actions.DeleteCircuit(chassis, circuit));
            MoveCommand = new RelayCommand(() => actions.MoveCircuit(board, chassis, circuit));
            SelectCommand = new RelayCommand(() => actions.SelectFixtures(board, chassis, circuit));
            RemoveCommand = new RelayCommand(() => actions.RemoveFixtures(board, chassis, circuit));
        }

        public string DeleteLabel { get; }
        public string MoveLabel { get; }
        public string AssignLabel { get; }
        public string SelectLabel { get; }
        public string RemoveLabel { get; }
        public string ConvertLabel { get; }
        public bool CanConvertToThreePhase { get; }
        public IRelayCommand ConvertCommand { get; }
        public bool HasCircuit { get; }
        public bool HasFixtures { get; }
        public IRelayCommand AssignCommand { get; }
        public IRelayCommand? DeleteCommand { get; }
        public IRelayCommand? MoveCommand { get; }
        public IRelayCommand? SelectCommand { get; }
        public IRelayCommand? RemoveCommand { get; }
    }

    public sealed class FixtureTreeNode : CircuitTreeNode
    {
        internal FixtureTreeNode(BoardData board, ChassisData chassis, CircuitData circuit,
            CircuitedFixture fixture, Side side, ICircuitTreeActions? actions)
        {
            Board = board;
            Chassis = chassis;
            Circuit = circuit;
            Fixture = fixture;
            Side = side;
            FixtureDetails? details = actions?.GetFixtureDetails(fixture);
            IsMissing = details != null && !details.Found;
            if (details == null || !details.Found)
            {
                Name = string.IsNullOrWhiteSpace(fixture.FamilyName) ? "(unnamed element)" : fixture.FamilyName;
                if (IsMissing) Name += "  ⚠ not found in model";
                Space = string.IsNullOrWhiteSpace(fixture.RoomName) ? "Space unknown" : $"Room {fixture.RoomName}";
            }
            else
            {
                Name = details.FamilyName.Length == 0 || details.FamilyName == details.TypeName
                    ? details.TypeName : $"{details.FamilyName} : {details.TypeName}";
                if (details.Mark.Length > 0) Name += $"  [{details.Mark}]";
                Space = details.Space.Length > 0 ? details.Space : "No space";
            }

            var tip = new List<string> { $"{circuit.DisplayLabel} — {Name}" };
            if (details?.Found == true)
            {
                if (details.Category.Length > 0) tip.Add($"Category: {details.Category}");
                tip.Add($"MEP space: {(details.Space.Length > 0 ? details.Space : "none")}");
                if (details.RoomName.Length > 0) tip.Add($"Room: {details.RoomName}");
                if (details.LevelName.Length > 0) tip.Add($"Level: {details.LevelName}");
                tip.Add($"Element ID: {details.ElementId}");
            }
            else if (IsMissing)
            {
                tip.Add("The element could not be found in the model. Run Validate Model to repair the assignment.");
            }
            tip.Add("Double-click to zoom to the element. Alt+drag onto a circuit position to move it.");
            ToolTip = string.Join(Environment.NewLine, tip);
            ZoomLabel = $"🔍  Zoom to Element — {Name}";
            SelectLabel = $"☑️  Select Element (edit properties) — {Name}";
            MoveLabel = $"↔️  Move to Circuit... — {Name}";
            if (actions == null) return;
            Selection = actions.Selection;
            MoveCommand = new RelayCommand(() => actions.MoveFixture(board, chassis, circuit, fixture));
            ZoomCommand = new RelayCommand(() => actions.ShowFixture(board, chassis, circuit, fixture, true));
            SelectCommand = new RelayCommand(() => actions.ShowFixture(board, chassis, circuit, fixture, false));
        }

        public BoardData Board { get; }
        public ChassisData Chassis { get; }
        public CircuitData Circuit { get; }
        public CircuitedFixture Fixture { get; }
        public Side Side { get; }
        public bool IsLeft => Side == Side.Left;
        public bool IsRight => Side == Side.Right;
        public string Name { get; }
        public string Space { get; }
        public bool IsMissing { get; }
        public string ToolTip { get; }
        public string ZoomLabel { get; }
        public string SelectLabel { get; }
        public IRelayCommand? ZoomCommand { get; }
        public IRelayCommand? SelectCommand { get; }
        public string MoveLabel { get; }
        public IRelayCommand? MoveCommand { get; }
        public TreeSelection? Selection { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}

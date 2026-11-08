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
        }

        public BoardData Board { get; }
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
                CircuitTreeProjection.BuildRows(board, chassis, actions))
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
}

using System.IO;
using Circuiting.Models;

namespace Circuiting.Services
{
    public sealed class ThreePhaseConversion
    {
        public PoleAssignment Start { get; internal set; } = null!;
        /// <summary>The existing record that becomes the three-phase circuit; null creates a new record.</summary>
        public CircuitData? Kept { get; internal set; }
        /// <summary>Unoccupied records in the group that are deleted by the conversion.</summary>
        public List<CircuitData> Replaced { get; } = new List<CircuitData>();
    }

    public static class CircuitLayout
    {
        public const int ThreePhasePoles = 3;

        /// <summary>A position is occupied only when model elements (fixtures) are assigned to it.</summary>
        public static bool IsOccupied(CircuitData circuit) => circuit.Fixtures.Count > 0;

        /// <summary>True when an unoccupied record holds saved details, making it a spare rather than a space.</summary>
        public static bool HasDetails(CircuitData circuit) =>
            !string.IsNullOrWhiteSpace(circuit.Description) || !string.IsNullOrEmpty(circuit.BreakerSize) ||
            !string.IsNullOrEmpty(circuit.ProtectionType) || !string.IsNullOrEmpty(circuit.EarthLeakage) ||
            circuit.Poles > 1;

        public static string SpareSummary(CircuitData circuit)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(circuit.Description)) parts.Add(circuit.Description.Trim());
            if (circuit.Poles > 1) parts.Add($"{circuit.Poles}-pole");
            string protection = string.IsNullOrEmpty(circuit.ProtectionType) ? string.Empty
                : circuit.ProtectionType == "MCB" || string.IsNullOrEmpty(circuit.EarthLeakage)
                    ? circuit.ProtectionType : $"{circuit.EarthLeakage} {circuit.ProtectionType}";
            string rating = $"{circuit.BreakerSize} {protection}".Trim();
            if (rating.Length > 0) parts.Add(rating);
            return parts.Count == 0 ? circuit.DisplayLabel : $"{circuit.DisplayLabel} ({string.Join(", ", parts)})";
        }

        public static IEnumerable<int> PolesOf(CircuitData circuit)
        {
            if (!circuit.PoleIndex.HasValue)
                throw new InvalidDataException($"Circuit '{circuit.DisplayLabel}' has no pole position.");
            for (int i = 0; i < circuit.Poles; i++)
                yield return checked(circuit.PoleIndex.Value + i * 2);
        }

        /// <summary>
        /// Returns the unoccupied records in <paramref name="circuits"/> that overlap <paramref name="poles"/>,
        /// excluding <paramref name="exclude"/>. These are replaced when another circuit takes their position.
        /// </summary>
        public static List<CircuitData> GetSparesAt(IEnumerable<CircuitData> circuits,
            IEnumerable<int> poles, CircuitData? exclude = null)
        {
            var wanted = new HashSet<int>(poles);
            return circuits.Where(other => !ReferenceEquals(other, exclude) && !IsOccupied(other) &&
                PolesOf(other).Any(wanted.Contains)).ToList();
        }

        /// <summary>Unoccupied records (spares or spaces) that a valid move would replace.</summary>
        public static List<CircuitData> GetSparesReplacedByMove(CircuitData circuit,
            ChassisData destination, int poleIndex)
        {
            IEnumerable<int> poles = Enumerable.Range(0, circuit.Poles).Select(i => checked(poleIndex + i * 2));
            return GetSparesAt(destination.Circuits, poles, circuit);
        }

        public static string? GetMoveError(ChassisData source, CircuitData circuit,
            BoardData destinationBoard, ChassisData destination, int poleIndex)
        {
            string? error = GetMoveErrors(source, circuit, destinationBoard, destination,
                out Dictionary<int, string?> errors);
            if (error != null) return error;
            return errors.TryGetValue(poleIndex, out string? positionError)
                ? positionError : "The destination position is outside the chassis.";
        }

        /// <summary>
        /// Validates moving <paramref name="circuit"/> to every position of <paramref name="destination"/> at once.
        /// Returns an error that applies to the whole chassis, or null with a per-position error (null = valid).
        /// </summary>
        public static string? GetMoveErrors(ChassisData source, CircuitData circuit,
            BoardData destinationBoard, ChassisData destination, out Dictionary<int, string?> errors)
        {
            errors = new Dictionary<int, string?>();
            if (!source.Circuits.Contains(circuit))
                return "The source circuit no longer belongs to its chassis.";
            if (!destinationBoard.Chassis.Contains(destination))
                return "The destination chassis no longer belongs to its board.";
            if (circuit.Poles < 1 || circuit.Poles > 3) return "The circuit has an invalid pole count.";
            List<PoleAssignment> assignments = CircuitLabelCalculator.Calculate(destinationBoard, destination);
            bool sameChassis = ReferenceEquals(source, destination);

            HashSet<int> occupied = new HashSet<int>();
            string? positionlessError = null;
            foreach (CircuitData other in destination.Circuits)
            {
                if (sameChassis && ReferenceEquals(other, circuit)) continue;
                if (!other.PoleIndex.HasValue)
                {
                    positionlessError = "A destination circuit has no pole position.";
                    break;
                }
                if (!IsOccupied(other)) continue;
                for (int i = 0; i < other.Poles; i++)
                    occupied.Add(checked(other.PoleIndex.Value + i * 2));
            }

            var positions = new HashSet<int>(assignments.Select(a => a.PoleIndex));
            foreach (PoleAssignment position in assignments)
                errors[position.PoleIndex] = sameChassis && circuit.PoleIndex == position.PoleIndex ? null
                    : positionlessError ?? PositionMoveError(circuit, destination, position, assignments, positions, occupied);
            return null;
        }

        private static string? PositionMoveError(CircuitData circuit, ChassisData destination, PoleAssignment position,
            List<PoleAssignment> assignments, HashSet<int> positions, HashSet<int> occupied)
        {
            if (circuit.Poles > 1 && (destination.PhaseLabels.Count < 3 ||
                !IsValidMultiPoleStart(position, destination, circuit.Poles, assignments)))
                return "A multi-pole circuit must start on the first phase of a three-phase chassis and fit on the same side.";
            for (int i = 0; i < circuit.Poles; i++)
            {
                int index = checked(position.PoleIndex + i * 2);
                if (!positions.Contains(index))
                    return "The circuit exceeds the destination chassis.";
                if (occupied.Contains(index))
                    return "The destination is occupied by a circuit with fixtures assigned. " +
                        "Choose positions that are spare or space for all circuit poles.";
            }
            return null;
        }

        public static bool Move(ChassisData source, CircuitData circuit,
            BoardData destinationBoard, ChassisData destination, int poleIndex)
        {
            string? error = GetMoveError(source, circuit, destinationBoard, destination, poleIndex);
            if (error != null) throw new InvalidDataException(error);
            if (ReferenceEquals(source, destination) && circuit.PoleIndex == poleIndex) return false;
            PoleAssignment position = CircuitLabelCalculator.Calculate(destinationBoard, destination)
                .Single(a => a.PoleIndex == poleIndex);
            foreach (CircuitData spare in GetSparesReplacedByMove(circuit, destination, poleIndex))
                destination.Circuits.Remove(spare);
            if (!ReferenceEquals(source, destination))
            {
                source.Circuits.Remove(circuit);
                destination.Circuits.Add(circuit);
            }
            circuit.PoleIndex = poleIndex;
            circuit.DisplayLabel = position.DisplayLabel;
            circuit.CircuitNumber = position.CircuitNumber;
            return true;
        }

        /// <summary>
        /// Plans converting the three-phase group containing <paramref name="poleIndex"/> into one
        /// three-pole circuit. Any position of the group (first, second or third phase) may be chosen.
        /// The record with fixtures is kept; otherwise the record at the chosen position is kept.
        /// Other unoccupied records in the group are replaced.
        /// </summary>
        public static string? GetThreePhaseConversion(BoardData board, ChassisData chassis, int poleIndex,
            out ThreePhaseConversion? plan)
        {
            plan = null;
            if (!board.Chassis.Contains(chassis)) return "The chassis no longer belongs to its board.";
            if (chassis.PhaseLabels.Count < ThreePhasePoles)
                return "Three-phase circuits need a three-phase chassis (at least three phase labels).";
            List<PoleAssignment> assignments = CircuitLabelCalculator.Calculate(board, chassis);
            PoleAssignment? clicked = assignments.FirstOrDefault(a => a.PoleIndex == poleIndex);
            if (clicked == null) return "The position is outside the chassis.";
            if (clicked.PhaseIndex >= ThreePhasePoles)
                return $"Position {clicked.DisplayLabel} is not on one of the first three phases.";
            int startRow = clicked.RowIndex - clicked.PhaseIndex;
            PoleAssignment? start = assignments.FirstOrDefault(a => a.Side == clicked.Side && a.RowIndex == startRow);
            if (start == null || !IsValidMultiPoleStart(start, chassis, ThreePhasePoles, assignments))
                return $"Position {clicked.DisplayLabel} does not have a complete set of three phases on this side.";

            var groupPoles = Enumerable.Range(0, ThreePhasePoles).Select(i => checked(start.PoleIndex + i * 2)).ToList();
            List<CircuitData> overlapping = chassis.Circuits
                .Where(circuit => PolesOf(circuit).Any(groupPoles.Contains)).ToList();
            List<CircuitData> occupied = overlapping.Where(IsOccupied).ToList();
            if (occupied.Count > 1)
                return $"Positions {string.Join(" and ", occupied.Select(c => c.DisplayLabel))} all have fixtures " +
                    "assigned. Only one position in a three-phase circuit may have fixtures; move or delete the others first.";
            CircuitData? kept = occupied.Count == 1 ? occupied[0]
                : overlapping.FirstOrDefault(circuit => PolesOf(circuit).Contains(poleIndex));
            if (kept != null && kept.Poles == ThreePhasePoles && kept.PoleIndex == start.PoleIndex)
                return $"{kept.DisplayLabel} is already a three-phase circuit.";

            plan = new ThreePhaseConversion { Start = start, Kept = kept };
            plan.Replaced.AddRange(overlapping.Where(circuit => !ReferenceEquals(circuit, kept)));
            return null;
        }

        public static CircuitData ConvertToThreePhase(BoardData board, ChassisData chassis, int poleIndex)
        {
            string? error = GetThreePhaseConversion(board, chassis, poleIndex, out ThreePhaseConversion? plan);
            if (error != null || plan == null) throw new InvalidDataException(error);
            CircuitData circuit = plan.Kept ?? new CircuitData { CircuitGuid = Guid.NewGuid() };
            List<CircuitData> candidates = chassis.Circuits
                .Where(other => !plan.Replaced.Contains(other) && !ReferenceEquals(other, circuit)).ToList();
            int? oldPoleIndex = circuit.PoleIndex;
            int oldPoles = circuit.Poles;
            circuit.PoleIndex = plan.Start.PoleIndex;
            circuit.Poles = ThreePhasePoles;
            candidates.Add(circuit);
            try
            {
                ValidateAndUpdate(board, chassis, candidates);
            }
            catch
            {
                circuit.PoleIndex = oldPoleIndex;
                circuit.Poles = oldPoles;
                throw;
            }
            chassis.Circuits = candidates;
            return circuit;
        }

        public static void MigrateLegacyPositions(ChassisData chassis)
        {
            List<PoleAssignment> assignments = CircuitLabelCalculator.Calculate(chassis);

            foreach (CircuitData circuit in chassis.Circuits)
            {
                if (circuit.PoleIndex.HasValue) continue;

                List<PoleAssignment> matches = assignments
                    .Where(a => a.DisplayLabel == circuit.DisplayLabel)
                    .ToList();
                if (matches.Count == 0 && circuit.CircuitNumber > 0)
                    matches = assignments.Where(a => a.CircuitNumber == circuit.CircuitNumber).ToList();
                else if (matches.Count > 1 && circuit.CircuitNumber > 0)
                    matches = matches.Where(a => a.CircuitNumber == circuit.CircuitNumber).ToList();

                if (matches.Count != 1)
                    throw new InvalidDataException(
                        $"Cannot determine a unique pole for circuit '{circuit.DisplayLabel}' " +
                        $"in chassis '{chassis.Name}'. The stored data was not changed.");

                circuit.PoleIndex = matches[0].PoleIndex;
            }
        }

        public static void ValidateAndUpdate(BoardData board, ChassisData chassis)
        {
            ValidateAndUpdate(board, chassis, chassis.Circuits);
        }

        public static void ValidateAndUpdate(
            BoardData board, ChassisData chassis, IEnumerable<CircuitData> circuits)
        {
            List<PoleAssignment> assignments = CircuitLabelCalculator.Calculate(board, chassis);
            Dictionary<int, PoleAssignment> positions = assignments.ToDictionary(a => a.PoleIndex);
            HashSet<int> occupied = new HashSet<int>();

            List<CircuitData> circuitList = circuits.ToList();
            foreach (CircuitData circuit in circuitList)
            {
                if (!circuit.PoleIndex.HasValue ||
                    !positions.TryGetValue(circuit.PoleIndex.Value, out PoleAssignment? start))
                    throw new InvalidDataException(
                        $"Circuit '{circuit.DisplayLabel}' is outside chassis '{chassis.Name}'.");
                if (circuit.Poles < 1 || circuit.Poles > 3)
                    throw new InvalidDataException($"Circuit '{circuit.DisplayLabel}' has an invalid pole count.");

                for (int i = 0; i < circuit.Poles; i++)
                {
                    int poleIndex = checked(start.PoleIndex + i * 2);
                    if (!positions.ContainsKey(poleIndex) || !occupied.Add(poleIndex))
                        throw new InvalidDataException(
                            $"Circuit '{circuit.DisplayLabel}' overlaps another circuit or exceeds chassis '{chassis.Name}'.");
                }

            }

            foreach (CircuitData circuit in circuitList)
            {
                PoleAssignment start = positions[circuit.PoleIndex!.Value];
                circuit.DisplayLabel = start.DisplayLabel;
                circuit.CircuitNumber = start.CircuitNumber;
            }
        }

        public static Dictionary<int, CircuitData> GetConsumedPoles(ChassisData chassis)
        {
            var consumed = new Dictionary<int, CircuitData>();
            foreach (CircuitData circuit in chassis.Circuits)
            {
                if (!circuit.PoleIndex.HasValue)
                    throw new InvalidDataException($"Circuit '{circuit.DisplayLabel}' has no pole position.");
                for (int i = 1; i < circuit.Poles; i++)
                    consumed.Add(checked(circuit.PoleIndex.Value + i * 2), circuit);
            }

            return consumed;
        }

        public static bool IsValidMultiPoleStart(PoleAssignment assignment, ChassisData chassis,
            int poles, IEnumerable<PoleAssignment> assignments)
        {
            if (poles < 1 || poles > 3) return false;
            int phaseCount = chassis.PhaseLabels?.Count ?? 0;
            if (phaseCount > 1 && assignment.PhaseIndex != 0) return false;
            return assignments.Count(a => a.Side == assignment.Side &&
                a.RowIndex >= assignment.RowIndex) >= poles;
        }
    }
}

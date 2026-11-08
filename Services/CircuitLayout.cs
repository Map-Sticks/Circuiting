using System.IO;
using Circuiting.Models;

namespace Circuiting.Services
{
    public static class CircuitLayout
    {
        public static string? GetMoveError(ChassisData source, CircuitData circuit,
            BoardData destinationBoard, ChassisData destination, int poleIndex)
        {
            if (!source.Circuits.Contains(circuit))
                return "The source circuit no longer belongs to its chassis.";
            if (!destinationBoard.Chassis.Contains(destination))
                return "The destination chassis no longer belongs to its board.";
            List<PoleAssignment> assignments = CircuitLabelCalculator.Calculate(destinationBoard, destination);
            PoleAssignment? position = assignments.FirstOrDefault(a => a.PoleIndex == poleIndex);
            if (position == null) return "The destination position is outside the chassis.";
            if (circuit.Poles < 1 || circuit.Poles > 3) return "The circuit has an invalid pole count.";
            if (ReferenceEquals(source, destination) && circuit.PoleIndex == poleIndex) return null;
            if (circuit.Poles > 1 && (destination.PhaseLabels.Count < 3 ||
                !IsValidMultiPoleStart(position, destination, circuit.Poles, assignments)))
                return "A multi-pole circuit must start on the first phase of a three-phase chassis and fit on the same side.";

            HashSet<int> occupied = new HashSet<int>();
            foreach (CircuitData other in destination.Circuits)
            {
                if (ReferenceEquals(source, destination) && ReferenceEquals(other, circuit)) continue;
                if (!other.PoleIndex.HasValue) return "A destination circuit has no pole position.";
                for (int i = 0; i < other.Poles; i++)
                    occupied.Add(checked(other.PoleIndex.Value + i * 2));
            }
            for (int i = 0; i < circuit.Poles; i++)
            {
                int index = checked(poleIndex + i * 2);
                if (!assignments.Any(a => a.PoleIndex == index))
                    return "The circuit exceeds the destination chassis.";
                if (occupied.Contains(index))
                    return "The destination is occupied. Choose free positions for all circuit poles.";
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

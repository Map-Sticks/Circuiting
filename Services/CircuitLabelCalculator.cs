using Circuiting.Models;

namespace Circuiting.Services
{
    public class PoleAssignment
    {
        public int RowIndex { get; set; }
        public Side Side { get; set; }
        public int CircuitNumber { get; set; }
        public int PhaseIndex { get; set; }
        public string PhaseLabel { get; set; } = string.Empty;
        public string DisplayLabel { get; set; } = string.Empty;
        public int PoleIndex => checked(RowIndex * 2 + (Side == Side.Right ? 1 : 0));
    }

    public enum Side
    {
        Left,
        Right
    }

    public static class CircuitLabelCalculator
    {
        public static List<PoleAssignment> Calculate(ChassisData chassis)
        {
            return Calculate(chassis, 0);
        }

        public static List<PoleAssignment> Calculate(BoardData board, ChassisData chassis)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (chassis == null) throw new ArgumentNullException(nameof(chassis));

            int previousOffset = 0;
            ChassisData? previous = null;
            foreach (ChassisData current in board.Chassis)
            {
                int phaseCount = current.PhaseLabels?.Count ?? 0;
                int offset = current.ContinuePhaseFromPrevious && previous != null &&
                    (previous.PhaseLabels?.Count ?? 0) > 0 && phaseCount > 0
                    ? (previousOffset + previous.MaxPoles / 2) % phaseCount
                    : 0;

                if (ReferenceEquals(current, chassis))
                    return Calculate(current, offset);

                previous = current;
                previousOffset = offset;
            }

            throw new ArgumentException("The chassis does not belong to the board.", nameof(chassis));
        }

        private static List<PoleAssignment> Calculate(ChassisData chassis, int phaseOffset)
        {
            if (chassis == null) throw new ArgumentNullException(nameof(chassis));
            if (chassis.MaxPoles <= 0 || chassis.MaxPoles % 2 != 0)
                throw new ArgumentOutOfRangeException(nameof(chassis), "The pole count must be a positive even number.");
            if (chassis.StartingCircuit < 1)
                throw new ArgumentOutOfRangeException(nameof(chassis), "The starting circuit must be positive.");

            var assignments = new List<PoleAssignment>();

            int patternLength = chassis.PhaseLabels?.Count ?? 0;
            int totalRows = chassis.MaxPoles / 2;

            // Per phase counters — left side odd, right side even
            // Starting from the chassis starting circuit number
            int startLeft = chassis.StartingCircuit;
            int startRight = checked(chassis.StartingCircuit + 1);

            // Each phase tracks its own counter independently
            int[] leftCounters = new int[patternLength == 0 ? 1 : patternLength];
            int[] rightCounters = new int[patternLength == 0 ? 1 : patternLength];

            for (int i = 0; i < leftCounters.Length; i++)
            {
                leftCounters[i] = startLeft;
                rightCounters[i] = startRight;
            }

            for (int row = 0; row < totalRows; row++)
            {
                int phaseIndex = patternLength > 0 ? (row + phaseOffset) % patternLength : 0;

                string phaseLabel = (chassis.ShowPhaseLabel && patternLength > 0 && chassis.PhaseLabels != null)
                    ? chassis.PhaseLabels[phaseIndex]
                    : string.Empty;

                int leftNum = leftCounters[phaseIndex];
                int rightNum = rightCounters[phaseIndex];

                assignments.Add(new PoleAssignment
                {
                    RowIndex = row,
                    Side = Side.Left,
                    CircuitNumber = leftNum,
                    PhaseIndex = phaseIndex,
                    PhaseLabel = phaseLabel,
                    DisplayLabel = FormatLabel(phaseLabel, leftNum, chassis)
                });

                assignments.Add(new PoleAssignment
                {
                    RowIndex = row,
                    Side = Side.Right,
                    CircuitNumber = rightNum,
                    PhaseIndex = phaseIndex,
                    PhaseLabel = phaseLabel,
                    DisplayLabel = FormatLabel(phaseLabel, rightNum, chassis)
                });

                // Advance this phase's counters by 2
                if (row + Math.Max(patternLength, 1) < totalRows)
                {
                    leftCounters[phaseIndex] = checked(leftCounters[phaseIndex] + 2);
                    rightCounters[phaseIndex] = checked(rightCounters[phaseIndex] + 2);
                }
            }

            return assignments;
        }

        private static string FormatLabel(
            string phaseLabel,
            int number,
            ChassisData chassis)
        {
            // Build chassis prefix
            string prefix = string.IsNullOrEmpty(chassis.Prefix)
                ? string.Empty
                : $"{chassis.Prefix}{chassis.Separator}";

            // No phase label — just prefix + number
            if (!chassis.ShowPhaseLabel || string.IsNullOrEmpty(phaseLabel))
                return $"{prefix}{number}";

            // Phase label before number eg. R1, L-R1
            return $"{prefix}{phaseLabel}{chassis.Separator}{number}";
        }

        // Returns just the display label for a specific circuit number
        public static string GetLabelForCircuit(
            int circuitNumber,
            ChassisData chassis)
        {
            var assignments = Calculate(chassis);

            var match = assignments
                .FirstOrDefault(a => a.CircuitNumber == circuitNumber);

            if (match == null) return circuitNumber.ToString();
            if (string.IsNullOrEmpty(match.DisplayLabel)) return circuitNumber.ToString();

            return match.DisplayLabel;
        }
    }
}
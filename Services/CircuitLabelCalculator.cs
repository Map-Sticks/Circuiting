using Circuiting.Models;

namespace Circuiting.Services
{
    public class PoleAssignment
    {
        public int RowIndex { get; set; }
        public Side Side { get; set; }
        public int CircuitNumber { get; set; }
        public string PhaseLabel { get; set; } = string.Empty;
        public string DisplayLabel { get; set; } = string.Empty;
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
            var assignments = new List<PoleAssignment>();

            int patternLength = chassis.PhaseLabels?.Count ?? 0;
            int totalRows = chassis.MaxPoles / 2;

            // Per phase counters — left side odd, right side even
            // Starting from the chassis starting circuit number
            int startLeft = chassis.StartingCircuit;
            int startRight = chassis.StartingCircuit + 1;

            // Each phase tracks its own counter independently
            int[] leftCounters = new int[patternLength == 0 ? 1 : patternLength];
            int[] rightCounters = new int[patternLength == 0 ? 1 : patternLength];

            for (int i = 0; i < leftCounters.Length; i++)
            {
                leftCounters[i] = startLeft + (i * 0);
                rightCounters[i] = startRight + (i * 0);
            }

            // Initialise counters properly
            // Left starts at startLeft, increments by 2 per phase occurrence
            // Right starts at startRight, increments by 2 per phase occurrence
            for (int i = 0; i < leftCounters.Length; i++)
            {
                leftCounters[i] = startLeft;
                rightCounters[i] = startRight;
            }

            for (int row = 0; row < totalRows; row++)
            {
                int phaseIndex = patternLength > 0 ? row % patternLength : 0;

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
                    PhaseLabel = phaseLabel,
                    DisplayLabel = FormatLabel(phaseLabel, leftNum, chassis)
                });

                assignments.Add(new PoleAssignment
                {
                    RowIndex = row,
                    Side = Side.Right,
                    CircuitNumber = rightNum,
                    PhaseLabel = phaseLabel,
                    DisplayLabel = FormatLabel(phaseLabel, rightNum, chassis)
                });

                // Advance this phase's counters by 2
                leftCounters[phaseIndex] += 2;
                rightCounters[phaseIndex] += 2;
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
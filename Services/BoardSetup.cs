using System.Globalization;
using Circuiting.Models;

namespace Circuiting.Services
{
    public static class BoardSetup
    {
        public const int MaxChassis = 20;
        public const int MaxCopies = 50;
        public static readonly IReadOnlyList<string> DefaultPhaseLabels = new[] { "R", "W", "B" };

        public static ChassisData DefaultChassis(bool threePhase, string name = "Main", int startingCircuit = 1) =>
            new ChassisData
            {
                ChassisGuid = Guid.NewGuid(),
                Name = name,
                MaxPoles = 24,
                StartingCircuit = startingCircuit,
                ShowPhaseLabel = threePhase,
                PhaseLabels = threePhase ? DefaultPhaseLabels.ToList() : new List<string>()
            };

        /// <summary>Copies a chassis' configuration with a new identity and no circuits.</summary>
        public static ChassisData CopyChassisSetup(ChassisData source) =>
            new ChassisData
            {
                ChassisGuid = Guid.NewGuid(),
                Name = source.Name,
                MaxPoles = source.MaxPoles,
                StartingCircuit = source.StartingCircuit,
                Prefix = source.Prefix,
                Separator = source.Separator,
                ShowPhaseLabel = source.ShowPhaseLabel,
                ContinuePhaseFromPrevious = source.ContinuePhaseFromPrevious,
                PhaseLabels = source.PhaseLabels.ToList()
            };

        /// <summary>Copies a board's supply and chassis configuration with new identities and no circuits.</summary>
        public static BoardData CopySetup(BoardData source, string name) =>
            new BoardData
            {
                BoardGuid = Guid.NewGuid(),
                DisplayName = name,
                Supply = source.Supply,
                Chassis = source.Chassis.Select(CopyChassisSetup).ToList()
            };

        public static int NextStartingCircuit(IReadOnlyList<ChassisData> chassis, int count)
        {
            if (count <= 0) return 1;
            ChassisData last = chassis[count - 1];
            var board = new BoardData { Chassis = chassis.Take(count).ToList() };
            List<PoleAssignment> assignments = CircuitLabelCalculator.Calculate(board, last);
            return assignments.Count == 0 ? last.StartingCircuit : checked(assignments.Max(a => a.CircuitNumber) + 1);
        }

        /// <summary>
        /// Grows or shrinks <paramref name="chassis"/> to <paramref name="count"/>. New chassis copy the
        /// last chassis' settings (or the defaults) and continue its circuit numbering.
        /// </summary>
        public static void Resize(List<ChassisData> chassis, int count, bool threePhase)
        {
            if (count < 1 || count > MaxChassis)
                throw new ArgumentOutOfRangeException(nameof(count), $"Choose between 1 and {MaxChassis} chassis.");
            if (chassis.Count > count) chassis.RemoveRange(count, chassis.Count - count);
            while (chassis.Count < count)
            {
                ChassisData next = chassis.Count == 0
                    ? DefaultChassis(threePhase)
                    : CopyChassisSetup(chassis[chassis.Count - 1]);
                if (chassis.Count > 0)
                {
                    next.Name = $"Chassis {chassis.Count + 1}";
                    next.StartingCircuit = NextStartingCircuit(chassis, chassis.Count);
                }
                chassis.Add(next);
            }
        }

        /// <summary>Applies shared pole and naming settings to every chassis and renumbers them consecutively.</summary>
        public static void ApplyToAll(List<ChassisData> chassis, int maxPoles, string prefix, string separator)
        {
            for (int i = 0; i < chassis.Count; i++)
            {
                chassis[i].MaxPoles = maxPoles;
                chassis[i].Prefix = prefix;
                chassis[i].Separator = separator;
                if (i > 0) chassis[i].StartingCircuit = NextStartingCircuit(chassis, i);
            }
        }

        /// <summary>Makes chassis phase settings consistent with the board supply.</summary>
        public static void ApplySupply(IEnumerable<ChassisData> chassis, bool threePhase)
        {
            foreach (ChassisData item in chassis)
            {
                if (!threePhase && item.PhaseLabels.Count >= 3)
                {
                    item.PhaseLabels = new List<string>();
                    item.ShowPhaseLabel = false;
                    item.ContinuePhaseFromPrevious = false;
                }
                else if (threePhase && item.PhaseLabels.Count == 0)
                {
                    item.PhaseLabels = DefaultPhaseLabels.ToList();
                    item.ShowPhaseLabel = true;
                }
            }
        }

        public static bool NameExists(IEnumerable<string> names, string name) =>
            names.Any(existing => string.Equals(existing.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>Suggests a numbered name pattern, e.g. "DB-L1" gives "DB-L" starting after the highest used number.</summary>
        public static (string Prefix, string StartText) SuggestNumbering(string sourceName, IEnumerable<string> existing)
        {
            string name = sourceName.Trim();
            int digits = 0;
            while (digits < name.Length && char.IsDigit(name[name.Length - 1 - digits])) digits++;
            string prefix = digits > 0 ? name.Substring(0, name.Length - digits)
                : name.Length == 0 ? "DB-" : name + "-";
            int width = Math.Max(digits, 1);
            int highest = 0;
            foreach (string other in existing.Append(name))
            {
                string candidate = other.Trim();
                if (candidate.Length <= prefix.Length ||
                    !candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                string suffix = candidate.Substring(prefix.Length);
                if (suffix.All(char.IsDigit) && int.TryParse(suffix, NumberStyles.None,
                        CultureInfo.InvariantCulture, out int number))
                    highest = Math.Max(highest, number);
            }
            return (prefix, checked(highest + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0'));
        }

        /// <summary>
        /// Generates <paramref name="count"/> names "prefix + number", counting up from
        /// <paramref name="startText"/> (whose length sets zero padding) and skipping names already in use.
        /// </summary>
        public static List<string> UniqueNames(string prefix, string startText, int count, IEnumerable<string> existing)
        {
            if (count < 1 || count > MaxCopies)
                throw new ArgumentOutOfRangeException(nameof(count), $"Choose between 1 and {MaxCopies} copies.");
            startText = startText.Trim();
            if (startText.Length == 0 || !startText.All(char.IsDigit) ||
                !int.TryParse(startText, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                throw new ArgumentException("The start number must be a whole number.", nameof(startText));
            var used = new HashSet<string>(existing.Select(name => name.Trim()), StringComparer.OrdinalIgnoreCase);
            var names = new List<string>();
            while (names.Count < count)
            {
                string name = prefix + number.ToString(CultureInfo.InvariantCulture).PadLeft(startText.Length, '0');
                if (used.Add(name)) names.Add(name);
                number = checked(number + 1);
            }
            return names;
        }
    }
}

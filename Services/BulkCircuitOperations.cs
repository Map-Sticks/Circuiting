using System.IO;
using Circuiting.Models;

namespace Circuiting.Services
{
    public sealed class CircuitRef
    {
        public BoardData Board { get; set; } = null!;
        public ChassisData Chassis { get; set; } = null!;
        public CircuitData Circuit { get; set; } = null!;
    }

    public sealed class FixtureRef
    {
        public BoardData Board { get; set; } = null!;
        public ChassisData Chassis { get; set; } = null!;
        public CircuitData Circuit { get; set; } = null!;
        public CircuitedFixture Fixture { get; set; } = null!;
    }

    public sealed class BulkMovePlan
    {
        public string? Error { get; internal set; }
        public List<(CircuitRef Source, PoleAssignment Position)> Placements { get; } =
            new List<(CircuitRef Source, PoleAssignment Position)>();
        /// <summary>Unoccupied records at the destination that the moved circuits replace.</summary>
        public List<CircuitData> ReplacedSpares { get; } = new List<CircuitData>();
        public bool IsValid => Error == null;
        /// <summary>True when every circuit would stay where it is.</summary>
        public bool IsNoOp { get; internal set; }
    }

    /// <summary>Bulk move and delete operations for circuits and circuited elements selected in the tree.</summary>
    public static class BulkCircuitOperations
    {
        /// <summary>Circuits with the given GUIDs, in board, chassis and position order.</summary>
        public static List<CircuitRef> ResolveCircuits(IEnumerable<BoardData> boards, ICollection<Guid> circuitGuids)
        {
            return (from board in boards
                    from chassis in board.Chassis
                    from circuit in chassis.Circuits.OrderBy(c => c.PoleIndex ?? int.MaxValue)
                    where circuitGuids.Contains(circuit.CircuitGuid)
                    select new CircuitRef { Board = board, Chassis = chassis, Circuit = circuit }).ToList();
        }

        /// <summary>Elements with the given fixture GUIDs, in board, chassis and circuit position order.</summary>
        public static List<FixtureRef> ResolveFixtures(IEnumerable<BoardData> boards, ICollection<Guid> fixtureGuids)
        {
            return (from board in boards
                    from chassis in board.Chassis
                    from circuit in chassis.Circuits.OrderBy(c => c.PoleIndex ?? int.MaxValue)
                    from fixture in circuit.Fixtures
                    where fixtureGuids.Contains(fixture.FixtureGuid)
                    select new FixtureRef { Board = board, Chassis = chassis, Circuit = circuit, Fixture = fixture })
                .ToList();
        }

        public static BulkMovePlan PlanMove(IReadOnlyList<CircuitRef> circuits, BoardData board, ChassisData chassis,
            int startPole)
        {
            return PlanMoves(circuits, board, chassis).TryGetValue(startPole, out BulkMovePlan? plan)
                ? plan : new BulkMovePlan { Error = "The destination position is outside the chassis." };
        }

        /// <summary>
        /// Plans moving <paramref name="circuits"/> to every start position of <paramref name="chassis"/>.
        /// The first circuit lands on the start position; each following circuit takes the next free
        /// position (in position order) where it fits. Positions with assigned elements are never used;
        /// unoccupied records (spares or spaces) are replaced.
        /// </summary>
        public static Dictionary<int, BulkMovePlan> PlanMoves(IReadOnlyList<CircuitRef> circuits, BoardData board,
            ChassisData chassis)
        {
            var plans = new Dictionary<int, BulkMovePlan>();
            List<PoleAssignment> assignments = CircuitLabelCalculator.Calculate(board, chassis)
                .OrderBy(a => a.PoleIndex).ToList();
            string? error = CommonError(circuits, board, chassis);
            var moving = new HashSet<CircuitData>(circuits.Select(c => c.Circuit));
            var positions = new HashSet<int>(assignments.Select(a => a.PoleIndex));
            var blocked = new HashSet<int>();
            if (error == null)
            {
                foreach (CircuitData other in chassis.Circuits)
                {
                    if (!other.PoleIndex.HasValue)
                    {
                        error = "A destination circuit has no pole position.";
                        break;
                    }
                    if (moving.Contains(other) || !CircuitLayout.IsOccupied(other)) continue;
                    foreach (int pole in CircuitLayout.PolesOf(other)) blocked.Add(pole);
                }
            }

            for (int start = 0; start < assignments.Count; start++)
            {
                var plan = new BulkMovePlan();
                plans[assignments[start].PoleIndex] = plan;
                if (error != null)
                {
                    plan.Error = error;
                    continue;
                }
                var taken = new HashSet<int>();
                int cursor = start;
                for (int k = 0; k < circuits.Count && plan.Error == null; k++)
                {
                    CircuitData circuit = circuits[k].Circuit;
                    int found = -1;
                    for (int i = cursor; i < assignments.Count && (k > 0 || i == start); i++)
                    {
                        if (!Fits(circuit, assignments[i], chassis, assignments, positions, blocked, taken)) continue;
                        found = i;
                        break;
                    }
                    if (found < 0)
                    {
                        plan.Error = k == 0
                            ? "The first selected circuit does not fit here (position in use, or a multi-pole " +
                              "circuit must start on the first phase of a three-phase chassis)."
                            : $"Only {k} of {circuits.Count} selected circuits fit from this position.";
                        break;
                    }
                    PoleAssignment position = assignments[found];
                    for (int i = 0; i < circuit.Poles; i++) taken.Add(position.PoleIndex + i * 2);
                    plan.Placements.Add((circuits[k], position));
                    cursor = found + 1;
                }
                if (plan.Error != null)
                {
                    plan.Placements.Clear();
                    continue;
                }
                plan.ReplacedSpares.AddRange(chassis.Circuits.Where(other => !moving.Contains(other) &&
                    !CircuitLayout.IsOccupied(other) && CircuitLayout.PolesOf(other).Any(taken.Contains)));
                plan.IsNoOp = plan.Placements.All(p => ReferenceEquals(p.Source.Chassis, chassis) &&
                    p.Source.Circuit.PoleIndex == p.Position.PoleIndex);
            }
            return plans;
        }

        private static string? CommonError(IReadOnlyList<CircuitRef> circuits, BoardData board, ChassisData chassis)
        {
            if (circuits.Count == 0) return "No circuits are selected.";
            if (!board.Chassis.Contains(chassis)) return "The destination chassis no longer belongs to its board.";
            foreach (CircuitRef source in circuits)
            {
                if (!source.Chassis.Circuits.Contains(source.Circuit))
                    return $"Circuit {source.Circuit.DisplayLabel} no longer belongs to its chassis.";
                if (source.Circuit.Poles < 1 || source.Circuit.Poles > 3)
                    return $"Circuit {source.Circuit.DisplayLabel} has an invalid pole count.";
            }
            return null;
        }

        private static bool Fits(CircuitData circuit, PoleAssignment position, ChassisData chassis,
            List<PoleAssignment> assignments, HashSet<int> positions, HashSet<int> blocked, HashSet<int> taken)
        {
            if (circuit.Poles > 1 && (chassis.PhaseLabels.Count < CircuitLayout.ThreePhasePoles ||
                !CircuitLayout.IsValidMultiPoleStart(position, chassis, circuit.Poles, assignments)))
                return false;
            for (int i = 0; i < circuit.Poles; i++)
            {
                int pole = position.PoleIndex + i * 2;
                if (!positions.Contains(pole) || blocked.Contains(pole) || taken.Contains(pole)) return false;
            }
            return true;
        }

        public static void ApplyMove(BulkMovePlan plan, BoardData board, ChassisData chassis)
        {
            if (!plan.IsValid) throw new InvalidDataException(plan.Error);
            foreach (CircuitData spare in plan.ReplacedSpares) chassis.Circuits.Remove(spare);
            foreach (var (source, position) in plan.Placements)
            {
                if (!ReferenceEquals(source.Chassis, chassis))
                {
                    source.Chassis.Circuits.Remove(source.Circuit);
                    chassis.Circuits.Add(source.Circuit);
                }
                source.Circuit.PoleIndex = position.PoleIndex;
            }
            CircuitLayout.ValidateAndUpdate(board, chassis);
        }

        /// <summary>Deletes the circuits, leaving their positions as spaces. Returns the number of unassigned elements.</summary>
        public static int DeleteCircuits(IEnumerable<CircuitRef> circuits)
        {
            int unassigned = 0;
            foreach (CircuitRef circuit in circuits)
            {
                if (!circuit.Chassis.Circuits.Remove(circuit.Circuit)) continue;
                unassigned += circuit.Circuit.Fixtures.Count;
            }
            return unassigned;
        }

        /// <summary>
        /// Unassigns the elements from their circuits. A circuit left without elements stays as a spare when it
        /// has saved details, otherwise it reverts to a space. Returns the number of unassigned elements.
        /// </summary>
        public static int UnassignFixtures(IEnumerable<FixtureRef> fixtures)
        {
            int removed = 0;
            var emptied = new List<FixtureRef>();
            foreach (FixtureRef fixture in fixtures)
            {
                if (!fixture.Circuit.Fixtures.Remove(fixture.Fixture)) continue;
                removed++;
                emptied.Add(fixture);
            }
            foreach (FixtureRef source in emptied)
                if (!CircuitLayout.IsOccupied(source.Circuit) && !CircuitLayout.HasDetails(source.Circuit))
                    source.Chassis.Circuits.Remove(source.Circuit);
            return removed;
        }
    }
}

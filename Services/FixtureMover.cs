using System.IO;
using Circuiting.Models;

namespace Circuiting.Services
{
    public enum FixtureMoveKind
    {
        /// <summary>The position belongs to the circuit the element is already on.</summary>
        Current,
        /// <summary>The element joins a circuit that already has elements assigned.</summary>
        AddToCircuit,
        /// <summary>The position is a spare (saved details, no elements); it becomes a circuit keeping its details.</summary>
        FillSpare,
        /// <summary>The position is a space; a new circuit is created for the element.</summary>
        NewCircuit
    }

    public sealed class FixtureMoveTarget
    {
        public PoleAssignment Position { get; internal set; } = null!;
        /// <summary>The existing circuit record that receives the element; null when a new circuit is created.</summary>
        public CircuitData? Circuit { get; internal set; }
        public FixtureMoveKind Kind { get; internal set; }
        public bool IsAvailable => Kind != FixtureMoveKind.Current;
        /// <summary>True when the destination is unoccupied, so the move makes a new circuit (from a spare or space).</summary>
        public bool CreatesCircuit => Kind == FixtureMoveKind.FillSpare || Kind == FixtureMoveKind.NewCircuit;
    }

    /// <summary>Moves a single circuited element (fixture) to another circuit position.</summary>
    public static class FixtureMover
    {
        /// <summary>
        /// Classifies every position of <paramref name="chassis"/> as a destination for an element currently on
        /// <paramref name="source"/>. Poles consumed by a multi-pole circuit resolve to that circuit.
        /// </summary>
        public static Dictionary<int, FixtureMoveTarget> GetTargets(BoardData board, ChassisData chassis,
            CircuitData? source)
        {
            if (!board.Chassis.Contains(chassis))
                throw new InvalidDataException("The destination chassis no longer belongs to its board.");
            var owners = new Dictionary<int, CircuitData>();
            foreach (CircuitData circuit in chassis.Circuits)
            foreach (int pole in CircuitLayout.PolesOf(circuit))
                owners[pole] = circuit;

            var targets = new Dictionary<int, FixtureMoveTarget>();
            foreach (PoleAssignment position in CircuitLabelCalculator.Calculate(board, chassis))
            {
                owners.TryGetValue(position.PoleIndex, out CircuitData? owner);
                FixtureMoveKind kind = owner == null ? FixtureMoveKind.NewCircuit
                    : ReferenceEquals(owner, source) ? FixtureMoveKind.Current
                    : CircuitLayout.IsOccupied(owner) ? FixtureMoveKind.AddToCircuit
                    : CircuitLayout.HasDetails(owner) ? FixtureMoveKind.FillSpare
                    : FixtureMoveKind.NewCircuit;
                targets[position.PoleIndex] = new FixtureMoveTarget { Position = position, Circuit = owner, Kind = kind };
            }
            return targets;
        }

        public static FixtureMoveTarget GetTarget(BoardData board, ChassisData chassis, CircuitData? source, int poleIndex)
        {
            return GetTargets(board, chassis, source).TryGetValue(poleIndex, out FixtureMoveTarget? target)
                ? target : throw new InvalidDataException("The destination position is outside the chassis.");
        }

        /// <summary>
        /// Moves <paramref name="fixture"/> from <paramref name="source"/> to the circuit at <paramref name="poleIndex"/>.
        /// When the destination is a spare or space, the destination becomes a circuit using
        /// <paramref name="details"/> (description and protection) if supplied; its pole count is unchanged.
        /// A source left without elements or saved details reverts to a space. Returns the destination circuit.
        /// </summary>
        public static CircuitData Move(ChassisData sourceChassis, CircuitData source, CircuitedFixture fixture,
            BoardData destinationBoard, ChassisData destination, int poleIndex, CircuitData? details = null)
        {
            if (!sourceChassis.Circuits.Contains(source))
                throw new InvalidDataException("The source circuit no longer belongs to its chassis.");
            if (!source.Fixtures.Contains(fixture))
                throw new InvalidDataException("The element is no longer assigned to the source circuit.");
            FixtureMoveTarget target = GetTarget(destinationBoard, destination, source, poleIndex);
            if (!target.IsAvailable)
                throw new InvalidDataException("The element is already on this circuit.");

            CircuitData circuit = target.Circuit ?? new CircuitData
            {
                CircuitGuid = Guid.NewGuid(),
                PoleIndex = target.Position.PoleIndex,
                DisplayLabel = target.Position.DisplayLabel,
                CircuitNumber = target.Position.CircuitNumber,
                Poles = 1
            };
            if (target.CreatesCircuit && details != null)
            {
                circuit.Description = details.Description;
                circuit.BreakerSize = details.BreakerSize;
                circuit.ProtectionType = details.ProtectionType;
                circuit.EarthLeakage = details.EarthLeakage;
            }
            if (target.Circuit == null) destination.Circuits.Add(circuit);

            source.Fixtures.Remove(fixture);
            circuit.Fixtures.Add(fixture);
            if (!CircuitLayout.IsOccupied(source) && !CircuitLayout.HasDetails(source))
                sourceChassis.Circuits.Remove(source);
            return circuit;
        }

        /// <summary>
        /// Moves several elements, possibly from different circuits, to the circuit at <paramref name="poleIndex"/>.
        /// Elements already on that circuit are left in place. Behaves like <see cref="Move"/> otherwise.
        /// </summary>
        public static CircuitData MoveMany(IReadOnlyList<FixtureRef> fixtures, BoardData destinationBoard,
            ChassisData destination, int poleIndex, CircuitData? details = null)
        {
            foreach (FixtureRef fixture in fixtures)
            {
                if (!fixture.Chassis.Circuits.Contains(fixture.Circuit))
                    throw new InvalidDataException($"Circuit {fixture.Circuit.DisplayLabel} no longer belongs to its chassis.");
                if (!fixture.Circuit.Fixtures.Contains(fixture.Fixture))
                    throw new InvalidDataException("An element is no longer assigned to its circuit.");
            }
            FixtureMoveTarget target = GetTarget(destinationBoard, destination, null, poleIndex);
            List<FixtureRef> moving = fixtures.Where(f => !ReferenceEquals(f.Circuit, target.Circuit)).ToList();
            if (moving.Count == 0)
                throw new InvalidDataException("The selected elements are already on this circuit.");

            CircuitData circuit = target.Circuit ?? new CircuitData
            {
                CircuitGuid = Guid.NewGuid(),
                PoleIndex = target.Position.PoleIndex,
                DisplayLabel = target.Position.DisplayLabel,
                CircuitNumber = target.Position.CircuitNumber,
                Poles = 1
            };
            if (target.CreatesCircuit && details != null)
            {
                circuit.Description = details.Description;
                circuit.BreakerSize = details.BreakerSize;
                circuit.ProtectionType = details.ProtectionType;
                circuit.EarthLeakage = details.EarthLeakage;
            }
            if (target.Circuit == null) destination.Circuits.Add(circuit);

            foreach (FixtureRef fixture in moving)
            {
                fixture.Circuit.Fixtures.Remove(fixture.Fixture);
                circuit.Fixtures.Add(fixture.Fixture);
            }
            foreach (FixtureRef source in moving)
                if (!CircuitLayout.IsOccupied(source.Circuit) && !CircuitLayout.HasDetails(source.Circuit))
                    source.Chassis.Circuits.Remove(source.Circuit);
            return circuit;
        }
    }
}

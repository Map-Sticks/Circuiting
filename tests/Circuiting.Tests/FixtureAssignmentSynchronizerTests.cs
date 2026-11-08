using Circuiting.Models;
using Circuiting.Services;
using Circuiting.Storage;
using Xunit;

namespace Circuiting.Tests;

public class FixtureAssignmentSynchronizerTests
{
    [Fact]
    public void DeletingAndUndoingAssignmentClearsAndRestoresFixtureStorage()
    {
        var (original, fixture, assignment) = Project();
        var store = new FakeStore(fixture, assignment);
        var empty = Clone(original);
        empty.Boards[0].Chassis[0].Circuits[0].Fixtures.Clear();

        FixtureAssignmentSynchronizer.Synchronize(Clone(original), empty, store);
        Assert.Null(store.Assignment);

        FixtureAssignmentSynchronizer.Synchronize(empty, Clone(original), store);
        Assert.Equal(assignment.CircuitGuid, store.Assignment?.CircuitGuid);
        Assert.Equal(assignment.FixtureGuid, store.Assignment?.FixtureGuid);
    }

    [Fact]
    public void RelabellingUpdatesFixtureReferenceWithoutChangingIdentity()
    {
        var (original, fixture, assignment) = Project();
        var store = new FakeStore(fixture, assignment);
        var next = Clone(original);
        next.Boards[0].Chassis[0].Circuits[0].DisplayLabel = "NEW";

        FixtureAssignmentSynchronizer.Synchronize(Clone(original), next, store);

        Assert.Equal("NEW", store.Assignment?.CircuitReference);
        Assert.Equal(assignment.FixtureGuid, store.Assignment?.FixtureGuid);
    }

    [Fact]
    public void SerializedChassisRelabellingUpdatesTheFixtureReference()
    {
        var (original, fixture, assignment) = Project();
        var store = new FakeStore(fixture, assignment);
        var next = Clone(original);
        next.Boards[0].Chassis[0].Prefix = "NEW";
        next.Boards[0].Chassis[0].Separator = "-";
        next = Clone(next);

        FixtureAssignmentSynchronizer.Synchronize(Clone(original), next, store);

        Assert.Equal("NEW-R-1", store.Assignment?.CircuitReference);
        Assert.Equal(assignment.FixtureGuid, store.Assignment?.FixtureGuid);
    }

    [Fact]
    public void ExternalAssignmentConflictIsRejectedBeforeAnyWrite()
    {
        var (original, fixture, assignment) = Project();
        var unrelated = new FixtureAssignment
        {
            FixtureGuid = Guid.NewGuid(), BoardGuid = assignment.BoardGuid,
            ChassisGuid = assignment.ChassisGuid, CircuitGuid = assignment.CircuitGuid
        };
        var store = new FakeStore(fixture, unrelated);
        var next = Clone(original);
        next.Boards[0].Chassis[0].Circuits[0].Fixtures.Clear();

        Assert.Throws<InvalidDataException>(() =>
            FixtureAssignmentSynchronizer.Synchronize(Clone(original), next, store));
        Assert.Same(unrelated, store.Assignment);
        Assert.Equal(0, store.Changes);
    }

    [Fact]
    public void RestoringFixtureCannotOverwriteAnotherCircuitWithTheSameFixtureGuid()
    {
        var (original, fixture, assignment) = Project();
        var unrelated = new FixtureAssignment
        {
            FixtureGuid = assignment.FixtureGuid, BoardGuid = assignment.BoardGuid,
            ChassisGuid = assignment.ChassisGuid, CircuitGuid = Guid.NewGuid()
        };
        var store = new FakeStore(fixture, unrelated);
        var previous = Clone(original);
        previous.Boards[0].Chassis[0].Circuits[0].Fixtures.Clear();

        Assert.Throws<InvalidDataException>(() =>
            FixtureAssignmentSynchronizer.Synchronize(previous, Clone(original), store));
        Assert.Same(unrelated, store.Assignment);
        Assert.Equal(0, store.Changes);
    }

    [Fact]
    public void RestoringFixtureMayRefreshTheReferenceForAnIdenticalAssignment()
    {
        var (original, fixture, assignment) = Project();
        var stale = new FixtureAssignment
        {
            FixtureGuid = assignment.FixtureGuid, BoardGuid = assignment.BoardGuid,
            ChassisGuid = assignment.ChassisGuid, CircuitGuid = assignment.CircuitGuid,
            CircuitReference = "old reference"
        };
        var store = new FakeStore(fixture, stale);
        var previous = Clone(original);
        previous.Boards[0].Chassis[0].Circuits[0].Fixtures.Clear();

        FixtureAssignmentSynchronizer.Synchronize(previous, Clone(original), store);

        Assert.Equal(assignment.CircuitReference, store.Assignment?.CircuitReference);
        Assert.Equal(1, store.Changes);
    }

    [Fact]
    public void MissingUniqueIdDoesNotFallBackToAReusedNumericId()
    {
        var (original, fixture, assignment) = Project();
        var store = new FakeStore(fixture, assignment) { IsMissing = true };
        var next = Clone(original);

        int missing = FixtureAssignmentSynchronizer.Synchronize(Clone(original), next, store);

        Assert.Equal(1, missing);
        Assert.Single(next.Boards[0].Chassis[0].Circuits[0].Fixtures);
        Assert.Equal(0, store.Changes);
    }

    [Fact]
    public void LegacyNumericIdRequiresMatchingFixtureIdentity()
    {
        var (original, fixture, assignment) = Project();
        fixture.RevitUniqueId = string.Empty;
        var store = new FakeStore(fixture, new FixtureAssignment { FixtureGuid = Guid.NewGuid() });

        Assert.Throws<InvalidDataException>(() =>
            FixtureAssignmentSynchronizer.Synchronize(Clone(original), Clone(original), store));
        Assert.Equal(0, store.Changes);
    }

    [Fact]
    public void AConflictOnASecondFixturePreventsEarlierPlannedWrites()
    {
        var (original, first, assignment) = Project();
        var second = new CircuitedFixture
        {
            FixtureGuid = Guid.NewGuid(), RevitElementId = 43, RevitUniqueId = "fixture-43"
        };
        original.Boards[0].Chassis[0].Circuits[0].Fixtures.Add(second);
        var unrelated = new FixtureAssignment
        {
            FixtureGuid = Guid.NewGuid(), BoardGuid = assignment.BoardGuid,
            ChassisGuid = assignment.ChassisGuid, CircuitGuid = assignment.CircuitGuid
        };
        var store = new MultipleFixtureStore(first, assignment, second, unrelated);
        var next = Clone(original);
        next.Boards[0].Chassis[0].Circuits[0].Fixtures.Clear();

        Assert.Throws<InvalidDataException>(() =>
            FixtureAssignmentSynchronizer.Synchronize(Clone(original), next, store));
        Assert.Equal(0, store.Changes);
        Assert.Same(assignment, store.Assignments[first.RevitUniqueId]);
        Assert.Same(unrelated, store.Assignments[second.RevitUniqueId]);
    }

    private static (ProjectData Project, CircuitedFixture Fixture, FixtureAssignment Assignment) Project()
    {
        var chassis = CircuitLayoutTests.Chassis(6);
        var board = CircuitLayoutTests.Board(chassis);
        var fixture = new CircuitedFixture
        {
            FixtureGuid = Guid.NewGuid(), RevitElementId = 42, RevitUniqueId = "fixture-42"
        };
        var circuit = new CircuitData
        {
            CircuitGuid = Guid.NewGuid(), PoleIndex = 0, DisplayLabel = "R1",
            Fixtures = new List<CircuitedFixture> { fixture }
        };
        chassis.Circuits.Add(circuit);
        var assignment = new FixtureAssignment
        {
            FixtureGuid = fixture.FixtureGuid, BoardGuid = board.BoardGuid,
            ChassisGuid = chassis.ChassisGuid, CircuitGuid = circuit.CircuitGuid,
            CircuitReference = circuit.DisplayLabel
        };
        return (new ProjectData { Boards = new List<BoardData> { board } }, fixture, assignment);
    }

    private static ProjectData Clone(ProjectData project) =>
        ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(project));

    private sealed class FakeStore : IFixtureAssignmentStore<CircuitedFixture>
    {
        private readonly CircuitedFixture _element;
        public FixtureAssignment? Assignment { get; private set; }
        public bool IsMissing { get; set; }
        public int Changes { get; private set; }

        public FakeStore(CircuitedFixture element, FixtureAssignment? assignment)
        {
            _element = element;
            Assignment = assignment;
        }

        public CircuitedFixture? Resolve(CircuitedFixture fixture) => IsMissing ? null : _element;
        public string GetUniqueId(CircuitedFixture element) => "fixture-42";
        public long GetElementId(CircuitedFixture element) => 42;
        public FixtureAssignment? Read(CircuitedFixture element) => Assignment;
        public void Write(CircuitedFixture element, FixtureAssignment assignment)
        {
            Assignment = assignment;
            Changes++;
        }
        public void Clear(CircuitedFixture element)
        {
            Assignment = null;
            Changes++;
        }
    }

    private sealed class MultipleFixtureStore : IFixtureAssignmentStore<CircuitedFixture>
    {
        private readonly Dictionary<string, CircuitedFixture> _elements;
        public Dictionary<string, FixtureAssignment?> Assignments { get; }
        public int Changes { get; private set; }

        public MultipleFixtureStore(CircuitedFixture first, FixtureAssignment firstAssignment,
            CircuitedFixture second, FixtureAssignment secondAssignment)
        {
            _elements = new[] { first, second }.ToDictionary(f => f.RevitUniqueId);
            Assignments = new Dictionary<string, FixtureAssignment?>
            {
                [first.RevitUniqueId] = firstAssignment,
                [second.RevitUniqueId] = secondAssignment
            };
        }

        public CircuitedFixture? Resolve(CircuitedFixture fixture) =>
            _elements.TryGetValue(fixture.RevitUniqueId, out var element) ? element : null;
        public string GetUniqueId(CircuitedFixture element) => element.RevitUniqueId;
        public long GetElementId(CircuitedFixture element) => element.RevitElementId;
        public FixtureAssignment? Read(CircuitedFixture element) => Assignments[element.RevitUniqueId];
        public void Write(CircuitedFixture element, FixtureAssignment assignment)
        {
            Assignments[element.RevitUniqueId] = assignment;
            Changes++;
        }
        public void Clear(CircuitedFixture element)
        {
            Assignments[element.RevitUniqueId] = null;
            Changes++;
        }
    }
}

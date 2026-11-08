using Circuiting.Models;
using Circuiting.Services;
using Xunit;

namespace Circuiting.Tests;

public class FixtureModelValidatorTests
{
    [Fact]
    public void ValidationPlansMissingOrphanAndCopiedFixtureRepairsBeforeApplying()
    {
        var (project, original, originalAssignment) = Project();
        var missing = new CircuitedFixture
        {
            FixtureGuid = Guid.NewGuid(), RevitUniqueId = "missing", RevitElementId = 99
        };
        project.Boards[0].Chassis[0].Circuits[0].Fixtures.Add(missing);
        var copy = new FakeElement("copy", 2, originalAssignment);
        var orphan = new FakeElement("orphan", 3, new FixtureAssignment { FixtureGuid = Guid.NewGuid() });
        var store = new FakeStore(original, copy, orphan);

        FixtureValidationPlan plan = FixtureModelValidator.Analyze(project, store);

        Assert.Equal(1, plan.MissingFixtures);
        Assert.Equal(1, plan.CopiedFixturesCleared);
        Assert.Equal(1, plan.OrphanAssignments);
        Assert.Equal(0, store.Changes);
        plan.Apply();
        Assert.Null(copy.Assignment);
        Assert.Null(orphan.Assignment);
        Assert.Same(originalAssignment, original.Assignment);
        Assert.Single(project.Boards[0].Chassis[0].Circuits[0].Fixtures);
    }

    [Fact]
    public void CopyRuleCanAssignCopyWithANewFixtureIdentity()
    {
        var (project, original, originalAssignment) = Project();
        project.Settings.AutoRules.OnFixtureCopied = CopiedFixtureAction.AssignToSameCircuit;
        var copy = new FakeElement("copy", 2, originalAssignment);
        var store = new FakeStore(original, copy);

        FixtureValidationPlan plan = FixtureModelValidator.Analyze(project, store);
        Assert.Equal(1, plan.CopiedFixturesAssigned);
        Assert.Equal(2, project.Boards[0].Chassis[0].Circuits[0].Fixtures.Count);
        plan.Apply();

        Assert.NotEqual(originalAssignment.FixtureGuid, copy.Assignment?.FixtureGuid);
        Assert.Equal(originalAssignment.CircuitGuid, copy.Assignment?.CircuitGuid);
    }

    [Fact]
    public void ValidationRepairsWrongReferenceFromProjectData()
    {
        var (project, original, assignment) = Project();
        original.Assignment = new FixtureAssignment
        {
            FixtureGuid = assignment.FixtureGuid, BoardGuid = assignment.BoardGuid,
            ChassisGuid = assignment.ChassisGuid, CircuitGuid = assignment.CircuitGuid,
            CircuitReference = "old"
        };
        var store = new FakeStore(original);

        FixtureValidationPlan plan = FixtureModelValidator.Analyze(project, store);
        Assert.Equal(1, plan.RepairedAssignments);
        plan.Apply();
        Assert.Equal("R1", original.Assignment?.CircuitReference);
    }

    [Fact]
    public void AmbiguousLegacyIdIsNotGuessed()
    {
        var (project, original, _) = Project();
        project.Boards[0].Chassis[0].Circuits[0].Fixtures[0].RevitUniqueId = string.Empty;
        original.Assignment = null;
        var store = new FakeStore(original);

        Assert.Throws<InvalidDataException>(() => FixtureModelValidator.Analyze(project, store));
        Assert.Equal(0, store.Changes);
    }

    [Fact]
    public void AutomaticResolutionTouchesOnlyAddedCopiesAndKnownDeletedFixtures()
    {
        var (project, original, originalAssignment) = Project();
        var circuit = project.Boards[0].Chassis[0].Circuits[0];
        circuit.Fixtures.Add(new CircuitedFixture
        {
            FixtureGuid = Guid.NewGuid(), RevitUniqueId = "deleted", RevitElementId = 4
        });
        circuit.Fixtures.Add(new CircuitedFixture
        {
            FixtureGuid = Guid.NewGuid(), RevitUniqueId = "unrelated-missing", RevitElementId = 5
        });
        var copy = new FakeElement("copy", 2, originalAssignment);
        var unrelatedOrphan = new FakeElement("orphan", 3, new FixtureAssignment { FixtureGuid = Guid.NewGuid() });
        var store = new FakeStore(original, copy, unrelatedOrphan);

        FixtureValidationPlan plan = FixtureModelValidator.AnalyzeChanges(
            project, store, new long[] { 2, 3 }, new long[] { 4 });

        Assert.Equal(1, plan.CopiedFixturesCleared);
        Assert.Equal(1, plan.MissingFixtures);
        Assert.Contains(circuit.Fixtures, f => f.RevitUniqueId == "unrelated-missing");
        plan.Apply();
        Assert.Null(copy.Assignment);
        Assert.NotNull(unrelatedOrphan.Assignment);
    }

    [Fact]
    public void AutomaticDeletionDoesNotGuessLegacyNumericIdentity()
    {
        var (project, original, _) = Project();
        var fixture = project.Boards[0].Chassis[0].Circuits[0].Fixtures[0];
        fixture.RevitUniqueId = string.Empty;
        var store = new FakeStore();

        FixtureValidationPlan plan = FixtureModelValidator.AnalyzeChanges(
            project, store, Array.Empty<long>(), new long[] { original.ElementId });

        Assert.False(plan.HasChanges);
        Assert.Single(project.Boards[0].Chassis[0].Circuits[0].Fixtures);
    }

    [Fact]
    public void AutomaticResolutionDoesNotClearConflictingCopiedMetadata()
    {
        var (project, original, originalAssignment) = Project();
        var conflict = new FakeElement("copy", 2, new FixtureAssignment
        {
            FixtureGuid = originalAssignment.FixtureGuid,
            BoardGuid = Guid.NewGuid(),
            ChassisGuid = originalAssignment.ChassisGuid,
            CircuitGuid = originalAssignment.CircuitGuid
        });
        var store = new FakeStore(original, conflict);

        FixtureValidationPlan plan = FixtureModelValidator.AnalyzeChanges(
            project, store, new long[] { 2 }, Array.Empty<long>());

        Assert.False(plan.HasChanges);
        Assert.NotNull(conflict.Assignment);
    }

    private static (ProjectData Project, FakeElement Element, FixtureAssignment Assignment) Project()
    {
        var chassis = CircuitLayoutTests.Chassis(6);
        var board = CircuitLayoutTests.Board(chassis);
        var fixture = new CircuitedFixture
        {
            FixtureGuid = Guid.NewGuid(), RevitElementId = 1, RevitUniqueId = "original"
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
            ChassisGuid = chassis.ChassisGuid, CircuitGuid = circuit.CircuitGuid, CircuitReference = "R1"
        };
        return (new ProjectData { Boards = new List<BoardData> { board } },
            new FakeElement("original", 1, assignment), assignment);
    }

    private sealed class FakeElement
    {
        public FakeElement(string id, long elementId, FixtureAssignment? assignment)
        {
            UniqueId = id;
            ElementId = elementId;
            Assignment = assignment;
        }
        public string UniqueId { get; }
        public long ElementId { get; }
        public FixtureAssignment? Assignment { get; set; }
    }

    private sealed class FakeStore : IFixtureModelStore<FakeElement>
    {
        private readonly List<FakeElement> _elements;
        public int Changes { get; private set; }
        public FakeStore(params FakeElement[] elements) => _elements = elements.ToList();
        public FakeElement? Resolve(CircuitedFixture fixture) =>
            _elements.SingleOrDefault(e => string.IsNullOrEmpty(fixture.RevitUniqueId)
                ? e.ElementId == fixture.RevitElementId : e.UniqueId == fixture.RevitUniqueId);
        public string GetUniqueId(FakeElement element) => element.UniqueId;
        public long GetElementId(FakeElement element) => element.ElementId;
        public FixtureAssignment? Read(FakeElement element) => element.Assignment;
        public void Write(FakeElement element, FixtureAssignment assignment)
        {
            element.Assignment = assignment;
            Changes++;
        }
        public void Clear(FakeElement element)
        {
            element.Assignment = null;
            Changes++;
        }
        public IEnumerable<FakeElement> GetFixtureElements(IEnumerable<long>? elementIds = null)
        {
            if (elementIds == null) return _elements;
            var ids = new HashSet<long>(elementIds);
            return _elements.Where(e => ids.Contains(e.ElementId));
        }
        public CircuitedFixture CreateFixture(FakeElement element, Guid fixtureGuid) => new()
        {
            FixtureGuid = fixtureGuid, RevitElementId = element.ElementId, RevitUniqueId = element.UniqueId
        };
    }
}

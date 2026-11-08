using System.IO;
using Circuiting.Models;

namespace Circuiting.Services
{
    public interface IFixtureModelStore<TElement> : IFixtureAssignmentStore<TElement> where TElement : class
    {
        IEnumerable<TElement> GetFixtureElements(IEnumerable<long>? elementIds = null);
        CircuitedFixture CreateFixture(TElement element, Guid fixtureGuid);
    }

    public sealed class FixtureValidationPlan
    {
        private readonly List<Action> _changes = new List<Action>();

        public ProjectData Project { get; set; } = null!;
        public int MissingFixtures { get; set; }
        public int OrphanAssignments { get; set; }
        public int CopiedFixturesCleared { get; set; }
        public int CopiedFixturesAssigned { get; set; }
        public int RepairedAssignments { get; set; }
        public int UpdatedIdentities { get; set; }
        public bool HasChanges => _changes.Count > 0 || MissingFixtures > 0 || UpdatedIdentities > 0;

        internal void AddChange(Action change) => _changes.Add(change);
        public void Apply()
        {
            foreach (Action change in _changes) change();
        }

        public string Describe() =>
            $"Missing fixture records to remove: {MissingFixtures}\n" +
            $"Orphan assignments to clear: {OrphanAssignments}\n" +
            $"Copied fixtures to clear: {CopiedFixturesCleared}\n" +
            $"Copied fixtures to assign to their source circuit: {CopiedFixturesAssigned}\n" +
            $"Fixture assignments to repair: {RepairedAssignments}\n" +
            $"Legacy fixture identities to update: {UpdatedIdentities}";
    }

    public static class FixtureModelValidator
    {
        private sealed class Source
        {
            public BoardData Board { get; set; } = null!;
            public ChassisData Chassis { get; set; } = null!;
            public CircuitData Circuit { get; set; } = null!;
            public CircuitedFixture Fixture { get; set; } = null!;
        }

        public static FixtureValidationPlan Analyze<TElement>(
            ProjectData project, IFixtureModelStore<TElement> store) where TElement : class
        {
            if (!Enum.IsDefined(typeof(CopiedFixtureAction), project.Settings.AutoRules.OnFixtureCopied) ||
                !Enum.IsDefined(typeof(DeletedFixtureAction), project.Settings.AutoRules.OnFixtureDeleted))
                throw new InvalidDataException("The fixture resolution rules are invalid.");

            var plan = new FixtureValidationPlan { Project = project };
            var sourcesByGuid = new Dictionary<Guid, Source>();
            var expectedByUniqueId = new Dictionary<string, Source>(StringComparer.Ordinal);
            var expectedElements = new Dictionary<string, TElement>(StringComparer.Ordinal);

            foreach (BoardData board in project.Boards)
            foreach (ChassisData chassis in board.Chassis)
            foreach (CircuitData circuit in chassis.Circuits)
            foreach (CircuitedFixture fixture in circuit.Fixtures.ToList())
            {
                var source = new Source { Board = board, Chassis = chassis, Circuit = circuit, Fixture = fixture };
                if (sourcesByGuid.ContainsKey(fixture.FixtureGuid))
                    throw new InvalidDataException("A fixture identity occurs in more than one circuit.");
                sourcesByGuid.Add(fixture.FixtureGuid, source);

                TElement? element = store.Resolve(fixture);
                if (element == null)
                {
                    circuit.Fixtures.Remove(fixture);
                    plan.MissingFixtures++;
                    continue;
                }

                string uniqueId = store.GetUniqueId(element);
                if (string.IsNullOrEmpty(uniqueId))
                    throw new InvalidDataException("A fixture has no Revit unique identifier.");
                if (string.IsNullOrEmpty(fixture.RevitUniqueId))
                {
                    FixtureIdentity.RequireLegacyAssignment(fixture, store.Read(element));
                    fixture.RevitUniqueId = uniqueId;
                    plan.UpdatedIdentities++;
                }
                long currentId = store.GetElementId(element);
                if (fixture.RevitElementId != currentId)
                {
                    fixture.RevitElementId = currentId;
                    plan.UpdatedIdentities++;
                }
                if (expectedByUniqueId.ContainsKey(uniqueId))
                    throw new InvalidDataException($"Fixture '{uniqueId}' occurs in more than one circuit.");
                expectedByUniqueId.Add(uniqueId, source);
                expectedElements.Add(uniqueId, element);
            }

            var eligibleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (TElement element in store.GetFixtureElements())
            {
                string uniqueId = store.GetUniqueId(element);
                if (!eligibleIds.Add(uniqueId))
                    throw new InvalidDataException($"Fixture '{uniqueId}' was encountered more than once.");
                FixtureAssignment? actual = store.Read(element);
                if (expectedByUniqueId.TryGetValue(uniqueId, out Source? expected))
                {
                    FixtureAssignment desired = AssignmentFor(expected);
                    if (!Matches(actual, desired))
                    {
                        plan.RepairedAssignments++;
                        plan.AddChange(() => store.Write(element, desired));
                    }
                    continue;
                }

                if (actual == null) continue;
                if (sourcesByGuid.TryGetValue(actual.FixtureGuid, out Source? source))
                {
                    PlanCopy(project, store, plan, source, element);
                }
                else
                {
                    plan.OrphanAssignments++;
                    plan.AddChange(() => store.Clear(element));
                }
            }

            foreach (var expected in expectedElements)
                if (!eligibleIds.Contains(expected.Key))
                    throw new InvalidDataException(
                        $"Referenced element '{expected.Key}' is not an eligible fixture or equipment element.");
            return plan;
        }

        public static FixtureValidationPlan AnalyzeChanges<TElement>(ProjectData project,
            IFixtureModelStore<TElement> store, IEnumerable<long> addedIds, IEnumerable<long> deletedIds)
            where TElement : class
        {
            if (!Enum.IsDefined(typeof(CopiedFixtureAction), project.Settings.AutoRules.OnFixtureCopied) ||
                !Enum.IsDefined(typeof(DeletedFixtureAction), project.Settings.AutoRules.OnFixtureDeleted))
                throw new InvalidDataException("The fixture resolution rules are invalid.");

            var plan = new FixtureValidationPlan { Project = project };
            var sources = new Dictionary<Guid, Source>();
            foreach (BoardData board in project.Boards)
            foreach (ChassisData chassis in board.Chassis)
            foreach (CircuitData circuit in chassis.Circuits)
            foreach (CircuitedFixture fixture in circuit.Fixtures)
            {
                if (sources.ContainsKey(fixture.FixtureGuid))
                    throw new InvalidDataException("A fixture identity occurs in more than one circuit.");
                sources.Add(fixture.FixtureGuid, new Source
                {
                    Board = board, Chassis = chassis, Circuit = circuit, Fixture = fixture
                });
            }

            foreach (TElement element in store.GetFixtureElements(addedIds))
            {
                FixtureAssignment? actual = store.Read(element);
                if (actual == null || !sources.TryGetValue(actual.FixtureGuid, out Source? source))
                    continue;
                if (actual.BoardGuid != source.Board.BoardGuid ||
                    actual.ChassisGuid != source.Chassis.ChassisGuid ||
                    actual.CircuitGuid != source.Circuit.CircuitGuid)
                    continue;
                string uniqueId = store.GetUniqueId(element);
                if ((!string.IsNullOrEmpty(source.Fixture.RevitUniqueId) &&
                     source.Fixture.RevitUniqueId == uniqueId) ||
                    (string.IsNullOrEmpty(source.Fixture.RevitUniqueId) &&
                     source.Fixture.RevitElementId == store.GetElementId(element)))
                    continue;
                PlanCopy(project, store, plan, source, element);
            }

            var deleted = new HashSet<long>(deletedIds);
            foreach (Source source in sources.Values)
            {
                CircuitedFixture fixture = source.Fixture;
                if (!string.IsNullOrEmpty(fixture.RevitUniqueId) &&
                    deleted.Contains(fixture.RevitElementId) && store.Resolve(fixture) == null)
                {
                    source.Circuit.Fixtures.Remove(fixture);
                    plan.MissingFixtures++;
                }
            }
            return plan;
        }

        private static void PlanCopy<TElement>(ProjectData project, IFixtureModelStore<TElement> store,
            FixtureValidationPlan plan, Source source, TElement element) where TElement : class
        {
            if (project.Settings.AutoRules.OnFixtureCopied == CopiedFixtureAction.AssignToSameCircuit)
            {
                CircuitedFixture copy = store.CreateFixture(element, Guid.NewGuid());
                source.Circuit.Fixtures.Add(copy);
                var copiedSource = new Source
                {
                    Board = source.Board, Chassis = source.Chassis, Circuit = source.Circuit, Fixture = copy
                };
                FixtureAssignment desired = AssignmentFor(copiedSource);
                plan.CopiedFixturesAssigned++;
                plan.AddChange(() => store.Write(element, desired));
            }
            else
            {
                plan.CopiedFixturesCleared++;
                plan.AddChange(() => store.Clear(element));
            }
        }

        private static FixtureAssignment AssignmentFor(Source source) => new FixtureAssignment
        {
            FixtureGuid = source.Fixture.FixtureGuid,
            BoardGuid = source.Board.BoardGuid,
            ChassisGuid = source.Chassis.ChassisGuid,
            CircuitGuid = source.Circuit.CircuitGuid,
            CircuitReference = source.Circuit.DisplayLabel
        };

        private static bool Matches(FixtureAssignment? actual, FixtureAssignment expected) =>
            actual != null && FixtureIdentity.MatchesAssignment(actual, expected) &&
            actual.CircuitReference == expected.CircuitReference;
    }
}

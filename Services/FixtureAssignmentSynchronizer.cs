using System.IO;
using Circuiting.Models;

namespace Circuiting.Services
{
    public interface IFixtureAssignmentStore<TElement> where TElement : class
    {
        TElement? Resolve(CircuitedFixture fixture);
        string GetUniqueId(TElement element);
        long GetElementId(TElement element);
        FixtureAssignment? Read(TElement element);
        void Write(TElement element, FixtureAssignment assignment);
        void Clear(TElement element);
    }

    public static class FixtureAssignmentSynchronizer
    {
        private sealed class Binding<TElement> where TElement : class
        {
            public TElement Element { get; set; } = null!;
            public FixtureAssignment Assignment { get; set; } = null!;
        }

        public static int Synchronize<TElement>(
            ProjectData previous, ProjectData next, IFixtureAssignmentStore<TElement> store)
            where TElement : class
        {
            int ignored;
            Dictionary<string, Binding<TElement>> oldBindings = BuildBindings(previous, store, out ignored);
            Dictionary<string, Binding<TElement>> newBindings = BuildBindings(next, store, out int missing);
            var changes = new List<Action>();

            foreach (var old in oldBindings)
            {
                if (newBindings.ContainsKey(old.Key)) continue;
                FixtureAssignment? actual = store.Read(old.Value.Element);
                VerifyPrevious(actual, old.Value.Assignment, old.Key);
                if (actual != null)
                    changes.Add(() => store.Clear(old.Value.Element));
            }

            foreach (var nextBinding in newBindings)
            {
                FixtureAssignment? actual = store.Read(nextBinding.Value.Element);
                if (oldBindings.TryGetValue(nextBinding.Key, out Binding<TElement>? old))
                {
                    VerifyPrevious(actual, old.Assignment, nextBinding.Key);
                }
                else if (actual != null && !FixtureIdentity.MatchesAssignment(actual, nextBinding.Value.Assignment))
                {
                    throw new InvalidDataException(
                        $"Fixture '{nextBinding.Key}' has an unrelated assignment. Validate the model before saving.");
                }

                if (!Matches(actual, nextBinding.Value.Assignment))
                    changes.Add(() => store.Write(nextBinding.Value.Element, nextBinding.Value.Assignment));
            }

            foreach (Action change in changes) change();
            return missing;
        }

        private static Dictionary<string, Binding<TElement>> BuildBindings<TElement>(
            ProjectData project, IFixtureAssignmentStore<TElement> store, out int missing)
            where TElement : class
        {
            var bindings = new Dictionary<string, Binding<TElement>>(StringComparer.Ordinal);
            missing = 0;
            foreach (BoardData board in project.Boards)
            foreach (ChassisData chassis in board.Chassis)
            foreach (CircuitData circuit in chassis.Circuits)
            foreach (CircuitedFixture fixture in circuit.Fixtures)
            {
                TElement? element = store.Resolve(fixture);
                if (element == null)
                {
                    missing++;
                    continue;
                }

                string uniqueId = store.GetUniqueId(element);
                if (string.IsNullOrEmpty(uniqueId))
                    throw new InvalidDataException("A fixture has no Revit unique identifier.");
                if (string.IsNullOrEmpty(fixture.RevitUniqueId))
                {
                    FixtureIdentity.RequireLegacyAssignment(fixture, store.Read(element));
                }

                fixture.RevitUniqueId = uniqueId;
                fixture.RevitElementId = store.GetElementId(element);
                var binding = new Binding<TElement>
                {
                    Element = element,
                    Assignment = new FixtureAssignment
                    {
                        FixtureGuid = fixture.FixtureGuid,
                        BoardGuid = board.BoardGuid,
                        ChassisGuid = chassis.ChassisGuid,
                        CircuitGuid = circuit.CircuitGuid,
                        CircuitReference = circuit.DisplayLabel
                    }
                };
                if (bindings.ContainsKey(uniqueId))
                    throw new InvalidDataException(
                        $"Fixture '{uniqueId}' appears in more than one circuit.");
                bindings.Add(uniqueId, binding);
            }

            return bindings;
        }

        private static void VerifyPrevious(FixtureAssignment? actual, FixtureAssignment expected, string uniqueId)
        {
            if (actual != null && !FixtureIdentity.MatchesAssignment(actual, expected))
                throw new InvalidDataException(
                    $"Fixture '{uniqueId}' was changed outside the circuit manager. Validate the model before saving.");
        }

        private static bool Matches(FixtureAssignment? actual, FixtureAssignment expected)
        {
            return actual != null && FixtureIdentity.MatchesAssignment(actual, expected) &&
                actual.CircuitReference == expected.CircuitReference;
        }
    }
}

using System.IO;
using Circuiting.Models;

namespace Circuiting.Services
{
    public static class FixtureIdentity
    {
        public static bool MatchesAssignment(FixtureAssignment? actual, FixtureAssignment expected) =>
            actual != null && actual.FixtureGuid == expected.FixtureGuid &&
            actual.BoardGuid == expected.BoardGuid && actual.ChassisGuid == expected.ChassisGuid &&
            actual.CircuitGuid == expected.CircuitGuid;

        public static void RequireLegacyAssignment(CircuitedFixture fixture, FixtureAssignment? actual)
        {
            if (actual == null || actual.FixtureGuid != fixture.FixtureGuid)
                throw new InvalidDataException(
                    $"Legacy fixture {fixture.RevitElementId} cannot be identified safely. " +
                    "Its stored assignment must be resolved manually.");
        }
    }
}

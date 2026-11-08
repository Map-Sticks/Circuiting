using Circuiting.Models;
using Circuiting.Services;
using Xunit;

namespace Circuiting.Tests;

public class FixtureIdentityTests
{
    [Fact]
    public void LegacyElementIdRequiresTheSameStoredFixtureGuid()
    {
        var fixture = new CircuitedFixture { FixtureGuid = Guid.NewGuid(), RevitElementId = 42 };
        Assert.Throws<InvalidDataException>(() => FixtureIdentity.RequireLegacyAssignment(fixture, null));
        Assert.Throws<InvalidDataException>(() =>
            FixtureIdentity.RequireLegacyAssignment(fixture, new FixtureAssignment { FixtureGuid = Guid.NewGuid() }));

        FixtureIdentity.RequireLegacyAssignment(fixture, new FixtureAssignment { FixtureGuid = fixture.FixtureGuid });
    }
}

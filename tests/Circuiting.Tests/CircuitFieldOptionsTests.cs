using Circuiting.Models;
using Circuiting.ViewModels;
using Xunit;

namespace Circuiting.Tests;

public class CircuitFieldOptionsTests
{
    [Fact]
    public void ExistingCustomValuesRemainAvailableWithoutDuplicatingStandardOptions()
    {
        Assert.Contains("80A", CircuitFieldOptions.IncludeCurrent(CircuitOptions.BreakerSizes, "80A"));
        Assert.Equal(CircuitOptions.BreakerSizes, CircuitFieldOptions.IncludeCurrent(CircuitOptions.BreakerSizes, "6A"));
    }

    [Fact]
    public void SinglePhasePreservesAnExistingMultipoleCircuitButDoesNotOfferNewOnes()
    {
        Assert.Equal(new[] { 1 }, CircuitFieldOptions.Poles(false, null));
        Assert.Equal(new[] { 1, 3 }, CircuitFieldOptions.Poles(false, 3));
        Assert.Equal(new[] { 1, 2, 3 }, CircuitFieldOptions.Poles(true, 3));
    }
}

using Circuiting.Models;
using Circuiting.Services;
using Xunit;

namespace Circuiting.Tests;

public class CircuitLayoutTests
{
    [Fact]
    public void PhysicalPositionsRemainUniqueWhenDisplayLabelsRepeat()
    {
        var chassis = Chassis(12);
        chassis.ShowPhaseLabel = false;

        var poles = CircuitLabelCalculator.Calculate(chassis);

        Assert.Equal(12, poles.Select(p => p.PoleIndex).Distinct().Count());
        Assert.True(poles.Select(p => p.DisplayLabel).Distinct().Count() < poles.Count);
    }

    [Fact]
    public void RelabellingKeepsCircuitAtItsPhysicalPosition()
    {
        var chassis = Chassis(12);
        var circuit = new CircuitData { CircuitGuid = Guid.NewGuid(), PoleIndex = 4 };
        chassis.Circuits.Add(circuit);
        var board = Board(chassis);

        CircuitLayout.ValidateAndUpdate(board, chassis);
        var original = circuit.DisplayLabel;
        chassis.Prefix = "NEW";
        chassis.StartingCircuit = 11;
        CircuitLayout.ValidateAndUpdate(board, chassis);

        Assert.NotEqual(original, circuit.DisplayLabel);
        Assert.Equal(4, circuit.PoleIndex);
        Assert.Equal(CircuitLabelCalculator.Calculate(board, chassis)[4].DisplayLabel, circuit.DisplayLabel);
    }

    [Fact]
    public void MultipoleCircuitsConsumeOnlyFollowingPolesOnTheSameSide()
    {
        var chassis = Chassis(12);
        var circuit = new CircuitData { CircuitGuid = Guid.NewGuid(), PoleIndex = 1, Poles = 3 };
        chassis.Circuits.Add(circuit);

        CircuitLayout.ValidateAndUpdate(Board(chassis), chassis);
        var consumed = CircuitLayout.GetConsumedPoles(chassis);

        Assert.Equal(new[] { 3, 5 }, consumed.Keys.OrderBy(i => i));
        Assert.All(consumed.Values, parent => Assert.Same(circuit, parent));
    }

    [Fact]
    public void OverlappingAndOutOfBoundsCircuitsAreRejected()
    {
        var chassis = Chassis(6);
        chassis.Circuits.Add(new CircuitData { CircuitGuid = Guid.NewGuid(), PoleIndex = 0, Poles = 2 });
        chassis.Circuits.Add(new CircuitData { CircuitGuid = Guid.NewGuid(), PoleIndex = 2 });
        var board = Board(chassis);

        Assert.Throws<InvalidDataException>(() => CircuitLayout.ValidateAndUpdate(board, chassis));

        chassis.Circuits.RemoveAt(1);
        chassis.Circuits[0].PoleIndex = 4;
        Assert.Throws<InvalidDataException>(() => CircuitLayout.ValidateAndUpdate(board, chassis));
    }

    [Fact]
    public void ContinuedPhaseStartsAfterPreviousChassis()
    {
        var first = Chassis(8);
        var second = Chassis(6);
        second.ContinuePhaseFromPrevious = true;
        var board = Board(first, second);

        var poles = CircuitLabelCalculator.Calculate(board, second);

        Assert.Equal("W", poles[0].PhaseLabel);
        Assert.Equal("B", poles[2].PhaseLabel);
    }

    [Fact]
    public void ContinuedPhaseOffsetsPropagateAndResetAcrossChassis()
    {
        var first = Chassis(8);
        var second = Chassis(8);
        var third = Chassis(6);
        second.ContinuePhaseFromPrevious = true;
        third.ContinuePhaseFromPrevious = true;
        var board = Board(first, second, third);

        Assert.Equal("B", CircuitLabelCalculator.Calculate(board, third)[0].PhaseLabel);
        second.ContinuePhaseFromPrevious = false;
        Assert.Equal("W", CircuitLabelCalculator.Calculate(board, third)[0].PhaseLabel);
    }

    [Fact]
    public void CircuitNumberOverflowIsReportedOnlyWhenAnotherNumberIsNeeded()
    {
        var chassis = Chassis(6);
        chassis.StartingCircuit = int.MaxValue - 1;
        var poles = CircuitLabelCalculator.Calculate(chassis);
        Assert.Equal(int.MaxValue, poles[1].CircuitNumber);

        chassis.MaxPoles = 8;
        Assert.Throws<OverflowException>(() => CircuitLabelCalculator.Calculate(chassis));
    }

    [Fact]
    public void MultipoleStartsRequireTheFirstPhaseAndEnoughFollowingRows()
    {
        var chassis = Chassis(8);
        var poles = CircuitLabelCalculator.Calculate(chassis);
        Assert.True(CircuitLayout.IsValidMultiPoleStart(poles[0], chassis, 3, poles));
        Assert.False(CircuitLayout.IsValidMultiPoleStart(poles[2], chassis, 2, poles));
        Assert.False(CircuitLayout.IsValidMultiPoleStart(poles[6], chassis, 2, poles));
        Assert.False(CircuitLayout.IsValidMultiPoleStart(poles[0], chassis, 4, poles));
    }

    internal static ChassisData Chassis(int poles) => new()
    {
        ChassisGuid = Guid.NewGuid(),
        Name = "Main",
        MaxPoles = poles,
        StartingCircuit = 1,
        PhaseLabels = new List<string> { "R", "W", "B" }
    };

    internal static BoardData Board(params ChassisData[] chassis) => new()
    {
        BoardGuid = Guid.NewGuid(),
        DisplayName = "Board",
        Chassis = chassis.ToList()
    };
}

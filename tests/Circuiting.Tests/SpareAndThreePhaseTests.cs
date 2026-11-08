using Circuiting.Models;
using Circuiting.Services;
using Circuiting.ViewModels;
using Xunit;

namespace Circuiting.Tests;

public class SpareAndThreePhaseTests
{
    private static CircuitData Record(int pole, string description = "", bool fixtures = false) => new()
    {
        CircuitGuid = Guid.NewGuid(), PoleIndex = pole, Description = description,
        Fixtures = fixtures ? new List<CircuitedFixture> { new() { FixtureGuid = Guid.NewGuid() } } : new()
    };

    [Fact]
    public void OnlyFixturesMakeAPositionOccupied()
    {
        var spare = Record(0, "Lights");
        spare.BreakerSize = "16A";
        spare.ProtectionType = "RCBO";
        Assert.False(CircuitLayout.IsOccupied(spare));
        Assert.True(CircuitLayout.HasDetails(spare));
        Assert.False(CircuitLayout.HasDetails(Record(0, "  ")));
        Assert.True(CircuitLayout.IsOccupied(Record(0, fixtures: true)));
    }

    [Fact]
    public void DisplayDistinguishesSpaceSpareAndOccupied()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        var spare = Record(0, "Lights");
        spare.BreakerSize = "16A";
        spare.ProtectionType = "RCBO";
        var protectionOnly = Record(1);
        protectionOnly.ProtectionType = "MCB";
        chassis.Circuits.AddRange(new[] { spare, protectionOnly, Record(2), Record(3, fixtures: true) });
        CircuitLayout.ValidateAndUpdate(board, chassis);

        var rows = CircuitTreeProjection.BuildRows(board, chassis);

        Assert.Equal("Spare — Lights", rows[0].LeftDisplay.Description);
        Assert.Equal("16A", rows[0].LeftDisplay.Breaker);
        Assert.Equal("RCBO", rows[0].LeftDisplay.Protection);
        Assert.True(rows[0].LeftDisplay.IsSpare);
        Assert.Equal("Spare", rows[0].RightDisplay.Description);
        Assert.True(rows[0].RightDisplay.IsSpare);
        Assert.Equal("Space", rows[1].LeftDisplay.Description);
        Assert.True(rows[1].LeftDisplay.IsSpare);
        Assert.Equal("(no description) [1]", rows[1].RightDisplay.Description);
        Assert.False(rows[1].RightDisplay.IsSpare);
        Assert.Equal("Space", rows[2].LeftDisplay.Description);
    }

    [Fact]
    public void MoveReplacesSparesButNotOccupiedPositions()
    {
        var source = CircuitLayoutTests.Chassis(12);
        var target = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(source, target);
        var circuit = Record(0, "Kitchen", fixtures: true);
        source.Circuits.Add(circuit);
        var spare = Record(2, "Old spare");
        var occupied = Record(4, "Lights", fixtures: true);
        target.Circuits.AddRange(new[] { spare, occupied });

        Assert.Same(spare, Assert.Single(CircuitLayout.GetSparesReplacedByMove(circuit, target, 2)));
        Assert.NotNull(CircuitLayout.GetMoveError(source, circuit, board, target, 4));
        Assert.True(CircuitLayout.Move(source, circuit, board, target, 2));

        Assert.Empty(source.Circuits);
        Assert.Equal(new[] { occupied, circuit }, target.Circuits);
        Assert.Equal(2, circuit.PoleIndex);
    }

    [Fact]
    public void MultiPoleMoveReplacesEverySpareItCovers()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        var circuit = Record(1, "Supply", fixtures: true);
        circuit.Poles = 3;
        var spares = new[] { Record(6, "A"), Record(8), Record(10, "C") };
        chassis.Circuits.Add(circuit);
        chassis.Circuits.AddRange(spares);

        Assert.Equal(spares, CircuitLayout.GetSparesReplacedByMove(circuit, chassis, 6));
        Assert.True(CircuitLayout.Move(chassis, circuit, board, chassis, 6));
        Assert.Same(circuit, Assert.Single(chassis.Circuits));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(11)]
    public void AnyPositionInTheGroupConvertsTheWholeGroup(int clicked)
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        int expectedStart = clicked < 6 ? clicked % 2 : 6 + clicked % 2;

        CircuitData result = CircuitLayout.ConvertToThreePhase(board, chassis, clicked);

        Assert.Same(result, Assert.Single(chassis.Circuits));
        Assert.Equal(expectedStart, result.PoleIndex);
        Assert.Equal(3, result.Poles);
        Assert.Equal(CircuitLabelCalculator.Calculate(board, chassis)[expectedStart].DisplayLabel, result.DisplayLabel);
    }

    [Fact]
    public void ConversionKeepsClickedDetailsAndDropsOtherSpares()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        var first = Record(0, "First");
        var clicked = Record(2, "Clicked");
        var other = Record(1, "Other side");
        chassis.Circuits.AddRange(new[] { first, clicked, other });

        Assert.Null(CircuitLayout.GetThreePhaseConversion(board, chassis, 2, out ThreePhaseConversion? plan));
        Assert.Same(clicked, plan!.Kept);
        Assert.Equal(new[] { first }, plan.Replaced);

        CircuitData result = CircuitLayout.ConvertToThreePhase(board, chassis, 2);

        Assert.Same(clicked, result);
        Assert.Equal("Clicked", result.Description);
        Assert.Equal(0, result.PoleIndex);
        Assert.Equal(new[] { other, clicked }, chassis.Circuits);
    }

    [Fact]
    public void ConversionKeepsThePositionWithFixtures()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        var occupied = Record(4, "Motor", fixtures: true);
        Guid guid = occupied.CircuitGuid;
        List<CircuitedFixture> fixtures = occupied.Fixtures;
        chassis.Circuits.AddRange(new[] { Record(0, "Spare"), occupied });

        CircuitData result = CircuitLayout.ConvertToThreePhase(board, chassis, 0);

        Assert.Same(occupied, Assert.Single(chassis.Circuits));
        Assert.Equal(guid, result.CircuitGuid);
        Assert.Same(fixtures, result.Fixtures);
        Assert.Equal(0, result.PoleIndex);
        Assert.Equal(3, result.Poles);
    }

    [Fact]
    public void ConversionIsRejectedWithoutMutationWhenTwoPositionsHaveFixtures()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        var a = Record(0, "A", fixtures: true);
        var b = Record(2, "B", fixtures: true);
        chassis.Circuits.AddRange(new[] { a, b });

        Assert.NotNull(CircuitLayout.GetThreePhaseConversion(board, chassis, 4, out _));
        Assert.Throws<InvalidDataException>(() => CircuitLayout.ConvertToThreePhase(board, chassis, 4));
        Assert.Equal(new[] { a, b }, chassis.Circuits);
        Assert.Equal(1, a.Poles);
    }

    [Fact]
    public void ConversionIsRejectedOnSinglePhaseIncompleteOrAlreadyThreePhaseGroups()
    {
        var single = CircuitLayoutTests.Chassis(12);
        single.PhaseLabels.Clear();
        Assert.NotNull(CircuitLayout.GetThreePhaseConversion(CircuitLayoutTests.Board(single), single, 0, out _));

        var shortChassis = CircuitLayoutTests.Chassis(8);
        Assert.NotNull(CircuitLayout.GetThreePhaseConversion(
            CircuitLayoutTests.Board(shortChassis), shortChassis, 6, out _));

        var first = CircuitLayoutTests.Chassis(8);
        var continued = CircuitLayoutTests.Chassis(12);
        continued.ContinuePhaseFromPrevious = true;
        var continuedBoard = CircuitLayoutTests.Board(first, continued);
        Assert.NotNull(CircuitLayout.GetThreePhaseConversion(continuedBoard, continued, 0, out _));
        Assert.Null(CircuitLayout.GetThreePhaseConversion(continuedBoard, continued, 4, out _));

        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        CircuitLayout.ConvertToThreePhase(board, chassis, 0);
        Assert.NotNull(CircuitLayout.GetThreePhaseConversion(board, chassis, 2, out _));
    }

    [Fact]
    public void ConvertCommandIsOfferedOnThreePhaseChassisForSpacesAndSinglePoleCircuits()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        chassis.Circuits.Add(Record(0, "Lights", fixtures: true));
        var actions = new CircuitTreeProjectionTests.RecordingActions();

        var rows = CircuitTreeProjection.BuildRows(board, chassis, actions);

        Assert.True(rows[0].LeftCommands!.CanConvertToThreePhase);
        Assert.True(rows[2].RightCommands!.CanConvertToThreePhase);
        rows[1].RightCommands!.ConvertCommand.Execute(null);
        Assert.Equal(("convert-3ph", (object?)rows[1].Right), (actions.Action, actions.Target));

        CircuitLayout.ConvertToThreePhase(board, chassis, 0);
        rows = CircuitTreeProjection.BuildRows(board, chassis, actions);
        Assert.False(rows[1].LeftCommands!.CanConvertToThreePhase);

        chassis.PhaseLabels.Clear();
        rows = CircuitTreeProjection.BuildRows(board, chassis, actions);
        Assert.False(rows[3].RightCommands!.CanConvertToThreePhase);
    }
}

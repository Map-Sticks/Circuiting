using Circuiting.Models;
using Circuiting.Services;
using Circuiting.Storage;
using Circuiting.ViewModels;
using Xunit;

namespace Circuiting.Tests;

public class CircuitMoveTests
{
    [Fact]
    public void RepeatedLabelsDoNotPreventMovingToADifferentPhysicalPosition()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        chassis.ShowPhaseLabel = false;
        var board = CircuitLayoutTests.Board(chassis);
        var circuit = new CircuitData { CircuitGuid = Guid.NewGuid(), PoleIndex = 0 };
        chassis.Circuits.Add(circuit);
        CircuitLayout.ValidateAndUpdate(board, chassis);
        string label = circuit.DisplayLabel;

        Assert.True(CircuitLayout.Move(chassis, circuit, board, chassis, 2));

        Assert.Equal(label, circuit.DisplayLabel);
        Assert.Equal(2, circuit.PoleIndex);
    }

    [Fact]
    public void InvalidSourceOrDestinationMembershipIsRejected()
    {
        var source = CircuitLayoutTests.Chassis(12);
        var target = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(target);
        var circuit = new CircuitData { CircuitGuid = Guid.NewGuid(), PoleIndex = 0 };
        Assert.Throws<InvalidDataException>(() => CircuitLayout.Move(source, circuit, board, target, 1));
        source.Circuits.Add(circuit);
        Assert.Throws<InvalidDataException>(() =>
            CircuitLayout.Move(source, circuit, CircuitLayoutTests.Board(), target, 1));
        Assert.Same(circuit, Assert.Single(source.Circuits));
        Assert.Empty(target.Circuits);
    }

    [Fact]
    public void MovingAcrossBoardsPreservesCircuitAndFixtureIdentityAndDetails()
    {
        var source = CircuitLayoutTests.Chassis(12);
        var target = CircuitLayoutTests.Chassis(12);
        target.Prefix = "NEW";
        var targetBoard = CircuitLayoutTests.Board(target);
        var fixtures = new List<CircuitedFixture> { new() { FixtureGuid = Guid.NewGuid() } };
        var circuit = new CircuitData
        {
            CircuitGuid = Guid.NewGuid(), PoleIndex = 0, Description = "Lights",
            BreakerSize = "16A", ProtectionType = "RCBO", EarthLeakage = "30mA",
            Fixtures = fixtures
        };
        source.Circuits.Add(circuit);
        Guid guid = circuit.CircuitGuid;

        Assert.True(CircuitLayout.Move(source, circuit, targetBoard, target, 7));

        Assert.Empty(source.Circuits);
        Assert.Same(circuit, Assert.Single(target.Circuits));
        Assert.Equal(guid, circuit.CircuitGuid);
        Assert.Same(fixtures, circuit.Fixtures);
        Assert.Equal("Lights", circuit.Description);
        Assert.Equal("16A", circuit.BreakerSize);
        Assert.Equal("RCBO", circuit.ProtectionType);
        Assert.Equal("30mA", circuit.EarthLeakage);
        PoleAssignment position = CircuitLabelCalculator.Calculate(targetBoard, target)[7];
        Assert.Equal(7, circuit.PoleIndex);
        Assert.Equal(position.DisplayLabel, circuit.DisplayLabel);
        Assert.Equal(position.CircuitNumber, circuit.CircuitNumber);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void OccupiedStartAndConsumedPolesRejectMoveWithoutMutation(int position)
    {
        var source = CircuitLayoutTests.Chassis(12);
        var target = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(source, target);
        var circuit = new CircuitData { CircuitGuid = Guid.NewGuid(), PoleIndex = 0, DisplayLabel = "Original" };
        source.Circuits.Add(circuit);
        var occupied = new CircuitData { CircuitGuid = Guid.NewGuid(), PoleIndex = 1, Poles = 3, Fixtures = { new CircuitedFixture() } };
        target.Circuits.Add(occupied);

        Assert.Throws<InvalidDataException>(() => CircuitLayout.Move(source, circuit, board, target, position));

        Assert.Same(circuit, Assert.Single(source.Circuits));
        Assert.Same(occupied, Assert.Single(target.Circuits));
        Assert.Equal(0, circuit.PoleIndex);
        Assert.Equal("Original", circuit.DisplayLabel);
    }

    [Fact]
    public void MultipoleMoveRequiresAllDestinationPolesFree()
    {
        var source = CircuitLayoutTests.Chassis(12);
        var target = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(source, target);
        var circuit = new CircuitData { CircuitGuid = Guid.NewGuid(), PoleIndex = 0, Poles = 3 };
        source.Circuits.Add(circuit);
        target.Circuits.Add(new CircuitData { CircuitGuid = Guid.NewGuid(), PoleIndex = 5, Fixtures = { new CircuitedFixture() } });

        Assert.Throws<InvalidDataException>(() => CircuitLayout.Move(source, circuit, board, target, 1));
        Assert.Same(circuit, Assert.Single(source.Circuits));
    }

    [Theory]
    [InlineData(2, 12, false)]
    [InlineData(6, 8, false)]
    [InlineData(0, 12, true)]
    [InlineData(-1, 12, false)]
    [InlineData(12, 12, false)]
    public void InvalidPhaseCapacityAndBoundsAreRejected(int position, int maxPoles, bool singlePhase)
    {
        var source = CircuitLayoutTests.Chassis(12);
        var target = CircuitLayoutTests.Chassis(maxPoles);
        if (singlePhase) target.PhaseLabels.Clear();
        var circuit = new CircuitData { CircuitGuid = Guid.NewGuid(), PoleIndex = 0, Poles = 3 };
        source.Circuits.Add(circuit);

        Assert.Throws<InvalidDataException>(() =>
            CircuitLayout.Move(source, circuit, CircuitLayoutTests.Board(target), target, position));
        Assert.Equal(0, circuit.PoleIndex);
        Assert.Empty(target.Circuits);
    }

    [Fact]
    public void ContinuedPhaseIsUsedWhenChoosingMultipoleDestination()
    {
        var first = CircuitLayoutTests.Chassis(8);
        var target = CircuitLayoutTests.Chassis(12);
        target.ContinuePhaseFromPrevious = true;
        var board = CircuitLayoutTests.Board(first, target);
        var circuit = new CircuitData { CircuitGuid = Guid.NewGuid(), PoleIndex = 0, Poles = 2 };
        first.Circuits.Add(circuit);

        Assert.NotNull(CircuitLayout.GetMoveError(first, circuit, board, target, 0));
        Assert.True(CircuitLayout.Move(first, circuit, board, target, 4));
        Assert.Equal(4, circuit.PoleIndex);
    }

    [Fact]
    public void SameChassisMoveMayReuseItsOwnPolesAndSamePositionIsANoOp()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        var circuit = new CircuitData { CircuitGuid = Guid.NewGuid(), PoleIndex = 0, Poles = 3 };
        chassis.Circuits.Add(circuit);

        Assert.False(CircuitLayout.Move(chassis, circuit, board, chassis, 0));
        Assert.True(CircuitLayout.Move(chassis, circuit, board, chassis, 1));
        Assert.Same(circuit, Assert.Single(chassis.Circuits));
        Assert.Equal(new[] { 3, 5 }, CircuitLayout.GetConsumedPoles(chassis).Keys.OrderBy(i => i));
    }

    [Fact]
    public void MoveSnapshotsRoundTripThroughUndoAndRedo()
    {
        var source = CircuitLayoutTests.Chassis(12);
        var target = CircuitLayoutTests.Chassis(12);
        var sourceBoard = CircuitLayoutTests.Board(source);
        var targetBoard = CircuitLayoutTests.Board(target);
        var circuit = new CircuitData { CircuitGuid = Guid.NewGuid(), PoleIndex = 0 };
        source.Circuits.Add(circuit);
        var project = new ProjectData { Boards = new List<BoardData> { sourceBoard, targetBoard } };
        var history = new CircuitEditHistory();
        history.Push(ProjectDataSerializer.Serialize(project));
        CircuitLayout.Move(source, circuit, targetBoard, target, 5);

        var undone = ProjectDataSerializer.Deserialize(history.Undo(ProjectDataSerializer.Serialize(project)));
        Assert.Equal(circuit.CircuitGuid, Assert.Single(undone.Boards[0].Chassis[0].Circuits).CircuitGuid);
        Assert.Empty(undone.Boards[1].Chassis[0].Circuits);
        var redone = ProjectDataSerializer.Deserialize(history.Redo(ProjectDataSerializer.Serialize(undone)));
        Assert.Empty(redone.Boards[0].Chassis[0].Circuits);
        var moved = Assert.Single(redone.Boards[1].Chassis[0].Circuits);
        Assert.Equal(circuit.CircuitGuid, moved.CircuitGuid);
        Assert.Equal(5, moved.PoleIndex);
    }
}

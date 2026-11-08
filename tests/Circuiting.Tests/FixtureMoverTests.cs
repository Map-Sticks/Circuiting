using Circuiting.Models;
using Circuiting.Services;
using Circuiting.ViewModels;
using Xunit;

namespace Circuiting.Tests;

public class FixtureMoverTests
{
    private static CircuitedFixture Fixture(string name) =>
        new() { FixtureGuid = Guid.NewGuid(), RevitUniqueId = Guid.NewGuid().ToString(), FamilyName = name };

    private static CircuitData Circuit(int pole, params CircuitedFixture[] fixtures) =>
        new() { CircuitGuid = Guid.NewGuid(), PoleIndex = pole, Fixtures = fixtures.ToList() };

    [Fact]
    public void MovingOntoASpaceCreatesANewCircuitWithTheEnteredDetails()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        var light = Fixture("Downlight");
        var source = Circuit(0, light, Fixture("Panel"));
        chassis.Circuits.Add(source);
        CircuitLayout.ValidateAndUpdate(board, chassis);

        Assert.Equal(FixtureMoveKind.NewCircuit, FixtureMover.GetTarget(board, chassis, source, 5).Kind);
        CircuitData created = FixtureMover.Move(chassis, source, light, board, chassis, 5,
            new CircuitData { Description = "Corridor lights", BreakerSize = "10A", ProtectionType = "MCB", Poles = 3 });

        Assert.Contains(created, chassis.Circuits);
        Assert.Equal(5, created.PoleIndex);
        Assert.Equal(1, created.Poles);
        Assert.Equal("Corridor lights", created.Description);
        Assert.Equal("10A", created.BreakerSize);
        Assert.Equal(new[] { light }, created.Fixtures);
        Assert.Single(source.Fixtures);
        Assert.NotEqual(Guid.Empty, created.CircuitGuid);
        Assert.False(string.IsNullOrEmpty(created.DisplayLabel));
    }

    [Fact]
    public void MovingOntoAnOccupiedCircuitAddsTheElementAndAnEmptiedSourceBecomesASpace()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var other = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis, other);
        var light = Fixture("Downlight");
        var source = Circuit(0, light);
        var target = Circuit(3, Fixture("Panel"));
        chassis.Circuits.Add(source);
        other.Circuits.Add(target);

        Assert.Equal(FixtureMoveKind.AddToCircuit, FixtureMover.GetTarget(board, other, source, 3).Kind);
        Assert.Same(target, FixtureMover.Move(chassis, source, light, board, other, 3));

        Assert.Equal(2, target.Fixtures.Count);
        Assert.Empty(chassis.Circuits);
    }

    [Fact]
    public void MovingOntoASpareKeepsItsDetailsUnlessAmendedAndAnEmptiedSourceWithDetailsStaysSpare()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        var light = Fixture("Downlight");
        var source = Circuit(0, light);
        source.Description = "Office lights";
        var spare = Circuit(2);
        spare.Description = "Spare 16A";
        spare.BreakerSize = "16A";
        var emptyRecord = Circuit(4);
        chassis.Circuits.AddRange(new[] { source, spare, emptyRecord });

        Assert.Equal(FixtureMoveKind.FillSpare, FixtureMover.GetTarget(board, chassis, source, 2).Kind);
        Assert.Equal(FixtureMoveKind.NewCircuit, FixtureMover.GetTarget(board, chassis, source, 4).Kind);
        Assert.Same(spare, FixtureMover.Move(chassis, source, light, board, chassis, 2));

        Assert.Equal("Spare 16A", spare.Description);
        Assert.Equal("16A", spare.BreakerSize);
        Assert.True(CircuitLayout.IsOccupied(spare));
        Assert.Contains(source, chassis.Circuits);
        Assert.False(CircuitLayout.IsOccupied(source));
    }

    [Fact]
    public void ConsumedPolesResolveToTheirMultiPoleCircuitAndTheCurrentCircuitIsUnavailable()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        var pump = Fixture("Pump");
        var motor = new CircuitData { CircuitGuid = Guid.NewGuid(), PoleIndex = 0, Poles = 3, Fixtures = { pump } };
        var source = Circuit(1, Fixture("Socket"));
        chassis.Circuits.AddRange(new[] { motor, source });

        Dictionary<int, FixtureMoveTarget> targets = FixtureMover.GetTargets(board, chassis, motor);
        Assert.Equal(FixtureMoveKind.Current, targets[2].Kind);
        Assert.Equal(FixtureMoveKind.Current, targets[4].Kind);
        Assert.Throws<InvalidDataException>(() => FixtureMover.Move(chassis, motor, pump, board, chassis, 4));

        FixtureMoveTarget consumed = FixtureMover.GetTarget(board, chassis, source, 4);
        Assert.Equal(FixtureMoveKind.AddToCircuit, consumed.Kind);
        Assert.Same(motor, consumed.Circuit);
    }

    [Fact]
    public void PickerListsEveryOtherPositionAsAvailableForAnElement()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        var light = Fixture("Downlight");
        var source = Circuit(0, light);
        var spare = Circuit(1);
        spare.Description = "Spare";
        chassis.Circuits.AddRange(new[] { source, spare, Circuit(2, Fixture("Panel")) });
        CircuitLayout.ValidateAndUpdate(board, chassis);

        var picker = new CircuitMovePicker(new[] { board },
            new[] { new FixtureRef { Board = board, Chassis = chassis, Circuit = source, Fixture = light } });
        List<MovePositionOption> all = picker.PositionsFor(chassis, availableOnly: false);

        Assert.Equal("Move Element to Circuit", picker.Title);
        Assert.Equal(all.Count - 1, picker.TotalAvailable);
        Assert.True(all[0].IsCurrent);
        Assert.Equal("Spare → circuit", all[1].Status);
        Assert.True(all[1].CreatesCircuit);
        Assert.Equal("Add to circuit", all[2].Status);
        Assert.False(all[2].CreatesCircuit);
        Assert.Equal("New circuit", all[3].Status);
        Assert.Contains("creates a new circuit", all[3].Note);
    }

    [Fact]
    public void ElementNodesOfferMoveToCircuit()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        var light = Fixture("Downlight");
        chassis.Circuits.Add(Circuit(0, light));
        var actions = new CircuitTreeProjectionTests.RecordingActions();

        FixtureTreeNode node = CircuitTreeProjection.BuildRows(board, chassis, actions)[0].Fixtures.Single();
        node.MoveCommand!.Execute(null);

        Assert.Equal(("move-fixture", (object?)light), (actions.Action, actions.Target));
        Assert.Same(board, node.Board);
        Assert.Same(chassis, node.Chassis);
    }
}

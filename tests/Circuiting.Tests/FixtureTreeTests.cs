using Circuiting.Models;
using Circuiting.Services;
using Circuiting.ViewModels;
using Xunit;

namespace Circuiting.Tests;

public class FixtureTreeTests
{
    private static CircuitedFixture Fixture(string type, string room = "") =>
        new() { FixtureGuid = Guid.NewGuid(), RevitElementId = 100, FamilyName = type, RoomName = room };

    [Fact]
    public void RowsListCircuitedElementsOfBothSidesWithTheirMepSpace()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        var office = Fixture("600x600");
        var corridor = Fixture("Downlight");
        var socket = Fixture("Double");
        var left = new CircuitData { CircuitGuid = Guid.NewGuid(), PoleIndex = 0, Fixtures = { office, corridor } };
        var right = new CircuitData { CircuitGuid = Guid.NewGuid(), PoleIndex = 1, Fixtures = { socket } };
        chassis.Circuits.AddRange(new[] { left, right });
        CircuitLayout.ValidateAndUpdate(board, chassis);
        var actions = new CircuitTreeProjectionTests.RecordingActions();
        actions.Details[office.FixtureGuid] = new FixtureDetails
        {
            Found = true, ElementId = 5, FamilyName = "Panel", TypeName = "600x600", Mark = "L1",
            SpaceNumber = "1.02", SpaceName = "Office", LevelName = "Level 1", Category = "Lighting Fixtures"
        };
        actions.Details[corridor.FixtureGuid] = new FixtureDetails
        {
            Found = true, FamilyName = "Downlight", TypeName = "Downlight", SpaceNumber = "1.01", SpaceName = "Corridor"
        };

        var rows = CircuitTreeProjection.BuildRows(board, chassis, actions);

        Assert.True(rows[0].HasFixtures);
        Assert.False(rows[1].HasFixtures);
        Assert.Equal(new[] { "Downlight", "Panel : 600x600  [L1]", "Double" }, rows[0].Fixtures.Select(f => f.Name));
        Assert.Equal(new[] { "1.01 Corridor", "1.02 Office", "Space unknown" }, rows[0].Fixtures.Select(f => f.Space));
        Assert.Equal(new[] { Side.Left, Side.Left, Side.Right }, rows[0].Fixtures.Select(f => f.Side));
        Assert.Same(right, rows[0].Fixtures[2].Circuit);
        FixtureTreeNode panel = rows[0].Fixtures[1];
        Assert.Contains("MEP space: 1.02 Office", panel.ToolTip);
        Assert.Contains("Level: Level 1", panel.ToolTip);
        Assert.Contains("Element ID: 5", panel.ToolTip);

        panel.ZoomCommand!.Execute(null);
        Assert.Equal(("zoom-fixture", (object?)office), (actions.Action, actions.Target));
        panel.SelectCommand!.Execute(null);
        Assert.Equal(("select-fixture", (object?)office), (actions.Action, actions.Target));
    }

    [Fact]
    public void MissingElementsAndStoredRoomsAreShownWhenLiveDetailsAreUnavailable()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        var missing = Fixture("Old type");
        var stored = Fixture("Spot", "Kitchen");
        chassis.Circuits.Add(new CircuitData { CircuitGuid = Guid.NewGuid(), PoleIndex = 0, Fixtures = { missing, stored } });
        var actions = new CircuitTreeProjectionTests.RecordingActions();
        actions.Details[missing.FixtureGuid] = new FixtureDetails { Found = false };

        var fixtures = CircuitTreeProjection.BuildRows(board, chassis, actions)[0].Fixtures;

        FixtureTreeNode lost = fixtures.Single(f => f.Fixture == missing);
        Assert.True(lost.IsMissing);
        Assert.Contains("not found in model", lost.Name);
        Assert.Contains("Validate Model", lost.ToolTip);
        Assert.Equal("Room Kitchen", fixtures.Single(f => f.Fixture == stored).Space);
    }

    [Fact]
    public void MultiPoleElementsAreListedOnceAndRowsExpandFromCircuitGuids()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        var supply = new CircuitData
        {
            CircuitGuid = Guid.NewGuid(), PoleIndex = 0, Poles = 3, Fixtures = { Fixture("Pump") }
        };
        chassis.Circuits.Add(supply);

        var rows = CircuitTreeProjection.BuildRows(board, chassis, null, new HashSet<Guid> { supply.CircuitGuid });

        Assert.Single(rows.SelectMany(r => r.Fixtures));
        Assert.True(rows[0].IsExpanded);
        Assert.False(rows[1].IsExpanded);
        Assert.Null(rows[0].Fixtures[0].ZoomCommand);
        Assert.False(CircuitTreeProjection.BuildRows(board, chassis, null, new HashSet<Guid>())[0].IsExpanded);
    }
}

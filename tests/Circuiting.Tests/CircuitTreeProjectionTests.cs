using Circuiting.Models;
using Circuiting.Services;
using Circuiting.ViewModels;
using Xunit;

namespace Circuiting.Tests;

public class CircuitTreeProjectionTests
{
    [Fact]
    public void RepeatedLabelsDoNotAliasDifferentPhysicalRows()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        chassis.ShowPhaseLabel = false;
        var first = new CircuitData { CircuitGuid = Guid.NewGuid(), PoleIndex = 0 };
        var second = new CircuitData { CircuitGuid = Guid.NewGuid(), PoleIndex = 2 };
        chassis.Circuits.AddRange(new[] { first, second });
        var board = CircuitLayoutTests.Board(chassis);
        CircuitLayout.ValidateAndUpdate(board, chassis);

        var rows = CircuitTreeProjection.BuildRows(board, chassis);

        Assert.Equal(rows[0].Left.DisplayLabel, rows[1].Left.DisplayLabel);
        Assert.Same(first, rows[0].LeftCircuit);
        Assert.Same(second, rows[1].LeftCircuit);
    }

    [Fact]
    public void ProjectionMarksConsumedPolesOnTheCorrectSideAndParent()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var parent = new CircuitData { CircuitGuid = Guid.NewGuid(), PoleIndex = 1, Poles = 3 };
        chassis.Circuits.Add(parent);
        var board = CircuitLayoutTests.Board(chassis);
        CircuitLayout.ValidateAndUpdate(board, chassis);

        var rows = CircuitTreeProjection.BuildRows(board, chassis);

        Assert.Same(parent, rows[0].RightCircuit);
        Assert.Same(parent, rows[1].RightConsumedInfo?.Parent);
        Assert.True(rows[1].RightConsumedInfo?.IsDescriptionRow);
        Assert.False(rows[2].RightConsumedInfo?.IsDescriptionRow);
        Assert.False(rows[1].LeftConsumed);
    }

    [Fact]
    public void RowDisplayPreservesSpareSinglePoleAndConsumedPoleFormatting()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var single = new CircuitData
        {
            CircuitGuid = Guid.NewGuid(), PoleIndex = 0, Description = "Lights",
            BreakerSize = "16A", ProtectionType = "RCBO", EarthLeakage = "30mA",
            Fixtures = new List<CircuitedFixture> { new CircuitedFixture() }
        };
        var multi = new CircuitData
        {
            CircuitGuid = Guid.NewGuid(), PoleIndex = 1, Poles = 3, Description = "Supply",
            BreakerSize = "32A", ProtectionType = "MCB", EarthLeakage = "100mA"
        };
        chassis.Circuits.AddRange(new[] { single, multi });
        var board = CircuitLayoutTests.Board(chassis);
        CircuitLayout.ValidateAndUpdate(board, chassis);

        var rows = CircuitTreeProjection.BuildRows(board, chassis);

        Assert.Equal("Lights [1]", rows[0].LeftDisplay.Description);
        Assert.Equal("30mA RCBO", rows[0].LeftDisplay.Protection);
        Assert.Equal("16A", rows[0].LeftDisplay.Breaker);
        Assert.Equal(string.Empty, rows[0].RightDisplay.Description);
        Assert.Equal("Spare — Supply", rows[1].RightDisplay.Description);
        Assert.True(rows[1].RightDisplay.IsSpare);
        Assert.True(rows[0].RightDisplay.IsSpare);
        Assert.False(rows[0].LeftDisplay.IsSpare);
        Assert.Equal("MCB", rows[1].RightDisplay.Protection);
        Assert.Equal("32A", rows[1].RightDisplay.Breaker);
        Assert.Equal(string.Empty, rows[2].RightDisplay.Description);
        Assert.True(rows[1].LeftDisplay.IsSpare);
        Assert.Equal("Space", rows[1].LeftDisplay.Description);
    }

    [Fact]
    public void TreeCommandsTargetTheCorrectBoardChassisAndConsumedOrSparePole()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var circuit = new CircuitData
        {
            CircuitGuid = Guid.NewGuid(), PoleIndex = 1, Poles = 3,
            Fixtures = new List<CircuitedFixture> { new CircuitedFixture() }
        };
        chassis.Circuits.Add(circuit);
        var board = CircuitLayoutTests.Board(chassis);
        CircuitLayout.ValidateAndUpdate(board, chassis);
        var actions = new RecordingActions();

        var node = Assert.Single(CircuitTreeProjection.Build(
            new[] { board }, actions, new HashSet<Guid> { board.BoardGuid, chassis.ChassisGuid }));
        var chassisNode = Assert.Single(node.Chassis);
        var row = chassisNode.Rows[1];
        Assert.True(node.IsExpanded);
        Assert.True(chassisNode.IsExpanded);
        Assert.True(row.RightCommands!.HasCircuit);
        Assert.True(row.RightCommands.HasFixtures);
        Assert.False(row.LeftCommands!.HasCircuit);
        Assert.Null(row.LeftCommands.MoveCommand);

        node.RenameCommand.Execute(null);
        Assert.Equal(("rename", board), (actions.Action, actions.Target));
        node.EditCommand.Execute(null);
        Assert.Equal(("edit-board", board), (actions.Action, actions.Target));
        node.DuplicateCommand.Execute(null);
        Assert.Equal(("duplicate-board", board), (actions.Action, actions.Target));
        chassisNode.EditCommand.Execute(null);
        Assert.Equal(("edit-chassis", chassis), (actions.Action, actions.Target));
        row.RightCommands.DeleteCommand!.Execute(null);
        Assert.Equal(("delete-circuit", circuit), (actions.Action, actions.Target));
        row.RightCommands.MoveCommand!.Execute(null);
        Assert.Equal(("move-circuit", circuit), (actions.Action, actions.Target));
        chassisNode.Rows[0].RightCommands!.MoveCommand!.Execute(null);
        Assert.Equal(("move-circuit", circuit), (actions.Action, actions.Target));
        row.LeftCommands.AssignCommand.Execute(null);
        Assert.Equal(("assign-spare", row.Left), (actions.Action, actions.Target));
        row.EditCommand!.Execute(null);
        Assert.Equal(("edit-row", row), (actions.Action, actions.Target));
    }

    internal sealed class RecordingActions : ICircuitTreeActions
    {
        public string Action { get; private set; } = string.Empty;
        public object? Target { get; private set; }
        private void Record(string action, object target) { Action = action; Target = target; }
        public void AddChassis(BoardData board) => Record("add-chassis", board);
        public void RenameBoard(BoardData board) => Record("rename", board);
        public void DeleteBoard(BoardData board) => Record("delete-board", board);
        public void EditBoard(BoardData board) => Record("edit-board", board);
        public void DuplicateBoard(BoardData board) => Record("duplicate-board", board);
        public void EditChassis(BoardData board, ChassisData chassis) => Record("edit-chassis", chassis);
        public void BulkEditChassis(BoardData board, ChassisData chassis) => Record("bulk-edit", chassis);
        public void ClearChassis(ChassisData chassis) => Record("clear-chassis", chassis);
        public void DeleteChassis(BoardData board, ChassisData chassis) => Record("delete-chassis", chassis);
        public void EditRow(CircuitRowViewModel row) => Record("edit-row", row);
        public void DeleteCircuit(ChassisData chassis, CircuitData circuit) => Record("delete-circuit", circuit);
        public void MoveCircuit(BoardData board, ChassisData chassis, CircuitData circuit) => Record("move-circuit", circuit);
        public void ConvertToThreePhase(BoardData board, ChassisData chassis, PoleAssignment pole) =>
            Record("convert-3ph", pole);
        public void AssignFixtures(BoardData board, ChassisData chassis, CircuitData circuit) => Record("assign", circuit);
        public void AssignFixturesToSpare(BoardData board, ChassisData chassis, PoleAssignment pole) =>
            Record("assign-spare", pole);
        public void SelectFixtures(BoardData board, ChassisData chassis, CircuitData circuit) => Record("select", circuit);
        public void RemoveFixtures(BoardData board, ChassisData chassis, CircuitData circuit) => Record("remove", circuit);
        public void ShowFixture(BoardData board, ChassisData chassis, CircuitData circuit, CircuitedFixture fixture,
            bool zoom) => Record(zoom ? "zoom-fixture" : "select-fixture", fixture);
        public Dictionary<Guid, FixtureDetails> Details { get; } = new();
        public FixtureDetails? GetFixtureDetails(CircuitedFixture fixture) =>
            Details.TryGetValue(fixture.FixtureGuid, out FixtureDetails? details) ? details : null;
    }
}

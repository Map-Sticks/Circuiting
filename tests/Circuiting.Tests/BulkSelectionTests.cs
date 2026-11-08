using Circuiting.Models;
using Circuiting.Services;
using Circuiting.ViewModels;
using Xunit;

namespace Circuiting.Tests;

public class BulkSelectionTests
{
    private static CircuitedFixture Fixture(string name = "Light") =>
        new() { FixtureGuid = Guid.NewGuid(), RevitUniqueId = Guid.NewGuid().ToString(), FamilyName = name };

    private static CircuitData Circuit(int pole, params CircuitedFixture[] fixtures) =>
        new() { CircuitGuid = Guid.NewGuid(), PoleIndex = pole, Fixtures = fixtures.ToList() };

    private static CircuitRef Ref(BoardData board, ChassisData chassis, CircuitData circuit) =>
        new() { Board = board, Chassis = chassis, Circuit = circuit };

    private static TreeSelection NewSelection() => new(() => { }, () => { }, () => { });

    // ─── TreeSelection ───────────────────────────────────

    [Fact]
    public void PlainClickSelectsOnlyTheClickedItem()
    {
        var keys = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var selection = NewSelection();

        selection.Click(TreeSelectionKind.Circuits, keys[1], keys, ctrl: false, shift: false);
        selection.Click(TreeSelectionKind.Circuits, keys[3], keys, ctrl: false, shift: false);

        Assert.Equal(new[] { keys[3] }, selection.Keys);
        Assert.Equal(TreeSelectionKind.Circuits, selection.Kind);
        Assert.False(selection.HasMultipleCircuits);
    }

    [Fact]
    public void CtrlClickTogglesItems()
    {
        var keys = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var selection = NewSelection();

        selection.Click(TreeSelectionKind.Circuits, keys[0], keys, false, false);
        selection.Click(TreeSelectionKind.Circuits, keys[2], keys, ctrl: true, shift: false);
        selection.Click(TreeSelectionKind.Circuits, keys[4], keys, ctrl: true, shift: false);
        Assert.Equal(3, selection.Count);
        Assert.True(selection.HasMultipleCircuits);
        Assert.Contains("3", selection.MoveCircuitsLabel);

        selection.Click(TreeSelectionKind.Circuits, keys[2], keys, ctrl: true, shift: false);
        Assert.Equal(new HashSet<Guid> { keys[0], keys[4] }, selection.Keys.ToHashSet());

        selection.Click(TreeSelectionKind.Circuits, keys[0], keys, ctrl: true, shift: false);
        selection.Click(TreeSelectionKind.Circuits, keys[4], keys, ctrl: true, shift: false);
        Assert.Equal(0, selection.Count);
        Assert.Equal(TreeSelectionKind.None, selection.Kind);
    }

    [Fact]
    public void ShiftClickSelectsTheRangeFromTheAnchorAndCtrlShiftAddsIt()
    {
        var keys = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToList();
        var selection = NewSelection();

        selection.Click(TreeSelectionKind.Elements, keys[5], keys, false, false);
        selection.Click(TreeSelectionKind.Elements, keys[2], keys, ctrl: false, shift: true);
        Assert.Equal(keys.Skip(2).Take(4).ToHashSet(), selection.Keys.ToHashSet());
        Assert.True(selection.HasMultipleElements);

        // A plain Shift range replaces the previous range; the anchor stays put.
        selection.Click(TreeSelectionKind.Elements, keys[6], keys, ctrl: false, shift: true);
        Assert.Equal(new HashSet<Guid> { keys[5], keys[6] }, selection.Keys.ToHashSet());

        selection.Click(TreeSelectionKind.Elements, keys[0], keys, ctrl: true, shift: false);
        selection.Click(TreeSelectionKind.Elements, keys[1], keys, ctrl: true, shift: true);
        Assert.Equal(new HashSet<Guid> { keys[0], keys[1], keys[5], keys[6] }, selection.Keys.ToHashSet());
    }

    [Fact]
    public void ClickingTheOtherKindStartsANewSelectionAndPruneDropsMissingKeys()
    {
        var circuits = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        var elements = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        var selection = NewSelection();

        selection.Click(TreeSelectionKind.Circuits, circuits[0], circuits, false, false);
        selection.Click(TreeSelectionKind.Circuits, circuits[2], circuits, false, true);
        selection.Click(TreeSelectionKind.Elements, elements[1], elements, ctrl: true, shift: false);
        Assert.Equal(TreeSelectionKind.Elements, selection.Kind);
        Assert.Equal(new[] { elements[1] }, selection.Keys);
        Assert.False(selection.Contains(TreeSelectionKind.Circuits, circuits[0]));

        selection.Prune(key => key != elements[1]);
        Assert.Equal(0, selection.Count);
        Assert.Equal(TreeSelectionKind.None, selection.Kind);
    }

    [Fact]
    public void VisibleOrderFollowsTheTreeAndApplyToHighlightsRows()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        var a = Fixture("A");
        var b = Fixture("B");
        var c = Fixture("C");
        var left1 = Circuit(0, a, b);
        var right1 = Circuit(1, c);
        var left2 = Circuit(2, Fixture());
        chassis.Circuits.AddRange(new[] { left2, right1, left1 });
        CircuitLayout.ValidateAndUpdate(board, chassis);
        var expanded = new HashSet<Guid> { board.BoardGuid, chassis.ChassisGuid, left1.CircuitGuid };

        var tree = CircuitTreeProjection.Build(new[] { board }, new CircuitTreeProjectionTests.RecordingActions(), expanded);

        Assert.Equal(new[] { left1.CircuitGuid, right1.CircuitGuid, left2.CircuitGuid }, TreeSelection.CircuitOrder(tree));
        Assert.Equal(new[] { a.FixtureGuid, b.FixtureGuid, c.FixtureGuid }, TreeSelection.ElementOrder(tree));

        var selection = NewSelection();
        selection.Click(TreeSelectionKind.Circuits, right1.CircuitGuid, TreeSelection.CircuitOrder(tree), false, false);
        selection.ApplyTo(tree);
        CircuitRowViewModel row = tree[0].Chassis[0].Rows[0];
        Assert.False(row.IsLeftSelected);
        Assert.True(row.IsRightSelected);

        tree[0].Chassis[0].IsExpanded = false;
        Assert.Empty(TreeSelection.CircuitOrder(tree));
    }

    // ─── Bulk circuit move ───────────────────────────────

    [Fact]
    public void BulkMovePlacesTheFirstOnTheStartAndTheRestOnTheNextFreePositions()
    {
        var source = CircuitLayoutTests.Chassis(12);
        var target = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(source, target);
        var c1 = Circuit(0, Fixture());
        var c2 = Circuit(1, Fixture());
        var c3 = Circuit(2, Fixture());
        source.Circuits.AddRange(new[] { c1, c2, c3 });
        var blocker = Circuit(5, Fixture());
        target.Circuits.Add(blocker);
        CircuitLayout.ValidateAndUpdate(board, source);
        CircuitLayout.ValidateAndUpdate(board, target);
        var refs = BulkCircuitOperations.ResolveCircuits(new[] { board },
            new[] { c3.CircuitGuid, c1.CircuitGuid, c2.CircuitGuid });

        Assert.Equal(new[] { c1, c2, c3 }, refs.Select(r => r.Circuit));
        BulkMovePlan plan = BulkCircuitOperations.PlanMove(refs, board, target, 4);
        Assert.True(plan.IsValid, plan.Error);
        Assert.Equal(new[] { 4, 6, 7 }, plan.Placements.Select(p => p.Position.PoleIndex));

        BulkCircuitOperations.ApplyMove(plan, board, target);
        Assert.Empty(source.Circuits);
        Assert.Equal(4, target.Circuits.Count);
        Assert.Equal(new[] { 4, 6, 7 }, new[] { c1, c2, c3 }.Select(c => c.PoleIndex!.Value));
    }

    [Fact]
    public void BulkMoveRejectsAnOccupiedStartOrTooLittleRoom()
    {
        var chassis = CircuitLayoutTests.Chassis(6);
        var board = CircuitLayoutTests.Board(chassis);
        var c1 = Circuit(0, Fixture());
        var c2 = Circuit(1, Fixture());
        var occupied = Circuit(3, Fixture());
        chassis.Circuits.AddRange(new[] { c1, c2, occupied });
        CircuitLayout.ValidateAndUpdate(board, chassis);
        var refs = new List<CircuitRef> { Ref(board, chassis, c1), Ref(board, chassis, c2) };

        Dictionary<int, BulkMovePlan> plans = BulkCircuitOperations.PlanMoves(refs, board, chassis);

        Assert.False(plans[3].IsValid);
        Assert.False(plans[5].IsValid);
        Assert.Contains("Only 1 of 2", plans[5].Error);
        Assert.True(plans[0].IsValid);
        Assert.True(plans[0].IsNoOp);
        Assert.True(plans[2].IsValid);
        Assert.False(plans[2].IsNoOp);
        // Positions held by the moving circuits themselves are usable.
        Assert.Equal(new[] { 1, 2 }, plans[1].Placements.Select(p => p.Position.PoleIndex));
    }

    [Fact]
    public void BulkMoveReplacesSparesAndSkipsPositionsAThreePhaseCircuitCannotStartOn()
    {
        var source = CircuitLayoutTests.Chassis(12);
        var target = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(source, target);
        var single = Circuit(0, Fixture());
        var threePhase = Circuit(1, Fixture());
        threePhase.Poles = 3;
        source.Circuits.AddRange(new[] { single, threePhase });
        var spare = Circuit(0);
        spare.Description = "Spare 20A";
        target.Circuits.Add(spare);
        CircuitLayout.ValidateAndUpdate(board, source);
        CircuitLayout.ValidateAndUpdate(board, target);
        var refs = new List<CircuitRef> { Ref(board, source, single), Ref(board, source, threePhase) };

        BulkMovePlan plan = BulkCircuitOperations.PlanMove(refs, board, target, 0);

        Assert.True(plan.IsValid, plan.Error);
        Assert.Equal(new[] { spare }, plan.ReplacedSpares);
        int threePhaseStart = plan.Placements[1].Position.PoleIndex;
        Assert.True(threePhaseStart > 0);
        Assert.True(CircuitLayout.IsValidMultiPoleStart(plan.Placements[1].Position, target, 3,
            CircuitLabelCalculator.Calculate(board, target)));

        BulkCircuitOperations.ApplyMove(plan, board, target);
        Assert.DoesNotContain(spare, target.Circuits);
        Assert.Equal(threePhaseStart, threePhase.PoleIndex);
    }

    // ─── Bulk delete / unassign ──────────────────────────

    [Fact]
    public void DeleteCircuitsRemovesThemAndCountsUnassignedElements()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        var c1 = Circuit(0, Fixture(), Fixture());
        var c2 = Circuit(1);
        c2.Description = "Spare";
        var keep = Circuit(2, Fixture());
        chassis.Circuits.AddRange(new[] { c1, c2, keep });

        int unassigned = BulkCircuitOperations.DeleteCircuits(new[] { Ref(board, chassis, c1), Ref(board, chassis, c2) });

        Assert.Equal(2, unassigned);
        Assert.Equal(new[] { keep }, chassis.Circuits);
    }

    [Fact]
    public void UnassignFixturesLeavesSparesWithDetailsAndRemovesBareCircuits()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        var a = Fixture();
        var b = Fixture();
        var c = Fixture();
        var withDetails = Circuit(0, a);
        withDetails.Description = "Kitchen";
        var bare = Circuit(1, b);
        var partial = Circuit(2, c, Fixture());
        chassis.Circuits.AddRange(new[] { withDetails, bare, partial });

        var refs = BulkCircuitOperations.ResolveFixtures(new[] { board },
            new[] { a.FixtureGuid, b.FixtureGuid, c.FixtureGuid });
        Assert.Equal(3, BulkCircuitOperations.UnassignFixtures(refs));

        Assert.Equal(new[] { withDetails, partial }, chassis.Circuits);
        Assert.Empty(withDetails.Fixtures);
        Assert.Single(partial.Fixtures);
    }

    // ─── Bulk element move ───────────────────────────────

    [Fact]
    public void MoveManyMovesElementsFromSeveralCircuitsOntoANewCircuit()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var other = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis, other);
        var a = Fixture();
        var b = Fixture();
        var source1 = Circuit(0, a);
        var source2 = Circuit(1, b, Fixture());
        chassis.Circuits.AddRange(new[] { source1, source2 });
        CircuitLayout.ValidateAndUpdate(board, chassis);
        var refs = BulkCircuitOperations.ResolveFixtures(new[] { board }, new[] { a.FixtureGuid, b.FixtureGuid });

        Assert.True(FixtureMover.GetTarget(board, other, null, 4).CreatesCircuit);
        CircuitData created = FixtureMover.MoveMany(refs, board, other, 4, new CircuitData { Description = "New" });

        Assert.Equal(new[] { a, b }, created.Fixtures);
        Assert.Equal("New", created.Description);
        Assert.Equal(new[] { created }, other.Circuits);
        Assert.Equal(new[] { source2 }, chassis.Circuits);
        Assert.Single(source2.Fixtures);
    }

    [Fact]
    public void MoveManySkipsElementsAlreadyOnTheTargetAndRejectsANoOp()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        var a = Fixture();
        var b = Fixture();
        var target = Circuit(0, a);
        var source = Circuit(1, b);
        chassis.Circuits.AddRange(new[] { target, source });
        CircuitLayout.ValidateAndUpdate(board, chassis);
        var refs = BulkCircuitOperations.ResolveFixtures(new[] { board }, new[] { a.FixtureGuid, b.FixtureGuid });

        Assert.Same(target, FixtureMover.MoveMany(refs, board, chassis, 0, null));
        Assert.Equal(new[] { a, b }, target.Fixtures);
        Assert.Equal(new[] { target }, chassis.Circuits);

        var again = BulkCircuitOperations.ResolveFixtures(new[] { board }, new[] { a.FixtureGuid, b.FixtureGuid });
        Assert.ThrowsAny<Exception>(() => FixtureMover.MoveMany(again, board, chassis, 0, null));
    }

    // ─── Bulk edit ───────────────────────────────────────

    [Fact]
    public void BulkEditSetsOnlyTheTickedPropertiesAndCountsChangedCircuits()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        var occupied = Circuit(0, Fixture());
        occupied.Description = "Lights";
        occupied.BreakerSize = "10A";
        occupied.EarthLeakage = "30mA";
        var spare = Circuit(1);
        spare.Description = "Spare";
        var already = Circuit(2, Fixture());
        already.BreakerSize = "16A";
        already.ProtectionType = "RCBO";
        chassis.Circuits.AddRange(new[] { occupied, spare, already });
        var refs = new[] { occupied, spare, already }.Select(c => Ref(board, chassis, c)).ToList();

        var edit = new BulkCircuitEdit { BreakerSize = "16A", ProtectionType = "RCBO", EarthLeakage = string.Empty };
        Assert.True(edit.HasChanges);
        Assert.Equal(2, BulkCircuitOperations.ApplyEdit(refs, edit));

        Assert.All(refs, r => Assert.Equal("16A", r.Circuit.BreakerSize));
        Assert.All(refs, r => Assert.Equal("RCBO", r.Circuit.ProtectionType));
        Assert.Equal(string.Empty, occupied.EarthLeakage);
        Assert.Equal("Lights", occupied.Description);
        Assert.Equal("Spare", spare.Description);
        Assert.Single(occupied.Fixtures);
        Assert.False(new BulkCircuitEdit().HasChanges);
    }

    [Fact]
    public void BulkEditSetsTripCurveAndTripSettingAndCanClearThem()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        var a = Circuit(0, Fixture());
        a.TripCurve = "B";
        a.TripSetting = "Ir 0.8×In";
        var b = Circuit(1, Fixture());
        b.BreakerSize = "32A";
        chassis.Circuits.AddRange(new[] { a, b });
        var refs = new[] { a, b }.Select(c => Ref(board, chassis, c)).ToList();

        var curveOnly = new BulkCircuitEdit { TripCurve = "C" };
        Assert.True(curveOnly.HasChanges);
        Assert.Equal(2, BulkCircuitOperations.ApplyEdit(refs, curveOnly));
        Assert.All(refs, r => Assert.Equal("C", r.Circuit.TripCurve));
        Assert.Equal("Ir 0.8×In", a.TripSetting);
        Assert.Equal("32A", b.BreakerSize);
        Assert.False(curveOnly.WouldChange(a));

        var setting = new BulkCircuitEdit { TripSetting = "Ir 1.0×In, Isd 6×Ir" };
        Assert.Equal(2, BulkCircuitOperations.ApplyEdit(refs, setting));
        Assert.All(refs, r => Assert.Equal("Ir 1.0×In, Isd 6×Ir", r.Circuit.TripSetting));

        var clear = new BulkCircuitEdit { TripCurve = string.Empty, TripSetting = string.Empty };
        Assert.Equal(2, BulkCircuitOperations.ApplyEdit(refs, clear));
        Assert.All(refs, r => Assert.Equal(string.Empty, r.Circuit.TripCurve + r.Circuit.TripSetting));
    }

    [Fact]
    public void TripDetailsKeepASpareAndShowWithTheBreaker()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        var spare = Circuit(0);
        Assert.False(CircuitLayout.HasDetails(spare));
        spare.TripCurve = "D";
        Assert.True(CircuitLayout.HasDetails(spare));
        Assert.Equal("Type D", CircuitLayout.BreakerText(spare));
        spare.BreakerSize = "20A";
        Assert.Equal("D20A", CircuitLayout.BreakerText(spare));
        spare.TripCurve = string.Empty;
        spare.TripSetting = "Ir 0.9×In";
        Assert.True(CircuitLayout.HasDetails(spare));
        Assert.Equal("20A", CircuitLayout.BreakerText(spare));

        spare.TripCurve = "C";
        chassis.Circuits.Add(spare);
        CircuitLayout.ValidateAndUpdate(board, chassis);
        Assert.Contains("C20A", CircuitLayout.SpareSummary(spare));
        Assert.Contains("Ir 0.9×In", CircuitLayout.SpareSummary(spare));

        var tree = CircuitTreeProjection.Build(new[] { board }, new CircuitTreeProjectionTests.RecordingActions(),
            new HashSet<Guid> { board.BoardGuid, chassis.ChassisGuid });
        CircuitSideViewModel side = tree[0].Chassis[0].Rows[0].LeftDisplay;
        Assert.Equal("C20A", side.Breaker);
        Assert.Equal("Trip curve: C\nTrip setting: Ir 0.9×In", side.TripToolTip);
        Assert.Null(tree[0].Chassis[0].Rows[0].RightDisplay.TripToolTip);

        Assert.True(new CircuitSearch("C20A", CircuitSearchScope.Circuits).MatchesCircuit(spare));
        Assert.False(new CircuitSearch("Isd", CircuitSearchScope.Circuits).MatchesCircuit(spare));
        Assert.True(new CircuitSearch("0.9", CircuitSearchScope.Circuits).MatchesCircuit(spare));
    }

    [Fact]
    public void MovingAnElementOntoASpaceCopiesTheTripDetails()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        var board = CircuitLayoutTests.Board(chassis);
        var light = Fixture();
        var source = Circuit(0, light, Fixture());
        chassis.Circuits.Add(source);
        CircuitLayout.ValidateAndUpdate(board, chassis);

        CircuitData created = FixtureMover.Move(chassis, source, light, board, chassis, 4,
            new CircuitData { BreakerSize = "16A", TripCurve = "B", TripSetting = "fixed" });

        Assert.Equal("B", created.TripCurve);
        Assert.Equal("fixed", created.TripSetting);
    }

    [Fact]
    public void CommonValueIsSharedValueOrNull()
    {
        var a = new CircuitData { BreakerSize = "20A", ProtectionType = "MCB" };
        var b = new CircuitData { BreakerSize = "20A", ProtectionType = "RCBO" };

        Assert.Equal("20A", BulkCircuitEdit.Common(new[] { a, b }, c => c.BreakerSize));
        Assert.Null(BulkCircuitEdit.Common(new[] { a, b }, c => c.ProtectionType));
        Assert.Equal(string.Empty, BulkCircuitEdit.Common(new[] { a, b }, c => c.EarthLeakage));
    }

    // ─── Board selection ─────────────────────────────────

    [Fact]
    public void BoardsCanBeRangeSelectedHighlightedAndRemoved()
    {
        var boards = Enumerable.Range(0, 4).Select(_ => CircuitLayoutTests.Board(CircuitLayoutTests.Chassis(6))).ToList();
        boards[1].Chassis[0].Circuits.Add(Circuit(0, Fixture(), Fixture()));
        var tree = CircuitTreeProjection.Build(boards, new CircuitTreeProjectionTests.RecordingActions(),
            new HashSet<Guid>());
        var selection = NewSelection();
        List<Guid> order = TreeSelection.BoardOrder(tree);

        selection.Click(TreeSelectionKind.Boards, order[1], order, false, false);
        selection.Click(TreeSelectionKind.Boards, order[2], order, ctrl: false, shift: true);
        selection.ApplyTo(tree);

        Assert.True(selection.HasBoards);
        Assert.True(selection.HasMultipleBoards);
        Assert.False(selection.HasMultipleCircuits);
        Assert.Equal(new[] { false, true, true, false }, tree.Select(n => n.IsSelected));
        Assert.Contains("2 boards", selection.Summary);
        Assert.Equal(2, BulkCircuitOperations.CountFixtures(boards[1]));

        List<BoardData> removed = BulkCircuitOperations.RemoveBoards(boards, selection.Keys.ToList());
        Assert.Equal(2, removed.Count);
        Assert.Equal(2, boards.Count);
        Assert.DoesNotContain(boards, b => selection.Contains(b.BoardGuid));
    }

    // ─── Bulk picker ─────────────────────────────────────

    [Fact]
    public void BulkCircuitPickerMarksPositionsWhereAllSelectedCircuitsFit()
    {
        var chassis = CircuitLayoutTests.Chassis(6);
        var board = CircuitLayoutTests.Board(chassis);
        var c1 = Circuit(0, Fixture());
        var c2 = Circuit(1, Fixture());
        chassis.Circuits.AddRange(new[] { c1, c2 });
        CircuitLayout.ValidateAndUpdate(board, chassis);
        var refs = new List<CircuitRef> { Ref(board, chassis, c1), Ref(board, chassis, c2) };

        var picker = new CircuitMovePicker(new[] { board }.ToList(), refs);

        Assert.True(picker.IsBulkCircuitMove);
        Assert.Equal("Move Circuits", picker.Title);
        List<MovePositionOption> positions = picker.PositionsFor(chassis, availableOnly: false);
        Assert.True(positions.Single(p => p.Position.PoleIndex == 0).IsCurrent);
        Assert.False(positions.Single(p => p.Position.PoleIndex == 5).IsAvailable);
        MovePositionOption fits = positions.Single(p => p.Position.PoleIndex == 2);
        Assert.True(fits.IsAvailable);
        Assert.Contains("2 circuits", fits.Note);
    }
}

using Circuiting.Models;
using Circuiting.Services;
using Circuiting.ViewModels;
using Xunit;

namespace Circuiting.Tests;

public class CircuitMovePickerTests
{
    private static CircuitData Record(int pole, string description = "", bool fixtures = false, int poles = 1) => new()
    {
        CircuitGuid = Guid.NewGuid(), PoleIndex = pole, Description = description, Poles = poles,
        Fixtures = fixtures ? new List<CircuitedFixture> { new() { FixtureGuid = Guid.NewGuid() } } : new()
    };

    private static (BoardData Source, BoardData Other, ChassisData Main, ChassisData Sub, CircuitData Circuit) Project()
    {
        var main = CircuitLayoutTests.Chassis(12);
        var sub = CircuitLayoutTests.Chassis(6);
        sub.Name = "Lighting";
        var source = CircuitLayoutTests.Board(main);
        source.DisplayName = "DB-L1";
        var other = CircuitLayoutTests.Board(sub);
        other.DisplayName = "DB-MECH";
        var circuit = Record(0, "Kitchen", fixtures: true);
        main.Circuits.AddRange(new[] { circuit, Record(1, "Lights", fixtures: true), Record(2, "Old"), Record(3) });
        sub.Circuits.Add(Record(0, "Pump", fixtures: true, poles: 3));
        sub.Circuits.Add(Record(5, "Fan", fixtures: true));
        sub.Circuits.Add(Record(3, "Heater", fixtures: true));
        CircuitLayout.ValidateAndUpdate(source, main);
        CircuitLayout.ValidateAndUpdate(other, sub);
        return (source, other, main, sub, circuit);
    }

    [Fact]
    public void BoardsChassisAndPositionsAreListedWithAvailability()
    {
        var p = Project();
        var picker = new CircuitMovePicker(new[] { p.Source, p.Other }, p.Source, p.Main, p.Circuit);

        Assert.Equal(new[] { 10, 1 }, picker.Boards.Select(b => b.Available));
        Assert.True(picker.Boards[0].IsSource);
        Assert.Equal(11, picker.TotalAvailable);
        MoveChassisOption sub = Assert.Single(picker.ChassisFor(p.Other));
        Assert.Equal(1, sub.Available);
        Assert.Contains("1 available", sub.Header);

        List<MovePositionOption> all = picker.PositionsFor(p.Main, availableOnly: false);
        Assert.Equal(12, all.Count);
        Assert.True(all[0].IsCurrent);
        Assert.False(all[0].IsAvailable);
        Assert.Equal("(this circuit)", all[0].Contents);
        Assert.Equal("Unavailable", all[1].Status);
        Assert.Equal("Lights [1]", all[1].Contents);
        Assert.Equal("Spare — Old", all[2].Contents);
        Assert.True(all[2].ReplacesSpare);
        Assert.Equal("Replaces spare", all[2].Status);
        Assert.Equal("Space", all[3].Contents);
        Assert.Equal("Available", all[3].Status);
        Assert.Equal("Left", all[2].Side);
        Assert.Equal(2, all[2].Row);

        List<MovePositionOption> subPositions = picker.PositionsFor(p.Sub, availableOnly: false);
        Assert.Equal("(pole of " + p.Sub.Circuits[0].DisplayLabel + ")", subPositions[2].Contents);
        MovePositionOption free = Assert.Single(picker.PositionsFor(p.Sub, availableOnly: true));
        Assert.Equal(1, free.Position.PoleIndex);
        Assert.Equal($"DB-MECH / Lighting / {free.Label} (Right, row 1)", free.Summary);
    }

    [Fact]
    public void BoardAndPositionFiltersMatchEveryWord()
    {
        var p = Project();
        var picker = new CircuitMovePicker(new[] { p.Source, p.Other }, p.Source, p.Main, p.Circuit);

        Assert.Equal(new[] { p.Other }, picker.FilterBoards("mech").Select(b => b.Board));
        Assert.Equal(new[] { p.Other }, picker.FilterBoards("lighting").Select(b => b.Board));
        Assert.Empty(picker.FilterBoards("db mains"));
        Assert.Equal(2, picker.FilterBoards("  ").Count);

        Assert.Equal(new[] { 2 }, picker.PositionsFor(p.Main, true, "spare old").Select(o => o.Position.PoleIndex));
        Assert.Equal(new[] { 1, 3, 5, 7, 9, 11 },
            picker.PositionsFor(p.Main, false, "right").Select(o => o.Position.PoleIndex));
        int number = p.Main.Circuits[3].CircuitNumber;
        Assert.Contains(picker.PositionsFor(p.Main, true, number.ToString()), o => o.Position.PoleIndex == 3);
    }

    [Fact]
    public void BatchValidationMatchesSinglePositionValidation()
    {
        var p = Project();
        var mover = Record(4, "Supply", fixtures: true, poles: 3);
        p.Main.Circuits.Remove(p.Main.Circuits.Single(c => c.PoleIndex == 4 || c.PoleIndex == 3));
        p.Main.Circuits.Add(mover);
        foreach (var (board, chassis) in new[] { (p.Source, p.Main), (p.Other, p.Sub) })
        {
            Assert.Null(CircuitLayout.GetMoveErrors(p.Main, mover, board, chassis, out var errors));
            foreach (PoleAssignment position in CircuitLabelCalculator.Calculate(board, chassis))
                Assert.Equal(CircuitLayout.GetMoveError(p.Main, mover, board, chassis, position.PoleIndex),
                    errors[position.PoleIndex]);
        }
        Assert.NotNull(CircuitLayout.GetMoveErrors(p.Sub, mover, p.Other, p.Sub, out _));
    }
}

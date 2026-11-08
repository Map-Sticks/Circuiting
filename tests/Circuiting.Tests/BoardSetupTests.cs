using Circuiting.Models;
using Circuiting.Services;
using Xunit;

namespace Circuiting.Tests;

public class BoardSetupTests
{
    private static BoardData SourceBoard()
    {
        var main = CircuitLayoutTests.Chassis(12);
        main.Prefix = "L";
        main.Separator = "-";
        main.Circuits.Add(new CircuitData
        {
            CircuitGuid = Guid.NewGuid(), PoleIndex = 0, Description = "Lights",
            Fixtures = new List<CircuitedFixture> { new() { FixtureGuid = Guid.NewGuid() } }
        });
        var second = CircuitLayoutTests.Chassis(6);
        second.Name = "Ext";
        second.StartingCircuit = 7;
        second.ContinuePhaseFromPrevious = true;
        var board = CircuitLayoutTests.Board(main, second);
        board.DisplayName = "DB-L1";
        return board;
    }

    [Fact]
    public void CopySetupCopiesChassisSettingsWithNewIdentitiesAndNoCircuits()
    {
        var source = SourceBoard();

        var copy = BoardSetup.CopySetup(source, "DB-L2");

        Assert.Equal("DB-L2", copy.DisplayName);
        Assert.Equal(source.Supply, copy.Supply);
        Assert.NotEqual(source.BoardGuid, copy.BoardGuid);
        Assert.Equal(2, copy.Chassis.Count);
        for (int i = 0; i < 2; i++)
        {
            ChassisData original = source.Chassis[i], duplicate = copy.Chassis[i];
            Assert.NotEqual(original.ChassisGuid, duplicate.ChassisGuid);
            Assert.Empty(duplicate.Circuits);
            Assert.Equal((original.Name, original.MaxPoles, original.StartingCircuit, original.Prefix,
                    original.Separator, original.ShowPhaseLabel, original.ContinuePhaseFromPrevious),
                (duplicate.Name, duplicate.MaxPoles, duplicate.StartingCircuit, duplicate.Prefix,
                    duplicate.Separator, duplicate.ShowPhaseLabel, duplicate.ContinuePhaseFromPrevious));
            Assert.Equal(original.PhaseLabels, duplicate.PhaseLabels);
            Assert.NotSame(original.PhaseLabels, duplicate.PhaseLabels);
        }
        Assert.Single(source.Chassis[0].Circuits);
        Assert.Equal(
            CircuitLabelCalculator.Calculate(source, source.Chassis[1]).Select(a => a.DisplayLabel),
            CircuitLabelCalculator.Calculate(copy, copy.Chassis[1]).Select(a => a.DisplayLabel));
    }

    [Fact]
    public void ResizeAddsChassisThatContinueNumberingAndRemovesFromTheEnd()
    {
        var chassis = new List<ChassisData>();
        BoardSetup.Resize(chassis, 1, threePhase: true);
        chassis[0].MaxPoles = 12;
        chassis[0].Prefix = "P";

        BoardSetup.Resize(chassis, 3, threePhase: true);

        Assert.Equal(new[] { "Main", "Chassis 2", "Chassis 3" }, chassis.Select(c => c.Name));
        Assert.All(chassis, c => Assert.Equal((12, "P"), (c.MaxPoles, c.Prefix)));
        Assert.Equal(3, chassis.Select(c => c.ChassisGuid).Distinct().Count());
        var board = new BoardData { Chassis = chassis };
        int lastOfFirst = CircuitLabelCalculator.Calculate(board, chassis[0]).Max(a => a.CircuitNumber);
        Assert.Equal(lastOfFirst + 1, chassis[1].StartingCircuit);
        Assert.True(chassis[2].StartingCircuit > chassis[1].StartingCircuit);

        BoardSetup.Resize(chassis, 1, threePhase: true);
        Assert.Equal("Main", Assert.Single(chassis).Name);
        Assert.Throws<ArgumentOutOfRangeException>(() => BoardSetup.Resize(chassis, 0, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => BoardSetup.Resize(chassis, BoardSetup.MaxChassis + 1, true));
    }

    [Fact]
    public void ApplyToAllSetsSharedSettingsAndRenumbersConsecutively()
    {
        var chassis = new List<ChassisData>();
        BoardSetup.Resize(chassis, 3, threePhase: false);

        BoardSetup.ApplyToAll(chassis, 6, "C", ".");

        Assert.All(chassis, c => Assert.Equal((6, "C", "."), (c.MaxPoles, c.Prefix, c.Separator)));
        Assert.Equal(new[] { 1, 7, 13 }, chassis.Select(c => c.StartingCircuit));
    }

    [Fact]
    public void ApplySupplyConvertsPhaseSettingsButKeepsLegacyLabels()
    {
        var three = BoardSetup.DefaultChassis(threePhase: true);
        three.ContinuePhaseFromPrevious = true;
        var legacy = BoardSetup.DefaultChassis(threePhase: false);
        legacy.PhaseLabels = new List<string> { "L1" };

        BoardSetup.ApplySupply(new[] { three, legacy }, threePhase: false);
        Assert.Empty(three.PhaseLabels);
        Assert.False(three.ShowPhaseLabel);
        Assert.False(three.ContinuePhaseFromPrevious);
        Assert.Equal(new[] { "L1" }, legacy.PhaseLabels);

        BoardSetup.ApplySupply(new[] { three }, threePhase: true);
        Assert.Equal(new[] { "R", "W", "B" }, three.PhaseLabels);
        Assert.True(three.ShowPhaseLabel);
    }

    [Theory]
    [InlineData("DB-L1", new[] { "DB-L1", "DB-L4", "Other" }, "DB-L", "5")]
    [InlineData("DB-GF", new[] { "DB-GF" }, "DB-GF-", "1")]
    [InlineData("DB-01", new[] { "DB-01", "db-02" }, "DB-", "03")]
    [InlineData("  ", new string[0], "DB-", "1")]
    public void SuggestNumberingContinuesAfterTheHighestUsedNumber(
        string source, string[] existing, string prefix, string start)
    {
        Assert.Equal((prefix, start), BoardSetup.SuggestNumbering(source, existing));
    }

    [Fact]
    public void UniqueNamesSkipExistingNamesCaseInsensitivelyAndKeepPadding()
    {
        var names = BoardSetup.UniqueNames("DB-", "01", 4, new[] { "db-02", " DB-03 " });

        Assert.Equal(new[] { "DB-01", "DB-04", "DB-05", "DB-06" }, names);
        Assert.Equal(new[] { "X9", "X10" }, BoardSetup.UniqueNames("X", "9", 2, Array.Empty<string>()));
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("1a", 1)]
    [InlineData("-1", 1)]
    [InlineData("1", 0)]
    [InlineData("1", BoardSetup.MaxCopies + 1)]
    public void UniqueNamesRejectInvalidInput(string start, int count)
    {
        Assert.ThrowsAny<ArgumentException>(() => BoardSetup.UniqueNames("DB-", start, count, Array.Empty<string>()));
    }

    [Fact]
    public void NameExistsIgnoresCaseAndSurroundingWhitespace()
    {
        Assert.True(BoardSetup.NameExists(new[] { " DB-L1 " }, "db-l1"));
        Assert.False(BoardSetup.NameExists(new[] { "DB-L1" }, "DB-L10"));
    }
}

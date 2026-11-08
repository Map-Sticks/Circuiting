using Circuiting.Models;
using Circuiting.Services;
using Circuiting.Storage;
using Circuiting.ViewModels;
using Newtonsoft.Json;
using Xunit;

namespace Circuiting.Tests;

public class ChassisFieldOptionsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void EditingALegacySinglePhaseChassisPreservesItsPhasePattern(int count)
    {
        var existing = new[] { "R", "W" }.Take(count).ToArray();

        var result = ChassisFieldOptions.PhaseLabels(true, string.Empty, existing, false);

        Assert.Equal(existing, result);
        Assert.NotSame(existing, result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void MigratedLegacyPatternsSurviveAnUnrelatedChassisEdit(int count)
    {
        var chassis = CircuitLayoutTests.Chassis(6);
        chassis.PhaseLabels = new[] { "R", "W" }.Take(count).ToList();
        string originalLabel = CircuitLabelCalculator.Calculate(chassis)[2].DisplayLabel;
        chassis.Circuits.Add(new CircuitData { CircuitGuid = Guid.NewGuid(), DisplayLabel = originalLabel });
        string legacyJson = JsonConvert.SerializeObject(new[] { CircuitLayoutTests.Board(chassis) });
        var project = ProjectDataSerializer.Deserialize(legacyJson);
        var migrated = project.Boards[0].Chassis[0];

        migrated.PhaseLabels = ChassisFieldOptions.PhaseLabels(
            true, string.Join(",", migrated.PhaseLabels), migrated.PhaseLabels, false);
        migrated.Name = "Renamed";
        var roundTrip = ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(project));

        Assert.Equal(chassis.PhaseLabels, roundTrip.Boards[0].Chassis[0].PhaseLabels);
        Assert.Equal(originalLabel, roundTrip.Boards[0].Chassis[0].Circuits[0].DisplayLabel);
        Assert.Equal(2, roundTrip.Boards[0].Chassis[0].Circuits[0].PoleIndex);
    }

    [Fact]
    public void LegacyPhaseLabelsAreRemovedOnlyWhenRequested()
    {
        var existing = new[] { "R", "W" };

        Assert.Empty(ChassisFieldOptions.PhaseLabels(true, string.Empty, existing, true));
    }

    [Fact]
    public void UnchangedCustomPhaseTextIsPreservedExactly()
    {
        var existing = new[] { " R ", "W", "B" };

        var result = ChassisFieldOptions.PhaseLabels(false, " R ,W,B", existing, false);

        Assert.Equal(existing, result);
    }

    [Fact]
    public void EditedPhaseTextIsTrimmedAndEmptyEntriesAreRemoved()
    {
        var result = ChassisFieldOptions.PhaseLabels(false, " L1, , L2, L3 ", null, false);

        Assert.Equal(new[] { "L1", "L2", "L3" }, result);
    }
}

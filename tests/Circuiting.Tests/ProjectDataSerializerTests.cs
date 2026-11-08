using Circuiting.Models;
using Circuiting.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Circuiting.Tests;

public class ProjectDataSerializerTests
{
    [Fact]
    public void LegacyArrayMigratesToVersionedPositionBasedData()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        chassis.Circuits.Add(new CircuitData
        {
            CircuitGuid = Guid.NewGuid(),
            DisplayLabel = "W1",
            Fixtures = new List<CircuitedFixture>
            {
                new() { FixtureGuid = Guid.NewGuid(), RevitElementId = 123 }
            }
        });
        var json = JsonConvert.SerializeObject(new[] { CircuitLayoutTests.Board(chassis) });

        var project = ProjectDataSerializer.Deserialize(json);
        var migrated = project.Boards[0].Chassis[0].Circuits[0];
        var roundTrip = ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(project));

        Assert.Equal(2, migrated.PoleIndex);
        Assert.Equal("W1", migrated.DisplayLabel);
        Assert.Equal(ProjectData.CurrentSchemaVersion, roundTrip.SchemaVersion);
        Assert.Equal(migrated.CircuitGuid, roundTrip.Boards[0].Chassis[0].Circuits[0].CircuitGuid);
        Assert.Equal(123, roundTrip.Boards[0].Chassis[0].Circuits[0].Fixtures[0].RevitElementId);
    }

    [Fact]
    public void AmbiguousLegacyLabelsAreRejectedRatherThanReassigned()
    {
        var chassis = CircuitLayoutTests.Chassis(12);
        chassis.ShowPhaseLabel = false;
        chassis.Circuits.Add(new CircuitData { CircuitGuid = Guid.NewGuid(), DisplayLabel = "1" });
        var json = JsonConvert.SerializeObject(new[] { CircuitLayoutTests.Board(chassis) });

        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize(json));
    }

    [Fact]
    public void LegacyNumberCanDisambiguateMatchingLabels()
    {
        var chassis = CircuitLayoutTests.Chassis(36);
        chassis.PhaseLabels = new List<string> { "P1", "P", "X" };
        chassis.Circuits.Add(new CircuitData
        {
            CircuitGuid = Guid.NewGuid(), DisplayLabel = "P11", CircuitNumber = 11
        });
        var json = JsonConvert.SerializeObject(new[] { CircuitLayoutTests.Board(chassis) });

        var project = ProjectDataSerializer.Deserialize(json);

        Assert.Equal(32, project.Boards[0].Chassis[0].Circuits[0].PoleIndex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{\"SchemaVersion\":999,\"Boards\":[],\"Settings\":{}}")]
    [InlineData("{\"SchemaVersion\":1,\"Boards\":null,\"Settings\":{}}")]
    public void InvalidOrFutureDataIsRejected(string json)
    {
        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize(json));
    }

    [Fact]
    public void DuplicateFixtureIdentitiesAreRejected()
    {
        var chassis = CircuitLayoutTests.Chassis(6);
        var fixtureId = Guid.NewGuid();
        chassis.Circuits.Add(new CircuitData
        {
            CircuitGuid = Guid.NewGuid(),
            PoleIndex = 0,
            Fixtures = new List<CircuitedFixture>
            {
                new() { FixtureGuid = fixtureId },
                new() { FixtureGuid = fixtureId }
            }
        });

        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Serialize(
            new ProjectData { Boards = new List<BoardData> { CircuitLayoutTests.Board(chassis) } }));
    }

    [Fact]
    public void InvalidResolutionRulesAreRejected()
    {
        var project = new ProjectData();
        project.Settings.AutoRules.OnFixtureCopied = (CopiedFixtureAction)999;
        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Serialize(project));
    }

    [Fact]
    public void DuplicateJsonPropertiesAreRejected()
    {
        const string json = "{\"SchemaVersion\":1,\"SchemaVersion\":1,\"Boards\":[],\"Settings\":{}}";
        Assert.Throws<JsonReaderException>(() => ProjectDataSerializer.Deserialize(json));
    }

    [Fact]
    public void NullFixtureUniqueIdsCannotProduceInvalidVersionedData()
    {
        var chassis = CircuitLayoutTests.Chassis(6);
        chassis.Circuits.Add(new CircuitData
        {
            CircuitGuid = Guid.NewGuid(), PoleIndex = 0,
            Fixtures = new List<CircuitedFixture>
            {
                new CircuitedFixture { FixtureGuid = Guid.NewGuid(), RevitUniqueId = null! }
            }
        });
        var project = new ProjectData { Boards = new List<BoardData> { CircuitLayoutTests.Board(chassis) } };

        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Serialize(project));
    }

    [Theory]
    [InlineData("BoardGuid")]
    [InlineData("DisplayName")]
    [InlineData("Supply")]
    [InlineData("Chassis")]
    [InlineData("ChassisGuid")]
    [InlineData("Name")]
    [InlineData("MaxPoles")]
    [InlineData("StartingCircuit")]
    [InlineData("Prefix")]
    [InlineData("Separator")]
    [InlineData("ShowPhaseLabel")]
    [InlineData("ContinuePhaseFromPrevious")]
    [InlineData("PhaseLabels")]
    [InlineData("Circuits")]
    [InlineData("CircuitGuid")]
    [InlineData("DisplayLabel")]
    [InlineData("CircuitNumber")]
    [InlineData("PoleIndex")]
    [InlineData("Description")]
    [InlineData("BreakerSize")]
    [InlineData("ProtectionType")]
    [InlineData("EarthLeakage")]
    [InlineData("Fixtures")]
    [InlineData("Poles")]
    [InlineData("FixtureGuid")]
    [InlineData("RevitElementId")]
    [InlineData("RevitUniqueId")]
    [InlineData("FamilyName")]
    [InlineData("RoomName")]
    [InlineData("AutoRules")]
    [InlineData("ResolutionMode")]
    [InlineData("ModeChangedBy")]
    [InlineData("ModeLastChanged")]
    [InlineData("OnFixtureCopied")]
    [InlineData("OnBoardCopied")]
    [InlineData("OnFixtureDeleted")]
    [InlineData("LogAutomaticActions")]
    public void MissingVersionedFieldsAreNotSilentlyReplacedWithDefaults(string missing)
    {
        var chassis = CircuitLayoutTests.Chassis(6);
        chassis.Circuits.Add(new CircuitData
        {
            CircuitGuid = Guid.NewGuid(), PoleIndex = 0,
            Fixtures = new List<CircuitedFixture>
            {
                new CircuitedFixture { FixtureGuid = Guid.NewGuid(), RevitUniqueId = "fixture" }
            }
        });
        var project = new ProjectData { Boards = new List<BoardData> { CircuitLayoutTests.Board(chassis) } };
        var root = JObject.Parse(ProjectDataSerializer.Serialize(project));
        var board = (JObject)root["Boards"]![0]!;
        var chassisObject = (JObject)board["Chassis"]![0]!;
        var circuit = (JObject)chassisObject["Circuits"]![0]!;
        var fixture = (JObject)circuit["Fixtures"]![0]!;
        JObject container = missing switch
        {
            "BoardGuid" or "DisplayName" or "Supply" or "Chassis" => board,
            "ChassisGuid" or "Name" or "MaxPoles" or "StartingCircuit" or "Prefix" or
                "Separator" or "ShowPhaseLabel" or "ContinuePhaseFromPrevious" or
                "PhaseLabels" or "Circuits" => chassisObject,
            "CircuitGuid" or "DisplayLabel" or "CircuitNumber" or "PoleIndex" or "Description" or
                "BreakerSize" or "ProtectionType" or "EarthLeakage" or "Fixtures" or "Poles" => circuit,
            "FixtureGuid" or "RevitElementId" or "RevitUniqueId" or "FamilyName" or "RoomName" => fixture,
            "AutoRules" or "ResolutionMode" or "ModeChangedBy" or "ModeLastChanged" => (JObject)root["Settings"]!,
            "OnFixtureCopied" or "OnBoardCopied" or "OnFixtureDeleted" or "LogAutomaticActions" =>
                (JObject)root["Settings"]!["AutoRules"]!,
            _ => throw new InvalidOperationException()
        };
        container.Remove(missing);

        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize(root.ToString()));
    }

    [Fact]
    public void TripDetailsRoundTripAndProjectsSavedBeforeThemStillLoad()
    {
        var chassis = CircuitLayoutTests.Chassis(6);
        chassis.Circuits.Add(new CircuitData
        {
            CircuitGuid = Guid.NewGuid(), PoleIndex = 0, BreakerSize = "16A", TripCurve = "C",
            TripSetting = "Ir 0.8×In",
            Fixtures = new List<CircuitedFixture>
            {
                new CircuitedFixture { FixtureGuid = Guid.NewGuid(), RevitUniqueId = "fixture" }
            }
        });
        var project = new ProjectData { Boards = new List<BoardData> { CircuitLayoutTests.Board(chassis) } };
        string json = ProjectDataSerializer.Serialize(project);

        CircuitData loaded = ProjectDataSerializer.Deserialize(json).Boards[0].Chassis[0].Circuits[0];
        Assert.Equal("C", loaded.TripCurve);
        Assert.Equal("Ir 0.8×In", loaded.TripSetting);

        var root = JObject.Parse(json);
        var circuit = (JObject)root["Boards"]![0]!["Chassis"]![0]!["Circuits"]![0]!;
        circuit.Remove("TripCurve");
        circuit.Remove("TripSetting");
        CircuitData old = ProjectDataSerializer.Deserialize(root.ToString()).Boards[0].Chassis[0].Circuits[0];
        Assert.Equal(string.Empty, old.TripCurve);
        Assert.Equal(string.Empty, old.TripSetting);

        circuit["TripCurve"] = JValue.CreateNull();
        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize(root.ToString()));
        circuit["TripCurve"] = 3;
        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize(root.ToString()));
    }

    [Theory]
    [InlineData("Supply")]
    [InlineData("ShowPhaseLabel")]
    [InlineData("Description")]
    [InlineData("PhaseLabels")]
    [InlineData("FamilyName")]
    [InlineData("ModeLastChanged")]
    public void VersionedFieldsWithWrongTypesAreRejected(string field)
    {
        var chassis = CircuitLayoutTests.Chassis(6);
        chassis.Circuits.Add(new CircuitData
        {
            CircuitGuid = Guid.NewGuid(), PoleIndex = 0,
            Fixtures = new List<CircuitedFixture>
            {
                new CircuitedFixture { FixtureGuid = Guid.NewGuid(), RevitUniqueId = "fixture" }
            }
        });
        var project = new ProjectData { Boards = new List<BoardData> { CircuitLayoutTests.Board(chassis) } };
        var root = JObject.Parse(ProjectDataSerializer.Serialize(project));
        var board = (JObject)root["Boards"]![0]!;
        var chassisObject = (JObject)board["Chassis"]![0]!;
        var circuit = (JObject)chassisObject["Circuits"]![0]!;
        var fixture = (JObject)circuit["Fixtures"]![0]!;
        switch (field)
        {
            case "Supply": board[field] = "1"; break;
            case "ShowPhaseLabel": chassisObject[field] = "true"; break;
            case "Description": circuit[field] = 123; break;
            case "PhaseLabels": chassisObject[field]![0] = 123; break;
            case "FamilyName": fixture[field] = JValue.CreateNull(); break;
            case "ModeLastChanged": root["Settings"]![field] = JValue.CreateNull(); break;
            default: throw new InvalidOperationException();
        }

        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize(root.ToString()));
    }

    [Fact]
    public void DateLikeTextSurvivesVersionedAndLegacyRoundTrips()
    {
        const string text = "2024-01-03T11:22:33Z";
        var chassis = CircuitLayoutTests.Chassis(6);
        chassis.Circuits.Add(new CircuitData
        {
            CircuitGuid = Guid.NewGuid(), PoleIndex = 0, Description = text,
            Fixtures = new List<CircuitedFixture>
            {
                new CircuitedFixture { FixtureGuid = Guid.NewGuid(), RevitUniqueId = "fixture", FamilyName = text }
            }
        });
        var board = CircuitLayoutTests.Board(chassis);
        board.DisplayName = text;
        var project = new ProjectData { Boards = new List<BoardData> { board } };
        project.Settings.ModeChangedBy = text;
        var changedAt = new DateTime(2024, 1, 3, 11, 22, 33, DateTimeKind.Utc);
        project.Settings.ModeLastChanged = changedAt;

        var roundTrip = ProjectDataSerializer.Deserialize(ProjectDataSerializer.Serialize(project));
        Assert.Equal(text, roundTrip.Settings.ModeChangedBy);
        Assert.Equal(changedAt, roundTrip.Settings.ModeLastChanged);
        Assert.Equal(DateTimeKind.Utc, roundTrip.Settings.ModeLastChanged.Kind);
        Assert.Equal(text, roundTrip.Boards[0].DisplayName);
        Assert.Equal(text, roundTrip.Boards[0].Chassis[0].Circuits[0].Description);
        Assert.Equal(text, roundTrip.Boards[0].Chassis[0].Circuits[0].Fixtures[0].FamilyName);

        var legacy = JArray.FromObject(project.Boards);
        var legacyCircuit = (JObject)legacy[0]!["Chassis"]![0]!["Circuits"]![0]!;
        legacyCircuit.Remove("PoleIndex");
        ((JObject)legacyCircuit["Fixtures"]![0]!).Remove("RevitUniqueId");
        var migrated = ProjectDataSerializer.Deserialize(legacy.ToString());
        Assert.Equal(text, migrated.Boards[0].DisplayName);
        Assert.Equal(text, migrated.Boards[0].Chassis[0].Circuits[0].Description);
        Assert.Equal(text, migrated.Boards[0].Chassis[0].Circuits[0].Fixtures[0].FamilyName);
    }

    [Fact]
    public void TrailingJsonContentIsRejected()
    {
        Assert.Throws<JsonReaderException>(() => ProjectDataSerializer.Deserialize("[]{}"));
    }

    [Fact]
    public void NullPersistedTextIsRejectedBeforeSerialization()
    {
        var chassis = CircuitLayoutTests.Chassis(6);
        var board = CircuitLayoutTests.Board(chassis);
        var project = new ProjectData { Boards = new List<BoardData> { board } };
        board.DisplayName = null!;
        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Serialize(project));

        board.DisplayName = "Board";
        chassis.PhaseLabels[0] = null!;
        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Serialize(project));
    }

    [Fact]
    public void LegacyDataWithMissingCircuitListsIsRejected()
    {
        var board = CircuitLayoutTests.Board(CircuitLayoutTests.Chassis(6));
        var array = JArray.FromObject(new[] { board });
        ((JObject)array[0]!["Chassis"]![0]!).Remove("Circuits");

        Assert.Throws<InvalidDataException>(() => ProjectDataSerializer.Deserialize(array.ToString()));
    }
}

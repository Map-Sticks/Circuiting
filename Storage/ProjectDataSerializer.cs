using System.IO;
using Circuiting.Models;
using Circuiting.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Circuiting.Storage
{
    public static class ProjectDataSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        public static ProjectData Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException("The stored project data is empty.");

            using var reader = new JsonTextReader(new StringReader(json))
            {
                DateParseHandling = DateParseHandling.None
            };
            JToken token = JToken.ReadFrom(reader, new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
            });
            // Read to the end so trailing content is rejected.
            while (reader.Read()) { }

            bool legacy = token.Type == JTokenType.Array;
            ProjectData project;

            if (legacy)
            {
                ValidateBoardStructure((JArray)token, false);
                project = new ProjectData
                {
                    Boards = token.ToObject<List<BoardData>>(JsonSerializer.Create(Settings))
                        ?? throw new InvalidDataException("The stored board list is null.")
                };
            }
            else if (token is JObject obj)
            {
                JToken? version = obj[nameof(ProjectData.SchemaVersion)];
                if (version?.Type != JTokenType.Integer)
                    throw new InvalidDataException("The project data has no valid schema version.");
                int schemaVersion = version.Value<int>();
                if (schemaVersion != ProjectData.CurrentSchemaVersion)
                    throw new InvalidDataException($"Project schema version {schemaVersion} is not supported.");
                if (obj[nameof(ProjectData.Boards)] is not JArray boards ||
                    obj[nameof(ProjectData.Settings)] is not JObject settings)
                    throw new InvalidDataException("The project data is missing its boards or settings.");
                RequireType(settings, nameof(ProjectSettings.ResolutionMode), JTokenType.Integer, "project settings");
                RequireTypes(settings, JTokenType.String, "project settings",
                    nameof(ProjectSettings.ModeChangedBy), nameof(ProjectSettings.ModeLastChanged));
                if (settings[nameof(ProjectSettings.AutoRules)] is not JObject rules)
                    throw new InvalidDataException("The project data is missing its automatic resolution rules.");
                RequireType(rules, nameof(AutomaticResolutionRules.OnFixtureCopied), JTokenType.Integer, "resolution rules");
                RequireType(rules, nameof(AutomaticResolutionRules.OnBoardCopied), JTokenType.Integer, "resolution rules");
                RequireType(rules, nameof(AutomaticResolutionRules.OnFixtureDeleted), JTokenType.Integer, "resolution rules");
                RequireType(rules, nameof(AutomaticResolutionRules.LogAutomaticActions), JTokenType.Boolean, "resolution rules");
                ValidateBoardStructure(boards, true);

                project = obj.ToObject<ProjectData>(JsonSerializer.Create(Settings))
                    ?? throw new InvalidDataException("The stored project data is null.");
            }
            else
            {
                throw new InvalidDataException("The stored project data has an unexpected format.");
            }

            Validate(project, legacy);
            return project;
        }

        public static string Serialize(ProjectData project)
        {
            Validate(project, false);
            return JsonConvert.SerializeObject(project, Settings);
        }

        private static void ValidateBoardStructure(JArray boards, bool versioned)
        {
            foreach (JToken boardToken in boards)
            {
                JObject board = RequireObject(boardToken, "board");
                if (versioned)
                {
                    RequireTypes(board, JTokenType.String, "board",
                        nameof(BoardData.BoardGuid), nameof(BoardData.DisplayName));
                    RequireType(board, nameof(BoardData.Supply), JTokenType.Integer, "board");
                }

                foreach (JToken chassisToken in RequireArray(board, nameof(BoardData.Chassis), "board"))
                {
                    JObject chassis = RequireObject(chassisToken, "chassis");
                    JArray phaseLabels = RequireArray(chassis, nameof(ChassisData.PhaseLabels), "chassis");
                    if (versioned)
                    {
                        RequireTypes(chassis, JTokenType.String, "chassis",
                            nameof(ChassisData.ChassisGuid), nameof(ChassisData.Name),
                            nameof(ChassisData.Prefix), nameof(ChassisData.Separator));
                        RequireTypes(chassis, JTokenType.Integer, "chassis",
                            nameof(ChassisData.MaxPoles), nameof(ChassisData.StartingCircuit));
                        RequireTypes(chassis, JTokenType.Boolean, "chassis",
                            nameof(ChassisData.ShowPhaseLabel), nameof(ChassisData.ContinuePhaseFromPrevious));
                        if (phaseLabels.Any(label => label.Type != JTokenType.String))
                            throw new InvalidDataException("The stored chassis has invalid phase labels.");
                    }

                    foreach (JToken circuitToken in RequireArray(chassis, nameof(ChassisData.Circuits), "chassis"))
                    {
                        JObject circuit = RequireObject(circuitToken, "circuit");
                        if (versioned)
                        {
                            RequireTypes(circuit, JTokenType.String, "circuit",
                                nameof(CircuitData.CircuitGuid), nameof(CircuitData.DisplayLabel),
                                nameof(CircuitData.Description), nameof(CircuitData.BreakerSize),
                                nameof(CircuitData.ProtectionType), nameof(CircuitData.EarthLeakage));
                            RequireTypes(circuit, JTokenType.Integer, "circuit",
                                nameof(CircuitData.CircuitNumber), nameof(CircuitData.PoleIndex),
                                nameof(CircuitData.Poles));
                            // Added after schema version 1 was released; older projects omit them.
                            OptionalTypes(circuit, JTokenType.String, "circuit",
                                nameof(CircuitData.TripCurve), nameof(CircuitData.TripSetting));
                        }

                        foreach (JToken fixtureToken in RequireArray(circuit, nameof(CircuitData.Fixtures), "circuit"))
                        {
                            JObject fixture = RequireObject(fixtureToken, "fixture");
                            if (!versioned) continue;
                            RequireType(fixture, nameof(CircuitedFixture.RevitElementId), JTokenType.Integer, "fixture");
                            RequireTypes(fixture, JTokenType.String, "fixture",
                                nameof(CircuitedFixture.FixtureGuid), nameof(CircuitedFixture.RevitUniqueId),
                                nameof(CircuitedFixture.FamilyName), nameof(CircuitedFixture.RoomName));
                        }
                    }
                }
            }
        }

        private static JObject RequireObject(JToken token, string context) =>
            token as JObject ?? throw new InvalidDataException($"The stored {context} is not an object.");

        private static JArray RequireArray(JObject obj, string property, string context) =>
            obj[property] as JArray ??
            throw new InvalidDataException($"The stored {context} is missing its {property} list.");

        private static void RequireType(JObject obj, string property, JTokenType type, string context)
        {
            if (obj[property]?.Type != type)
                throw new InvalidDataException($"The stored {context} has missing or invalid {property}.");
        }

        private static void OptionalTypes(JObject obj, JTokenType type, string context, params string[] properties)
        {
            foreach (string property in properties)
                if (obj[property] != null)
                    RequireType(obj, property, type, context);
        }

        private static void RequireTypes(JObject obj, JTokenType type, string context, params string[] properties)
        {
            foreach (string property in properties)
                RequireType(obj, property, type, context);
        }

        private static void Validate(ProjectData project, bool legacy)
        {
            if (project.SchemaVersion != ProjectData.CurrentSchemaVersion ||
                project.Boards == null || project.Settings?.AutoRules == null)
                throw new InvalidDataException("The project data is incomplete or has an unsupported version.");
            if (project.Settings.ModeChangedBy == null)
                throw new InvalidDataException("The project settings contain a null mode-change author.");
            if (!Enum.IsDefined(typeof(ResolutionMode), project.Settings.ResolutionMode) ||
                !Enum.IsDefined(typeof(CopiedFixtureAction), project.Settings.AutoRules.OnFixtureCopied) ||
                !Enum.IsDefined(typeof(CopiedBoardAction), project.Settings.AutoRules.OnBoardCopied) ||
                !Enum.IsDefined(typeof(DeletedFixtureAction), project.Settings.AutoRules.OnFixtureDeleted))
                throw new InvalidDataException("The project data contains an invalid resolution setting.");

            var boardIds = new HashSet<Guid>();
            var chassisIds = new HashSet<Guid>();
            var circuitIds = new HashSet<Guid>();
            var fixtureIds = new HashSet<Guid>();
            var fixtureUniqueIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (BoardData board in project.Boards)
            {
                if (board == null || board.BoardGuid == Guid.Empty || !boardIds.Add(board.BoardGuid) ||
                    board.DisplayName == null ||
                    board.Chassis == null || !Enum.IsDefined(typeof(SupplyType), board.Supply))
                    throw new InvalidDataException("A board has missing or duplicate identity or chassis data.");

                foreach (ChassisData chassis in board.Chassis)
                {
                    if (chassis == null || chassis.ChassisGuid == Guid.Empty ||
                        !chassisIds.Add(chassis.ChassisGuid) || chassis.Circuits == null ||
                        chassis.Name == null || chassis.Prefix == null || chassis.Separator == null ||
                        chassis.PhaseLabels == null || chassis.PhaseLabels.Any(label => label == null))
                        throw new InvalidDataException($"Board '{board.DisplayName}' has invalid chassis data.");

                    // Legacy loading previously removed duplicated phase labels.
                    if (legacy && chassis.PhaseLabels.Count > 3)
                        chassis.PhaseLabels = chassis.PhaseLabels.Distinct().ToList();

                    foreach (CircuitData circuit in chassis.Circuits)
                    {
                        if (circuit == null || circuit.CircuitGuid == Guid.Empty ||
                            !circuitIds.Add(circuit.CircuitGuid) || circuit.Fixtures == null ||
                            circuit.DisplayLabel == null || circuit.Description == null ||
                            circuit.BreakerSize == null || circuit.ProtectionType == null ||
                            circuit.EarthLeakage == null || circuit.TripCurve == null || circuit.TripSetting == null)
                            throw new InvalidDataException($"Chassis '{chassis.Name}' has invalid circuit data.");

                        foreach (CircuitedFixture fixture in circuit.Fixtures)
                        {
                            if (fixture == null || fixture.FixtureGuid == Guid.Empty || fixture.RevitUniqueId == null ||
                                fixture.FamilyName == null || fixture.RoomName == null ||
                                !fixtureIds.Add(fixture.FixtureGuid) ||
                                (!string.IsNullOrEmpty(fixture.RevitUniqueId) &&
                                 !fixtureUniqueIds.Add(fixture.RevitUniqueId)))
                                throw new InvalidDataException(
                                    $"Circuit '{circuit.DisplayLabel}' has missing or duplicate fixture identity.");
                        }
                    }

                    if (legacy) CircuitLayout.MigrateLegacyPositions(chassis);
                    CircuitLayout.ValidateAndUpdate(board, chassis);
                }
            }
        }
    }
}

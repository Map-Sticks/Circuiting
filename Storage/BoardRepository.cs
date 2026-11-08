using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Circuiting.Models;
using Newtonsoft.Json;
using System.IO;

namespace Circuiting.Storage
{
    public static class BoardRepository
    {
        // ─── Project Level ────────────────────────────────────────────

        public static List<BoardData> LoadAllBoards(Document doc)
        {
            return LoadProjectData(doc).Boards;
        }

        public static ProjectData LoadProjectData(Document doc)
        {
            ProjectInfo projectInfo = doc.ProjectInformation;
            Schema schema = ESSchema.GetOrCreateProjectSchema();
            Entity entity = projectInfo.GetEntity(schema);
            if (!entity.IsValid()) return new ProjectData();

            try
            {
                return ProjectDataSerializer.Deserialize(entity.Get<string>("ProjectDataJson"));
            }
            catch (Exception ex) when (ex is JsonException || ex is InvalidDataException ||
                                       ex is ArgumentException || ex is OverflowException)
            {
                throw new InvalidDataException(
                    "Circuit data could not be loaded. The existing model data has not been overwritten. " +
                    ex.Message, ex);
            }
        }

        public static void SaveAllBoards(Document doc, List<BoardData> boards)
        {
            ProjectData project = LoadProjectData(doc);
            project.Boards = boards;
            SaveProjectData(doc, project);
        }

        public static void SaveProjectData(Document doc, ProjectData project)
        {
            ProjectInfo projectInfo = doc.ProjectInformation;
            Schema schema = ESSchema.GetOrCreateProjectSchema();
            string json = ProjectDataSerializer.Serialize(project);

            Entity entity = new Entity(schema);
            entity.Set("ProjectDataJson", json);
            projectInfo.SetEntity(entity);
        }

        // ─── Board Level ──────────────────────────────────────────────

        public static BoardData? LoadBoard(Element panelElement)
        {
            Schema schema = ESSchema.GetOrCreateBoardSchema();
            Entity entity = panelElement.GetEntity(schema);

            if (!entity.IsValid())
                return null;

            string json = entity.Get<string>("BoardDataJson");

            if (string.IsNullOrEmpty(json))
                throw new InvalidDataException("The board data is empty.");

            var settings = new JsonSerializerSettings
            {
                ObjectCreationHandling = ObjectCreationHandling.Replace
            };
            return JsonConvert.DeserializeObject<BoardData>(json, settings)
                ?? throw new InvalidDataException("The board data is null.");
        }

        public static void SaveBoard(Element panelElement, BoardData board)
        {
            Schema schema = ESSchema.GetOrCreateBoardSchema();
            string json = JsonConvert.SerializeObject(board);

            Entity entity = new Entity(schema);
            entity.Set("BoardDataJson", json);
            panelElement.SetEntity(entity);
        }

        public static bool HasBoardData(Element panelElement)
        {
            Schema schema = ESSchema.GetOrCreateBoardSchema();
            Entity entity = panelElement.GetEntity(schema);
            return entity.IsValid();
        }

        // ─── Fixture Level ────────────────────────────────────────────

        public static void SaveFixtureAssignment(
            Element fixture,
            Guid fixtureGuid,
            Guid boardGuid,
            Guid chassisGuid,
            Guid circuitGuid,
            string circuitReference)
        {
            Schema schema = ESSchema.GetOrCreateFixtureSchema();

            Entity entity = new Entity(schema);
            entity.Set("FixtureGuid", fixtureGuid.ToString());
            entity.Set("BoardGuid", boardGuid.ToString());
            entity.Set("ChassisGuid", chassisGuid.ToString());
            entity.Set("CircuitGuid", circuitGuid.ToString());
            entity.Set("CircuitReference", circuitReference);
            fixture.SetEntity(entity);
        }

        public static void ClearFixtureAssignment(Element fixture)
        {
            Schema schema = ESSchema.GetOrCreateFixtureSchema();
            fixture.DeleteEntity(schema);
        }

        public static bool IsFixtureCircuited(Element fixture)
        {
            Schema schema = ESSchema.GetOrCreateFixtureSchema();
            Entity entity = fixture.GetEntity(schema);
            return entity.IsValid();
        }

        public static string? GetFixtureCircuitReference(Element fixture)
        {
            return GetFixtureAssignment(fixture)?.CircuitReference;
        }

        public static FixtureAssignment? GetFixtureAssignment(Element fixture)
        {
            Entity entity = fixture.GetEntity(ESSchema.GetOrCreateFixtureSchema());
            if (!entity.IsValid()) return null;

            return new FixtureAssignment
            {
                FixtureGuid = ReadGuid(entity, "FixtureGuid"),
                BoardGuid = ReadGuid(entity, "BoardGuid"),
                ChassisGuid = ReadGuid(entity, "ChassisGuid"),
                CircuitGuid = ReadGuid(entity, "CircuitGuid"),
                CircuitReference = entity.Get<string>("CircuitReference")
            };
        }

        private static Guid ReadGuid(Entity entity, string field)
        {
            if (!Guid.TryParse(entity.Get<string>(field), out Guid value) || value == Guid.Empty)
                throw new InvalidDataException($"Fixture assignment has an invalid {field}.");
            return value;
        }
    }

}
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Circuiting.Models;
using Newtonsoft.Json;

namespace Circuiting.Storage
{
    public static class BoardRepository
    {
        // ─── Project Level ────────────────────────────────────────────

        public static List<BoardData> LoadAllBoards(Document doc)
        {
            try
            {
                ProjectInfo projectInfo = doc.ProjectInformation;
                Schema schema = ESSchema.GetOrCreateProjectSchema();
                Entity entity = projectInfo.GetEntity(schema);

                if (!entity.IsValid())
                    return new List<BoardData>();

                string json = entity.Get<string>("ProjectDataJson");

                if (string.IsNullOrEmpty(json))
                    return new List<BoardData>();

                var settings = new JsonSerializerSettings
                {
                    ObjectCreationHandling = ObjectCreationHandling.Replace
                };
                List<BoardData> boards = JsonConvert
                    .DeserializeObject<List<BoardData>>(json, settings)
                    ?? new List<BoardData>();

                // Clean up any duplicated phase labels from previous bug
                foreach (BoardData board in boards)
                {
                    foreach (ChassisData chassis in board.Chassis)
                    {
                        if (chassis.PhaseLabels != null
                            && chassis.PhaseLabels.Count > 3)
                        {
                            chassis.PhaseLabels =
                                chassis.PhaseLabels.Distinct().ToList();
                        }
                    }
                }

                return boards;
            }
            catch
            {
                return new List<BoardData>();
            }
        }

        public static void SaveAllBoards(Document doc, List<BoardData> boards)
        {
            ProjectInfo projectInfo = doc.ProjectInformation;
            Schema schema = ESSchema.GetOrCreateProjectSchema();

            string json = JsonConvert.SerializeObject(boards);

            Entity entity = new Entity(schema);
            entity.Set("ProjectDataJson", json);
            projectInfo.SetEntity(entity);
        }

        // ─── Board Level ──────────────────────────────────────────────

        public static BoardData? LoadBoard(Element panelElement)
        {
            try
            {
                Schema schema = ESSchema.GetOrCreateBoardSchema();
                Entity entity = panelElement.GetEntity(schema);

                if (!entity.IsValid())
                    return null;

                string json = entity.Get<string>("BoardDataJson");

                if (string.IsNullOrEmpty(json))
                    return null;

                var settings = new JsonSerializerSettings
                {
                    ObjectCreationHandling = ObjectCreationHandling.Replace
                };
                return JsonConvert.DeserializeObject<BoardData>(json, settings);
            }
            catch
            {
                return null;
            }
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
            try
            {
                Schema schema = ESSchema.GetOrCreateFixtureSchema();
                Entity entity = fixture.GetEntity(schema);

                if (!entity.IsValid())
                    return string.Empty;

                return entity.Get<string>("CircuitReference");
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
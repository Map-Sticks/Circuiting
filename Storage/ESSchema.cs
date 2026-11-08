using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace Circuiting.Storage
{
    public static class ESSchema
    {
        // These GUIDs must never change — they are the permanent 
        // identifiers for your data in every Revit file
        private static readonly Guid ProjectSchemaGuid =
            new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");

        private static readonly Guid BoardSchemaGuid =
            new Guid("B2C3D4E5-F6A7-8901-BCDE-F12345678901");

        private static readonly Guid FixtureSchemaGuid =
            new Guid("C3D4E5F6-A7B8-9012-CDEF-123456789012");

        // Schema names
        private const string ProjectSchemaName = "DBCircuitManager_Project";
        private const string BoardSchemaName = "DBCircuitManager_Board";
        private const string FixtureSchemaName = "DBCircuitManager_Fixture";

        // ─── Project Schema ───────────────────────────────────────────
        // Stored on ProjectInfo — holds the list of board references
        // and project-level settings
        public static Schema GetOrCreateProjectSchema()
        {
            Schema existing = Schema.Lookup(ProjectSchemaGuid);
            if (existing != null) return existing;

            SchemaBuilder sb = new SchemaBuilder(ProjectSchemaGuid);
            sb.SetSchemaName(ProjectSchemaName);
            sb.SetReadAccessLevel(AccessLevel.Public);
            sb.SetWriteAccessLevel(AccessLevel.Public);
            sb.SetDocumentation("DB Circuit Manager project-level data");

            // Stores full JSON of all board references and settings
            sb.AddSimpleField("ProjectDataJson", typeof(string));

            return sb.Finish();
        }

        // ─── Board Schema ─────────────────────────────────────────────
        // Stored on the panel FamilyInstance — holds full board data
        public static Schema GetOrCreateBoardSchema()
        {
            Schema existing = Schema.Lookup(BoardSchemaGuid);
            if (existing != null) return existing;

            SchemaBuilder sb = new SchemaBuilder(BoardSchemaGuid);
            sb.SetSchemaName(BoardSchemaName);
            sb.SetReadAccessLevel(AccessLevel.Public);
            sb.SetWriteAccessLevel(AccessLevel.Public);
            sb.SetDocumentation("DB Circuit Manager board data");

            // Stores full JSON of board config including chassis and circuits
            sb.AddSimpleField("BoardDataJson", typeof(string));

            return sb.Finish();
        }

        // ─── Fixture Schema ───────────────────────────────────────────
        // Stored on each circuited fixture — holds circuit assignment
        public static Schema GetOrCreateFixtureSchema()
        {
            Schema existing = Schema.Lookup(FixtureSchemaGuid);
            if (existing != null) return existing;

            SchemaBuilder sb = new SchemaBuilder(FixtureSchemaGuid);
            sb.SetSchemaName(FixtureSchemaName);
            sb.SetReadAccessLevel(AccessLevel.Public);
            sb.SetWriteAccessLevel(AccessLevel.Public);
            sb.SetDocumentation("DB Circuit Manager fixture circuit assignment");

            sb.AddSimpleField("FixtureGuid", typeof(string));
            sb.AddSimpleField("BoardGuid", typeof(string));
            sb.AddSimpleField("ChassisGuid", typeof(string));
            sb.AddSimpleField("CircuitGuid", typeof(string));
            sb.AddSimpleField("CircuitReference", typeof(string));

            return sb.Finish();
        }
    }
}
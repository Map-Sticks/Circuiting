using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Circuiting.Models;
using Circuiting.ViewModels;

namespace Circuiting.Services
{
    /// <summary>Reads live element information (family, type, MEP space, level) for circuited elements.</summary>
    public static class FixtureDetailsReader
    {
        public static Dictionary<Guid, FixtureDetails> Read(Document doc, ProjectData project)
        {
            var details = new Dictionary<Guid, FixtureDetails>();
            var phases = new Dictionary<long, Phase?>();
            foreach (BoardData board in project.Boards)
            foreach (ChassisData chassis in board.Chassis)
            foreach (CircuitData circuit in chassis.Circuits)
            foreach (CircuitedFixture fixture in circuit.Fixtures)
            {
                if (details.ContainsKey(fixture.FixtureGuid)) continue;
                Element? element = RevitElementIds.Resolve(doc, fixture);
                details[fixture.FixtureGuid] = element == null
                    ? new FixtureDetails { Found = false, ElementId = fixture.RevitElementId }
                    : Describe(doc, element, phases);
            }
            return details;
        }

        private static FixtureDetails Describe(Document doc, Element element, Dictionary<long, Phase?> phases)
        {
            var details = new FixtureDetails
            {
                Found = true,
                ElementId = RevitElementIds.GetValue(element.Id),
                Category = element.Category?.Name ?? string.Empty,
                TypeName = element.Name ?? string.Empty,
                Mark = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? string.Empty
            };

            Phase? phase = PhaseOf(doc, element, phases);
            Space? space = null;
            Room? room = null;
            if (element is FamilyInstance instance)
            {
                details.FamilyName = instance.Symbol?.FamilyName ?? string.Empty;
                space = Safe(() => instance.Space) ?? (phase == null ? null : Safe(() => instance.get_Space(phase)));
                room = Safe(() => instance.Room) ?? (phase == null ? null : Safe(() => instance.get_Room(phase)));
            }
            if (space == null && element.Location is LocationPoint location)
                space = Safe(() => phase == null ? doc.GetSpaceAtPoint(location.Point)
                    : doc.GetSpaceAtPoint(location.Point, phase));

            if (space != null)
            {
                details.SpaceNumber = space.Number ?? string.Empty;
                details.SpaceName = space.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? string.Empty;
            }
            if (room != null)
                details.RoomName = $"{room.Number} {room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString()}".Trim();

            if (doc.GetElement(element.LevelId) is Level level)
                details.LevelName = level.Name;
            else
                details.LevelName = element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)?.AsValueString()
                    ?? element.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM)?.AsValueString()
                    ?? string.Empty;
            return details;
        }

        private static Phase? PhaseOf(Document doc, Element element, Dictionary<long, Phase?> phases)
        {
            ElementId id = element.CreatedPhaseId;
            if (id == null || id == ElementId.InvalidElementId) return null;
            long key = RevitElementIds.GetValue(id);
            if (!phases.TryGetValue(key, out Phase? phase))
                phases[key] = phase = doc.GetElement(id) as Phase;
            return phase;
        }

        private static T? Safe<T>(Func<T?> read) where T : class
        {
            try
            {
                return read();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;
            }
        }
    }
}

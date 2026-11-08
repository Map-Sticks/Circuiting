using Autodesk.Revit.DB;
using Circuiting.Models;
using Circuiting.Services;

namespace Circuiting.Storage
{
    public sealed class RevitFixtureAssignmentStore : IFixtureModelStore<Element>
    {
        private readonly Document _document;

        public RevitFixtureAssignmentStore(Document document)
        {
            _document = document;
        }

        public Element? Resolve(CircuitedFixture fixture) => RevitElementIds.Resolve(_document, fixture);
        public string GetUniqueId(Element element) => element.UniqueId;
        public long GetElementId(Element element) => RevitElementIds.GetValue(element.Id);
        public FixtureAssignment? Read(Element element) => BoardRepository.GetFixtureAssignment(element);

        public void Write(Element element, FixtureAssignment assignment)
        {
            BoardRepository.SaveFixtureAssignment(element, assignment.FixtureGuid, assignment.BoardGuid,
                assignment.ChassisGuid, assignment.CircuitGuid, assignment.CircuitReference);
        }

        public void Clear(Element element) => BoardRepository.ClearFixtureAssignment(element);

        public IEnumerable<Element> GetFixtureElements(IEnumerable<long>? elementIds = null)
        {
            BuiltInCategory[] categories =
            {
                BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_ElectricalFixtures,
                BuiltInCategory.OST_ElectricalEquipment
            };
            List<ElementId>? ids = elementIds?.Distinct().Select(RevitElementIds.Create)
                .Where(id => _document.GetElement(id) != null).ToList();
            if (ids != null && ids.Count == 0) return new List<Element>();
            using (var collector = ids == null
                ? new FilteredElementCollector(_document)
                : new FilteredElementCollector(_document, ids))
            {
                return collector.WherePasses(new ElementMulticategoryFilter(categories))
                    .WhereElementIsNotElementType().ToElements().ToList();
            }
        }

        public CircuitedFixture CreateFixture(Element element, Guid fixtureGuid)
        {
            string roomName = element is FamilyInstance family && family.Room != null
                ? family.Room.Name : string.Empty;
            return new CircuitedFixture
            {
                FixtureGuid = fixtureGuid,
                RevitElementId = GetElementId(element),
                RevitUniqueId = element.UniqueId,
                FamilyName = element.Name,
                RoomName = roomName
            };
        }
    }
}

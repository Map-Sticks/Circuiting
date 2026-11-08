using Autodesk.Revit.DB;
using Circuiting.Models;
using Circuiting.Storage;

namespace Circuiting.Services
{
    public static class RevitElementIds
    {
        public static long GetValue(ElementId id)
        {
#if DEBUG_R23 || RELEASE_R23
            return id.IntegerValue;
#else
            return id.Value;
#endif
        }

        public static ElementId Create(long value)
        {
#if DEBUG_R23 || RELEASE_R23
            return new ElementId(checked((int)value));
#else
            return new ElementId(value);
#endif
        }

        public static Element? Resolve(Document document, CircuitedFixture fixture)
        {
            if (!string.IsNullOrEmpty(fixture.RevitUniqueId))
                return document.GetElement(fixture.RevitUniqueId);

            Element? element = document.GetElement(Create(fixture.RevitElementId));
            if (element != null)
                FixtureIdentity.RequireLegacyAssignment(fixture, BoardRepository.GetFixtureAssignment(element));
            return element;
        }
    }
}

using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;
using Circuiting.Services;

namespace Circuiting.Commands
{
    public class CircuitFixtureFilter : ISelectionFilter
    {
        private HashSet<long> _allowedIds;

        public CircuitFixtureFilter(IEnumerable<long> fixtureElementIds)
        {
            _allowedIds = new HashSet<long>(fixtureElementIds);
        }

        public bool AllowElement(Element elem)
        {
            if (elem == null) return false;
            return _allowedIds.Contains(RevitElementIds.GetValue(elem.Id));
        }

        public bool AllowReference(Reference reference,
            XYZ position)
        {
            return false;
        }
    }
}
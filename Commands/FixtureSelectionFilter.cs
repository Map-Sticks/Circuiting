using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace Circuiting.Commands
{
    public class FixtureSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem)
        {
            if (elem?.Category == null) return false;

            ElementId catId = elem.Category.Id;

            return catId == new ElementId(
                       BuiltInCategory.OST_LightingFixtures)
                || catId == new ElementId(
                       BuiltInCategory.OST_ElectricalFixtures)
                || catId == new ElementId(
                       BuiltInCategory.OST_ElectricalEquipment);
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}
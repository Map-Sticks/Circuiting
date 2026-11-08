using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Nice3point.Revit.Toolkit.External;

namespace Circuiting.Commands
{
    [UsedImplicitly]
    [Transaction(TransactionMode.Manual)]
    public class StartupCommand : ExternalCommand
    {
        public override void Execute()
        {
            UIDocument? uidoc = Application.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show("DB Circuit Manager", "Open a Revit project first.");
                return;
            }

            var panel = Circuiting.Application.PaneInstance;
            if (panel == null)
                throw new InvalidOperationException("The circuit manager pane was not registered.");
            panel.StartTracking(uidoc.Document);
            Application.GetDockablePane(Circuiting.Application.PaneId).Show();
        }
    }
}

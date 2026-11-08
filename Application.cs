using Autodesk.Revit.UI;
using Circuiting.Commands;
using Circuiting.UI.DockablePanel;
using Nice3point.Revit.Toolkit.External;

namespace Circuiting
{
    [UsedImplicitly]
    public class Application : ExternalApplication
    {
        public static DockablePaneId PaneId =
            new DockablePaneId(
                new Guid("D1E2F3A4-B5C6-7890-DEFA-123456789ABC"));

        public static CircuitManagerPane? PaneInstance;
        public static RevitEventHandler EventHandler = null!;
        public static Autodesk.Revit.UI.ExternalEvent RevitEvent = null!;

        public override void OnStartup()
        {
            EventHandler = new RevitEventHandler();
            RevitEvent = Autodesk.Revit.UI.ExternalEvent.Create(EventHandler);

            CreateRibbon();
            RegisterDockablePane();
        }

        private void CreateRibbon()
        {
            var panel = Application.CreatePanel(
                "Circuit Manager", "DB Circuits");

            panel.AddPushButton<StartupCommand>("Open\nCircuits");
        }

        private void RegisterDockablePane()
        {
            PaneInstance = new CircuitManagerPane();

            Application.RegisterDockablePane(
                PaneId,
                "DB Circuit Manager",
                PaneInstance);
        }
    }
}
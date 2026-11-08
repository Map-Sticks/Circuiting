using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI.Events;
using Circuiting.Commands;
using Circuiting.UI.DockablePanel;
using Circuiting.Services;
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
        private readonly Dictionary<int, CircuitManagerPane.ClosingDocumentState> _closingDocuments =
            new Dictionary<int, CircuitManagerPane.ClosingDocumentState>();

        public override void OnStartup()
        {
            EventHandler = new RevitEventHandler();
            RevitEvent = Autodesk.Revit.UI.ExternalEvent.Create(EventHandler);

            CreateRibbon();
            RegisterDockablePane();
            Application.ViewActivated += OnViewActivated;
            Application.ControlledApplication.DocumentChanged += OnDocumentChanged;
            Application.ControlledApplication.DocumentClosing += OnDocumentClosing;
            Application.ControlledApplication.DocumentClosed += OnDocumentClosed;
        }

        public override void OnShutdown()
        {
            Application.ViewActivated -= OnViewActivated;
            Application.ControlledApplication.DocumentChanged -= OnDocumentChanged;
            Application.ControlledApplication.DocumentClosing -= OnDocumentClosing;
            Application.ControlledApplication.DocumentClosed -= OnDocumentClosed;
            RevitEvent.Dispose();
        }

        private void OnViewActivated(object? sender, ViewActivatedEventArgs args)
        {
            if (args.Status == RevitAPIEventStatus.Succeeded && PaneInstance?.HasOpened == true)
                PaneInstance.OpenDocument(args.Document);
        }

        private void OnDocumentChanged(object? sender, DocumentChangedEventArgs args)
        {
            if (EventHandler.IsExecuting || PaneInstance?.HasOpened != true) return;
            var document = args.GetDocument();
            if (!PaneInstance.IsShowing(document)) return;
            PaneInstance.HandleModelChanged(document,
                args.GetAddedElementIds().Select(RevitElementIds.GetValue).ToArray(),
                args.GetDeletedElementIds().Select(RevitElementIds.GetValue).ToArray(),
                args.Operation == UndoOperation.TransactionCommitted);
        }

        private void OnDocumentClosing(object? sender, DocumentClosingEventArgs args)
        {
            if (PaneInstance?.HasOpened == true)
                _closingDocuments[args.DocumentId] = PaneInstance.PrepareDocumentClose(args.Document);
        }

        private void OnDocumentClosed(object? sender, DocumentClosedEventArgs args)
        {
            if (!_closingDocuments.TryGetValue(args.DocumentId, out CircuitManagerPane.ClosingDocumentState? state))
                return;
            _closingDocuments.Remove(args.DocumentId);
            PaneInstance?.CompleteDocumentClose(state, args.Status == RevitAPIEventStatus.Succeeded);
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
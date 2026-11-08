using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Circuiting.Models;
using Circuiting.Storage;
using Nice3point.Revit.Toolkit.External;

namespace Circuiting.Commands
{
    [UsedImplicitly]
    [Transaction(TransactionMode.Manual)]
    public class StartupCommand : ExternalCommand
    {
        public override void Execute()
        {
            bool shiftHeld =
                System.Windows.Input.Keyboard.IsKeyDown(
                    System.Windows.Input.Key.LeftShift) ||
                System.Windows.Input.Keyboard.IsKeyDown(
                    System.Windows.Input.Key.RightShift);

            if (shiftHeld)
            {
                ShowClearDialog();
                return;
            }

            // Load data from ES and send to panel
            Document doc = ExternalCommandData.Application
                .ActiveUIDocument.Document;

            List<BoardData> boards =
                BoardRepository.LoadAllBoards(doc);

            if (Circuiting.Application.PaneInstance != null)
            {
                Circuiting.Application.PaneInstance
                    .SetBoards(boards);
            }

            // Show the panel
            DockablePane pane =
                Circuiting.Application.PaneInstance != null
                ? new UIApplication(
                    ExternalCommandData.Application.Application)
                    .GetDockablePane(
                        Circuiting.Application.PaneId)
                : null!;

            if (pane != null)
            {
                pane.Show();
            }
        }

        private void ShowClearDialog()
        {
            System.Windows.MessageBoxResult clearChoice =
                System.Windows.MessageBox.Show(
                    "Clear circuit data from this project?\n\n" +
                    "YES = Clear ALL boards, chassis and circuits\n" +
                    "NO = Cancel",
                    "🔧 Clear ES Data (Hidden Tool)",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);

            if (clearChoice != System.Windows.MessageBoxResult.Yes)
                return;

            // Second confirmation
            System.Windows.MessageBoxResult confirm =
                System.Windows.MessageBox.Show(
                    "Are you sure? This cannot be undone.\n\n" +
                    "All board, chassis and circuit data " +
                    "will be permanently deleted from the model.",
                    "Confirm Clear",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Stop);

            if (confirm != System.Windows.MessageBoxResult.Yes)
                return;

            Document doc = ExternalCommandData.Application
                .ActiveUIDocument.Document;

            using (Transaction t = new Transaction(doc,
                "Clear All Circuit Data"))
            {
                t.Start();
                BoardRepository.SaveAllBoards(doc,
                    new List<BoardData>());
                t.Commit();
            }

            // Clear the panel too
            if (Circuiting.Application.PaneInstance != null)
            {
                Circuiting.Application.PaneInstance
                    .SetBoards(new List<BoardData>());
            }

            TaskDialog.Show("DB Circuit Manager",
                "All circuit data has been cleared.");
        }
    }
}
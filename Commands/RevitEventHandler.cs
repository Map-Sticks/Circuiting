using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Circuiting.Models;
using Circuiting.Storage;

namespace Circuiting.Commands
{
    public enum RevitAction
    {
        Save,
        ClearAll,
        ClearCircuitsOnly,
        ClearChassisAndCircuits,
        AssignFixtures,
        SelectFixtures,
        RemoveFixtures
    }

    public class AssignFixturesRequest
    {
        public Guid BoardGuid { get; set; }
        public Guid ChassisGuid { get; set; }
        public string CircuitDisplayLabel { get; set; }
            = string.Empty;
    }

    public class RevitEventHandler : IExternalEventHandler
    {
        public RevitAction CurrentAction { get; set; }
        public List<BoardData>? BoardsToSave { get; set; }
        public AssignFixturesRequest? AssignRequest { get; set; }
        public bool LastActionSucceeded { get; private set; }
        public string LastMessage { get; private set; }
            = string.Empty;

        public void Execute(UIApplication app)
        {
            try
            {
                UIDocument? uidoc = app.ActiveUIDocument;
                if (uidoc == null)
                {
                    LastActionSucceeded = false;
                    LastMessage = "No document open.";
                    return;
                }

                Document doc = uidoc.Document;

                switch (CurrentAction)
                {
                    case RevitAction.Save:
                        ExecuteSave(doc);
                        break;
                    case RevitAction.ClearAll:
                        ExecuteClear(doc, true, true, true);
                        break;
                    case RevitAction.ClearCircuitsOnly:
                        ExecuteClear(doc, false, true, false);
                        break;
                    case RevitAction.ClearChassisAndCircuits:
                        ExecuteClear(doc, true, true, false);
                        break;
                    case RevitAction.AssignFixtures:
                        ExecuteAssignFixtures(app);
                        break;
                    case RevitAction.SelectFixtures:
                        ExecuteSelectFixtures(app);
                        break;
                    case RevitAction.RemoveFixtures:
                        ExecuteRemoveFixtures(app);
                        break;
                }
            }
            catch (System.Exception ex)
            {
                LastActionSucceeded = false;
                LastMessage = ex.Message;
            }
        }

        private void ExecuteSave(Document doc)
        {
            if (BoardsToSave == null) return;

            using (Transaction t = new Transaction(doc,
                "Save Circuit Data"))
            {
                t.Start();
                BoardRepository.SaveAllBoards(doc, BoardsToSave);
                t.Commit();
            }

            LastActionSucceeded = true;
            LastMessage = $"Saved {BoardsToSave.Count} board(s).";
        }

        private void ExecuteClear(Document doc,
            bool clearChassis, bool clearCircuits,
            bool clearBoards)
        {
            List<BoardData> boards =
                BoardRepository.LoadAllBoards(doc);

            if (clearBoards) boards.Clear();
            else if (clearChassis)
                foreach (BoardData b in boards) b.Chassis.Clear();
            else if (clearCircuits)
                foreach (BoardData b in boards)
                    foreach (ChassisData c in b.Chassis)
                        c.Circuits.Clear();

            using (Transaction t = new Transaction(doc,
                "Clear Circuit Data"))
            {
                t.Start();
                BoardRepository.SaveAllBoards(doc, boards);
                t.Commit();
            }

            LastActionSucceeded = true;
            LastMessage = "Data cleared.";
        }

        // ─── Assign Fixtures ─────────────────────────────────

        private CircuitData? FindTargetCircuit()
        {
            if (AssignRequest == null || BoardsToSave == null)
                return null;

            return BoardsToSave
                .FirstOrDefault(b =>
                    b.BoardGuid == AssignRequest.BoardGuid)
                ?.Chassis.FirstOrDefault(c =>
                    c.ChassisGuid == AssignRequest.ChassisGuid)
                ?.Circuits.FirstOrDefault(c =>
                    c.DisplayLabel ==
                    AssignRequest.CircuitDisplayLabel);
        }

        private BoardData? FindTargetBoard()
        {
            return BoardsToSave?.FirstOrDefault(b =>
                b.BoardGuid == AssignRequest?.BoardGuid);
        }

        private ChassisData? FindTargetChassis()
        {
            return FindTargetBoard()?.Chassis.FirstOrDefault(c =>
                c.ChassisGuid == AssignRequest?.ChassisGuid);
        }

        private void RemoveFixtureFromAllCircuits(
            long elementId)
        {
            if (BoardsToSave == null) return;

            foreach (BoardData board in BoardsToSave)
                foreach (ChassisData chassis in board.Chassis)
                    foreach (CircuitData circuit in chassis.Circuits)
                        circuit.Fixtures.RemoveAll(f =>
                            f.RevitElementId == elementId);
        }

        private void ExecuteAssignFixtures(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            Document doc = uidoc.Document;
            Autodesk.Revit.DB.View activeView =
                uidoc.ActiveView;

            CircuitData? circuit = FindTargetCircuit();
            BoardData? board = FindTargetBoard();
            ChassisData? chassis = FindTargetChassis();

            if (circuit == null || board == null
                || chassis == null)
            {
                LastMessage = "Circuit not found.";
                return;
            }

            // Apply halftone to all fixtures, bright for
            // already-assigned ones
            List<ElementId> overriddenIds = new List<ElementId>();

            using (Transaction tOverride = new Transaction(doc,
                "Apply Selection View"))
            {
                tOverride.Start();

                OverrideGraphicSettings halftone =
                    new OverrideGraphicSettings();
                halftone.SetHalftone(true);

                BuiltInCategory[] categories =
                {
                    BuiltInCategory.OST_LightingFixtures,
                    BuiltInCategory.OST_ElectricalFixtures,
                    BuiltInCategory.OST_ElectricalEquipment
                };

                foreach (BuiltInCategory cat in categories)
                {
                    FilteredElementCollector collector =
                        new FilteredElementCollector(
                            doc, activeView.Id)
                        .OfCategory(cat)
                        .WhereElementIsNotElementType();

                    foreach (Element el in collector)
                    {
                        activeView.SetElementOverrides(
                            el.Id, halftone);
                        overriddenIds.Add(el.Id);
                    }
                }

                // Bright green for existing circuit fixtures
                OverrideGraphicSettings bright =
                    new OverrideGraphicSettings();
                bright.SetHalftone(false);
                bright.SetProjectionLineColor(
                    new Autodesk.Revit.DB.Color(0, 200, 0));

                foreach (CircuitedFixture fix in
                    circuit.Fixtures)
                {
                    ElementId fixId =
                        new ElementId(fix.RevitElementId);
                    if (doc.GetElement(fixId) != null)
                    {
                        activeView.SetElementOverrides(
                            fixId, bright);
                    }
                }

                tOverride.Commit();
            }

            try
            {
                IList<Reference> refs = uidoc.Selection
                    .PickObjects(
                        ObjectType.Element,
                        new FixtureSelectionFilter(),
                        $"Select fixtures for " +
                        $"{circuit.DisplayLabel} — " +
                        $"Escape when done");

                int added = 0;
                int moved = 0;
                int skipped = 0;

                using (Transaction t = new Transaction(doc,
                    "Assign Fixtures"))
                {
                    t.Start();

                    // Clear overrides
                    ClearOverrides(activeView, overriddenIds);

                    foreach (Reference r in refs)
                    {
                        Element el = doc.GetElement(r);
                        if (el == null) continue;

                        long elId = el.Id.Value;

                        // Already in this circuit?
                        if (circuit.Fixtures.Any(f =>
                            f.RevitElementId == elId))
                        {
                            skipped++;
                            continue;
                        }

                        // In another circuit?
                        if (BoardRepository
                            .IsFixtureCircuited(el))
                        {
                            string existingRef =
                                BoardRepository
                                .GetFixtureCircuitReference(
                                    el) ?? "unknown";

                            TaskDialog td = new TaskDialog(
                                "Fixture Already Assigned");
                            td.MainInstruction =
                                $"This fixture is assigned " +
                                $"to circuit {existingRef}.";
                            td.AddCommandLink(
                                TaskDialogCommandLinkId
                                    .CommandLink1,
                                "Move to this circuit");
                            td.AddCommandLink(
                                TaskDialogCommandLinkId
                                    .CommandLink2,
                                "Skip this fixture");

                            TaskDialogResult tdResult =
                                td.Show();

                            if (tdResult ==
                                TaskDialogResult.CommandLink2)
                            {
                                skipped++;
                                continue;
                            }

                            // Remove from old circuit
                            RemoveFixtureFromAllCircuits(elId);
                            moved++;
                        }
                        else
                        {
                            added++;
                        }

                        Guid fixtureGuid = Guid.NewGuid();
                        string familyName = el.Name;
                        string roomName = string.Empty;

                        if (el is FamilyInstance fi
                            && fi.Room != null)
                            roomName = fi.Room.Name;

                        circuit.Fixtures.Add(
                            new CircuitedFixture
                            {
                                FixtureGuid = fixtureGuid,
                                RevitElementId = elId,
                                FamilyName = familyName,
                                RoomName = roomName
                            });

                        BoardRepository.SaveFixtureAssignment(
                            el, fixtureGuid, board.BoardGuid,
                            chassis.ChassisGuid,
                            circuit.CircuitGuid,
                            circuit.DisplayLabel);
                    }

                    BoardRepository.SaveAllBoards(
                        doc, BoardsToSave);
                    t.Commit();
                }

                LastActionSucceeded = true;
                LastMessage =
                    $"Added {added}, moved {moved}, " +
                    $"skipped {skipped} fixture(s).";
            }
            catch (Autodesk.Revit.Exceptions
                .OperationCanceledException)
            {
                // Clear overrides on cancel
                using (Transaction tClear = new Transaction(
                    doc, "Clear Overrides"))
                {
                    tClear.Start();
                    ClearOverrides(activeView, overriddenIds);
                    tClear.Commit();
                }

                LastActionSucceeded = true;
                LastMessage = "Selection cancelled.";
            }

            RefreshPanel();
        }

        // ─── Select Fixtures ─────────────────────────────────

        private void ExecuteSelectFixtures(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;

            CircuitData? circuit = FindTargetCircuit();
            if (circuit == null || circuit.Fixtures.Count == 0)
            {
                LastMessage = "No fixtures assigned.";
                return;
            }

            List<ElementId> ids = circuit.Fixtures
                .Select(f => new ElementId(f.RevitElementId))
                .Where(id =>
                    uidoc.Document.GetElement(id) != null)
                .ToList();

            if (ids.Count > 0)
            {
                uidoc.Selection.SetElementIds(ids);
                uidoc.ShowElements(ids);
                LastMessage =
                    $"Selected {ids.Count} fixture(s).";
            }

            LastActionSucceeded = true;
        }

        // ─── Remove Fixtures (pick mode) ─────────────────────

        private void ExecuteRemoveFixtures(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            Document doc = uidoc.Document;

            CircuitData? circuit = FindTargetCircuit();
            BoardData? board = FindTargetBoard();

            if (circuit == null || circuit.Fixtures.Count == 0)
            {
                LastMessage = "No fixtures to remove.";
                return;
            }

            // Only allow picking fixtures in this circuit
            List<long> circuitFixtureIds = circuit.Fixtures
                .Select(f => f.RevitElementId).ToList();

            try
            {
                IList<Reference> refs = uidoc.Selection
                    .PickObjects(
                        ObjectType.Element,
                        new CircuitFixtureFilter(
                            circuitFixtureIds),
                        $"Pick fixtures to remove from " +
                        $"{circuit.DisplayLabel} — " +
                        $"Escape when done");

                if (refs.Count == 0)
                {
                    LastMessage = "No fixtures selected.";
                    return;
                }

                int removed = 0;

                using (Transaction t = new Transaction(doc,
                    "Remove Fixtures from Circuit"))
                {
                    t.Start();

                    foreach (Reference r in refs)
                    {
                        Element el = doc.GetElement(r);
                        if (el == null) continue;

                        long elId = el.Id.Value;

                        CircuitedFixture? fix =
                            circuit.Fixtures.FirstOrDefault(
                                f => f.RevitElementId == elId);
                        if (fix != null)
                        {
                            circuit.Fixtures.Remove(fix);
                            BoardRepository
                                .ClearFixtureAssignment(el);
                            removed++;
                        }
                    }

                    BoardRepository.SaveAllBoards(
                        doc, BoardsToSave);
                    t.Commit();
                }

                LastActionSucceeded = true;
                LastMessage =
                    $"Removed {removed} fixture(s) from " +
                    $"{circuit.DisplayLabel}.";
            }
            catch (Autodesk.Revit.Exceptions
                .OperationCanceledException)
            {
                LastMessage = "Cancelled.";
                LastActionSucceeded = true;
            }

            RefreshPanel();
        }

        // ─── Helpers ─────────────────────────────────────────

        private void ClearOverrides(
            Autodesk.Revit.DB.View view,
            List<ElementId> ids)
        {
            OverrideGraphicSettings clear =
                new OverrideGraphicSettings();
            foreach (ElementId id in ids)
            {
                view.SetElementOverrides(id, clear);
            }
        }

        private void RefreshPanel()
        {
            Circuiting.Application.PaneInstance?
                .Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        Circuiting.Application.PaneInstance?
                            .RefreshTree();
                        Circuiting.Application.PaneInstance?
                            .UpdateStatus(LastMessage);
                    }));
        }

        public string GetName()
        {
            return "DB Circuit Manager Event Handler";
        }
    }
}
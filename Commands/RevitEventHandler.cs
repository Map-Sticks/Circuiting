using System.Diagnostics;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Circuiting.Models;
using Circuiting.Services;
using Circuiting.Storage;

namespace Circuiting.Commands
{
    public enum RevitAction
    {
        Save,
        Reload,
        Validate,
        ResolveChanges,
        AssignFixtures,
        SelectFixtures,
        RemoveFixtures,
        ZoomToFixture,
        SelectFixture
    }

    public sealed class RevitRequest
    {
        public RevitRequest(RevitAction action, Document document, string expectedJson, string desiredJson,
            Action<RevitResult> completed, Guid boardGuid = default, Guid chassisGuid = default,
            Guid circuitGuid = default, IEnumerable<long>? addedIds = null, IEnumerable<long>? deletedIds = null,
            Guid fixtureGuid = default)
        {
            FixtureGuid = fixtureGuid;
            Action = action;
            Document = document;
            ExpectedJson = expectedJson;
            DesiredJson = desiredJson;
            Completed = completed;
            BoardGuid = boardGuid;
            ChassisGuid = chassisGuid;
            CircuitGuid = circuitGuid;
            AddedIds = addedIds?.ToArray() ?? Array.Empty<long>();
            DeletedIds = deletedIds?.ToArray() ?? Array.Empty<long>();
        }

        public RevitAction Action { get; }
        public Document Document { get; }
        public string ExpectedJson { get; }
        public string DesiredJson { get; }
        public Guid BoardGuid { get; }
        public Guid ChassisGuid { get; }
        public Guid CircuitGuid { get; }
        public Guid FixtureGuid { get; }
        public IReadOnlyList<long> AddedIds { get; }
        public IReadOnlyList<long> DeletedIds { get; }
        public Action<RevitResult> Completed { get; }
    }

    public sealed class RevitResult
    {
        public bool Success { get; set; }
        public bool Changed { get; set; }
        public string Message { get; set; } = string.Empty;
        public ProjectData? Project { get; set; }
    }

    public sealed class RevitEventHandler : IExternalEventHandler
    {
        private readonly object _gate = new object();
        private readonly List<RevitRequest> _requests = new List<RevitRequest>();

        public bool IsExecuting { get; private set; }

        public void Enqueue(RevitRequest request, ExternalEvent externalEvent)
        {
            lock (_gate)
            {
                _requests.Add(request);
                ExternalEventRequest outcome;
                try
                {
                    outcome = externalEvent.Raise();
                }
                catch
                {
                    _requests.Remove(request);
                    throw;
                }

                if (outcome != ExternalEventRequest.Accepted && outcome != ExternalEventRequest.Pending)
                {
                    _requests.Remove(request);
                    throw new InvalidOperationException($"Revit could not accept the request ({outcome}).");
                }
            }
        }

        public void Execute(UIApplication app)
        {
            while (true)
            {
                RevitRequest request;
                lock (_gate)
                {
                    if (_requests.Count == 0) return;
                    request = _requests[0];
                    _requests.RemoveAt(0);
                }

                RevitResult result;
                IsExecuting = true;
                try
                {
                    result = ExecuteRequest(app, request);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    result = new RevitResult { Success = true, Message = "Selection cancelled." };
                }
                catch (Exception ex)
                {
                    Trace.TraceError("Circuit manager request failed: {0}", ex);
                    result = new RevitResult { Success = false, Message = ex.Message };
                }
                finally
                {
                    IsExecuting = false;
                }

                try
                {
                    request.Completed(result);
                }
                catch (Exception ex)
                {
                    Trace.TraceError("Circuit manager could not report a request result: {0}", ex);
                    TaskDialog.Show("DB Circuit Manager", $"Could not update the circuit panel: {ex.Message}");
                }
            }
        }

        private static RevitResult ExecuteRequest(UIApplication app, RevitRequest request)
        {
            UIDocument? uidoc = app.ActiveUIDocument;
            if (uidoc == null || !request.Document.IsValidObject ||
                !request.Document.Equals(uidoc.Document))
                throw new InvalidOperationException("The active Revit document changed. Reopen the circuit panel.");

            Document doc = uidoc.Document;
            ProjectData previous = BoardRepository.LoadProjectData(doc);
            if (request.Action == RevitAction.Reload)
                return new RevitResult
                {
                    Success = true, Changed = true, Project = previous, Message = "Circuit data refreshed."
                };
            if (!string.Equals(ProjectDataSerializer.Serialize(previous), request.ExpectedJson, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Circuit data changed in Revit since the panel was loaded. Refresh the panel and try again.");
            ProjectData desired = ProjectDataSerializer.Deserialize(request.DesiredJson);

            switch (request.Action)
            {
                case RevitAction.Save:
                    return Save(doc, previous, desired, "Save Circuit Data", "Circuit data saved.");
                case RevitAction.AssignFixtures:
                    return AssignFixtures(uidoc, previous, desired, request);
                case RevitAction.Validate:
                    return ValidateFixtures(doc, desired);
                case RevitAction.ResolveChanges:
                    return ResolveChanges(doc, desired, request);
                case RevitAction.SelectFixtures:
                    return SelectFixtures(uidoc, desired, request);
                case RevitAction.RemoveFixtures:
                    return RemoveFixtures(uidoc, previous, desired, request);
                case RevitAction.ZoomToFixture:
                    return SelectFixture(uidoc, desired, request, zoom: true);
                case RevitAction.SelectFixture:
                    return SelectFixture(uidoc, desired, request, zoom: false);
                default:
                    throw new InvalidOperationException($"Unsupported circuit action: {request.Action}.");
            }
        }

        private static RevitResult Save(Document doc, ProjectData previous, ProjectData desired,
            string transactionName, string message)
        {
            int missing;
            using (Transaction transaction = new Transaction(doc, transactionName))
            {
                if (transaction.Start() != TransactionStatus.Started)
                    throw new InvalidOperationException($"Could not start '{transactionName}'.");
                missing = FixtureAssignmentSynchronizer.Synchronize(
                    previous, desired, new RevitFixtureAssignmentStore(doc));
                BoardRepository.SaveProjectData(doc, desired);
                TransactionStatus status = transaction.Commit();
                if (status != TransactionStatus.Committed)
                    throw new InvalidOperationException(
                        $"Revit did not commit '{transactionName}' ({status}). Reopen the panel after resolving failures.");
            }

            if (missing > 0)
                message += $" {missing} referenced fixture(s) are missing; validate the model.";
            return new RevitResult { Success = true, Changed = true, Message = message, Project = desired };
        }

        private static (BoardData Board, ChassisData Chassis, CircuitData Circuit) FindTarget(
            ProjectData project, RevitRequest request)
        {
            BoardData board = project.Boards.FirstOrDefault(b => b.BoardGuid == request.BoardGuid)
                ?? throw new InvalidOperationException("The selected board no longer exists.");
            ChassisData chassis = board.Chassis.FirstOrDefault(c => c.ChassisGuid == request.ChassisGuid)
                ?? throw new InvalidOperationException("The selected chassis no longer exists.");
            CircuitData circuit = chassis.Circuits.FirstOrDefault(c => c.CircuitGuid == request.CircuitGuid)
                ?? throw new InvalidOperationException("The selected circuit no longer exists.");
            return (board, chassis, circuit);
        }

        private static RevitResult ValidateFixtures(Document doc, ProjectData desired)
        {
            FixtureValidationPlan plan = FixtureModelValidator.Analyze(
                desired, new RevitFixtureAssignmentStore(doc));
            if (!plan.HasChanges)
                return new RevitResult { Success = true, Message = "No fixture inconsistencies found." };

            var dialog = new TaskDialog("Validate Circuit Fixtures")
            {
                MainInstruction = "Apply the proposed fixture repairs?",
                MainContent = plan.Describe() +
                    "\n\nThe project circuit data is authoritative. Use Revit Undo to reverse this repair.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            if (dialog.Show() != TaskDialogResult.Yes)
                return new RevitResult { Success = true, Message = "Fixture validation cancelled; no changes made." };

            return CommitValidationPlan(doc, plan, "Validate Circuit Fixtures",
                "Fixture validation completed. Use Revit Undo to reverse the repair.");
        }

        private static RevitResult ResolveChanges(Document doc, ProjectData desired, RevitRequest request)
        {
            if (desired.Settings.ResolutionMode != ResolutionMode.Automatic)
                return new RevitResult { Success = true, Message = "Automatic resolution is disabled." };
            FixtureValidationPlan plan = FixtureModelValidator.AnalyzeChanges(
                desired, new RevitFixtureAssignmentStore(doc), request.AddedIds, request.DeletedIds);
            if (!plan.HasChanges)
                return new RevitResult
                {
                    Success = true,
                    Message = "Model changed. Validate the model to review any unresolved assignments."
                };
            RevitResult result = CommitValidationPlan(doc, plan, "Resolve Circuit Fixture Changes",
                $"Automatic fixture resolution: {plan.MissingFixtures} deleted, " +
                $"{plan.CopiedFixturesCleared} copied assignment(s) cleared, " +
                $"{plan.CopiedFixturesAssigned} copied fixture(s) assigned. Use Revit Undo to reverse.");
            AutomaticResolutionRules rules = desired.Settings.AutoRules;
            if (rules.LogAutomaticActions)
                Trace.TraceInformation(result.Message);
            if ((plan.MissingFixtures > 0 && rules.OnFixtureDeleted == DeletedFixtureAction.RemoveAndNotify) ||
                (plan.CopiedFixturesCleared > 0 && rules.OnFixtureCopied == CopiedFixtureAction.ClearAndNotify))
                TaskDialog.Show("Circuit Fixture Changes", result.Message);
            return result;
        }

        private static RevitResult CommitValidationPlan(
            Document doc, FixtureValidationPlan plan, string transactionName, string message)
        {
            using (Transaction transaction = new Transaction(doc, transactionName))
            {
                if (transaction.Start() != TransactionStatus.Started)
                    throw new InvalidOperationException($"Could not start '{transactionName}'.");
                plan.Apply();
                BoardRepository.SaveProjectData(doc, plan.Project);
                TransactionStatus status = transaction.Commit();
                if (status != TransactionStatus.Committed)
                    throw new InvalidOperationException(
                        $"Revit did not commit '{transactionName}' ({status}). Reopen the panel after resolving failures.");
            }
            return new RevitResult
            {
                Success = true, Changed = true, Project = plan.Project, Message = message
            };
        }

        private static RevitResult AssignFixtures(
            UIDocument uidoc, ProjectData previous, ProjectData desired, RevitRequest request)
        {
            var target = FindTarget(desired, request);
            IList<Reference> references = PickFixturesWithHighlights(uidoc, target.Circuit);
            int added = 0;
            int moved = 0;
            int skipped = 0;
            var visited = new HashSet<string>(StringComparer.Ordinal);

            foreach (Reference reference in references)
            {
                Element? element = uidoc.Document.GetElement(reference);
                if (element == null || !visited.Add(element.UniqueId)) continue;
                long elementId = RevitElementIds.GetValue(element.Id);
                var existing = FindFixture(desired, element.UniqueId, elementId);
                FixtureAssignment? stored = BoardRepository.GetFixtureAssignment(element);
                if (existing.HasValue && string.IsNullOrEmpty(existing.Value.Fixture.RevitUniqueId))
                    FixtureIdentity.RequireLegacyAssignment(existing.Value.Fixture, stored);
                if (existing.HasValue && stored != null &&
                    existing.Value.Fixture.FixtureGuid != stored.FixtureGuid)
                    throw new InvalidDataException(
                        $"Fixture '{element.UniqueId}' has conflicting assignments. Validate the model first.");
                if (existing.HasValue && existing.Value.Circuit.CircuitGuid == target.Circuit.CircuitGuid)
                {
                    var expected = new FixtureAssignment
                    {
                        FixtureGuid = existing.Value.Fixture.FixtureGuid,
                        BoardGuid = target.Board.BoardGuid,
                        ChassisGuid = target.Chassis.ChassisGuid,
                        CircuitGuid = target.Circuit.CircuitGuid
                    };
                    if (stored == null || !FixtureIdentity.MatchesAssignment(stored, expected) ||
                        stored.CircuitReference != target.Circuit.DisplayLabel)
                        throw new InvalidDataException(
                            $"Fixture '{element.UniqueId}' has inconsistent assignment data. Validate the model first.");
                    skipped++;
                    continue;
                }
                if (!existing.HasValue && stored != null)
                {
                    string issue = FindFixtureByGuid(desired, stored.FixtureGuid) != null
                        ? "is a copy of an assigned fixture"
                        : "has an assignment missing from the project data";
                    throw new InvalidDataException(
                        $"Fixture '{element.UniqueId}' {issue}. Validate the model before assigning it.");
                }

                if (existing.HasValue)
                {
                    string referenceLabel = existing.Value.Circuit.DisplayLabel;
                    TaskDialog dialog = new TaskDialog("Fixture Already Assigned")
                    {
                        MainInstruction = $"This fixture is assigned to circuit {referenceLabel}."
                    };
                    dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Move to this circuit");
                    dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Skip this fixture");
                    if (dialog.Show() != TaskDialogResult.CommandLink1)
                    {
                        skipped++;
                        continue;
                    }
                    moved++;
                }
                else
                {
                    added++;
                }

                Guid fixtureGuid = existing?.Fixture.FixtureGuid ?? Guid.NewGuid();
                if (existing.HasValue)
                    existing.Value.Circuit.Fixtures.Remove(existing.Value.Fixture);
                string roomName = element is FamilyInstance family && family.Room != null
                    ? family.Room.Name : string.Empty;
                target.Circuit.Fixtures.Add(new CircuitedFixture
                {
                    FixtureGuid = fixtureGuid,
                    RevitElementId = elementId,
                    RevitUniqueId = element.UniqueId,
                    FamilyName = element.Name,
                    RoomName = roomName
                });
            }

            if (added + moved == 0)
                return new RevitResult { Success = true, Message = $"No fixtures changed; {skipped} skipped." };
            return Save(uidoc.Document, previous, desired, "Assign Fixtures",
                $"Added {added}, moved {moved}, skipped {skipped} fixture(s).");
        }

        private static IList<Reference> PickFixturesWithHighlights(UIDocument uidoc, CircuitData circuit)
        {
            Document doc = uidoc.Document;
            Autodesk.Revit.DB.View view = uidoc.ActiveView;
            if (!FilteredElementCollector.IsViewValidForElementIteration(doc, view.Id) ||
                !view.AreGraphicsOverridesAllowed())
                throw new InvalidOperationException(
                    "Open a model view that supports visibility/graphics overrides before assigning fixtures.");

            var assignedIds = new HashSet<ElementId>(circuit.Fixtures
                .Select(f => RevitElementIds.Resolve(doc, f))
                .Where(e => e != null)
                .Select(e => e!.Id));
            BuiltInCategory[] categories =
            {
                BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_ElectricalFixtures,
                BuiltInCategory.OST_ElectricalEquipment
            };

            using (TransactionGroup group = new TransactionGroup(doc, "Temporary Fixture Highlights"))
            {
                if (group.Start() != TransactionStatus.Started)
                    throw new InvalidOperationException("Could not start temporary fixture highlighting.");
                try
                {
                    using (Transaction transaction = new Transaction(doc, "Highlight Fixtures"))
                    {
                        if (transaction.Start() != TransactionStatus.Started)
                            throw new InvalidOperationException("Could not highlight fixtures.");
                        using (var collector = new FilteredElementCollector(doc, view.Id))
                        {
                            foreach (Element element in collector
                                .WherePasses(new ElementMulticategoryFilter(categories))
                                .WhereElementIsNotElementType())
                            {
                                using (OverrideGraphicSettings original = view.GetElementOverrides(element.Id))
                                using (OverrideGraphicSettings highlight = new OverrideGraphicSettings(original))
                                {
                                    bool assigned = assignedIds.Contains(element.Id);
                                    highlight.SetHalftone(!assigned);
                                    if (assigned)
                                        highlight.SetProjectionLineColor(new Autodesk.Revit.DB.Color(0, 200, 0));
                                    view.SetElementOverrides(element.Id, highlight);
                                }
                            }
                        }
                        if (transaction.Commit() != TransactionStatus.Committed)
                            throw new InvalidOperationException("Could not commit temporary fixture highlighting.");
                    }

                    uidoc.RefreshActiveView();
                    return uidoc.Selection.PickObjects(ObjectType.Element, new FixtureSelectionFilter(),
                        $"Select fixtures for {circuit.DisplayLabel}, then click Finish; Esc cancels.");
                }
                finally
                {
                    if (group.GetStatus() == TransactionStatus.Started &&
                        group.RollBack() != TransactionStatus.RolledBack)
                        throw new InvalidOperationException("Could not restore the view's original graphics overrides.");
                    uidoc.RefreshActiveView();
                }
            }
        }

        private static RevitResult SelectFixtures(UIDocument uidoc, ProjectData project, RevitRequest request)
        {
            CircuitData circuit = FindTarget(project, request).Circuit;
            List<ElementId> ids = circuit.Fixtures.Select(f => RevitElementIds.Resolve(uidoc.Document, f))
                .Where(e => e != null).Select(e => e!.Id).ToList();
            if (ids.Count == 0)
                return new RevitResult { Success = true, Message = "No existing fixtures to select." };

            uidoc.Selection.SetElementIds(ids);
            uidoc.ShowElements(ids);
            return new RevitResult { Success = true, Message = $"Selected {ids.Count} fixture(s)." };
        }

        private static RevitResult SelectFixture(UIDocument uidoc, ProjectData project, RevitRequest request, bool zoom)
        {
            CircuitData circuit = FindTarget(project, request).Circuit;
            CircuitedFixture fixture = circuit.Fixtures.FirstOrDefault(f => f.FixtureGuid == request.FixtureGuid)
                ?? throw new InvalidOperationException("The selected element is no longer assigned to this circuit.");
            Element? element = RevitElementIds.Resolve(uidoc.Document, fixture);
            if (element == null)
                return new RevitResult
                {
                    Success = true,
                    Message = $"The element is not in the model. Run Validate Model to repair {circuit.DisplayLabel}."
                };

            var ids = new List<ElementId> { element.Id };
            uidoc.Selection.SetElementIds(ids);
            if (zoom) uidoc.ShowElements(ids);
            return new RevitResult
            {
                Success = true,
                Message = $"{(zoom ? "Zoomed to" : "Selected")} {element.Name} ({circuit.DisplayLabel})."
            };
        }

        private static RevitResult RemoveFixtures(
            UIDocument uidoc, ProjectData previous, ProjectData desired, RevitRequest request)
        {
            CircuitData circuit = FindTarget(desired, request).Circuit;
            var ids = circuit.Fixtures.Select(f => RevitElementIds.Resolve(uidoc.Document, f))
                .Where(e => e != null).Select(e => RevitElementIds.GetValue(e!.Id)).ToList();
            if (ids.Count == 0)
                return new RevitResult { Success = true, Message = "No existing fixtures to remove; validate the model." };

            IList<Reference> references = uidoc.Selection.PickObjects(
                ObjectType.Element, new CircuitFixtureFilter(ids),
                $"Select fixtures to remove from {circuit.DisplayLabel}, then click Finish; Esc cancels.");
            int removed = 0;
            foreach (Reference reference in references)
            {
                Element? element = uidoc.Document.GetElement(reference);
                if (element == null) continue;
                CircuitedFixture? fixture = circuit.Fixtures.FirstOrDefault(f =>
                    FixtureMatches(f, element.UniqueId, RevitElementIds.GetValue(element.Id)));
                if (fixture != null && circuit.Fixtures.Remove(fixture)) removed++;
            }

            if (removed == 0)
                return new RevitResult { Success = true, Message = "No fixtures removed." };
            return Save(uidoc.Document, previous, desired, "Remove Fixtures",
                $"Removed {removed} fixture(s) from {circuit.DisplayLabel}.");
        }

        private static (CircuitData Circuit, CircuitedFixture Fixture)? FindFixture(
            ProjectData project, string uniqueId, long elementId)
        {
            foreach (BoardData board in project.Boards)
            foreach (ChassisData chassis in board.Chassis)
            foreach (CircuitData circuit in chassis.Circuits)
            foreach (CircuitedFixture fixture in circuit.Fixtures)
                if (FixtureMatches(fixture, uniqueId, elementId))
                    return (circuit, fixture);
            return null;
        }

        private static CircuitedFixture? FindFixtureByGuid(ProjectData project, Guid fixtureGuid)
        {
            return project.Boards.SelectMany(b => b.Chassis)
                .SelectMany(c => c.Circuits)
                .SelectMany(c => c.Fixtures)
                .FirstOrDefault(f => f.FixtureGuid == fixtureGuid);
        }

        private static bool FixtureMatches(CircuitedFixture fixture, string uniqueId, long elementId)
        {
            return string.IsNullOrEmpty(fixture.RevitUniqueId)
                ? fixture.RevitElementId == elementId
                : fixture.RevitUniqueId == uniqueId;
        }

        public string GetName() => "DB Circuit Manager Event Handler";
    }
}

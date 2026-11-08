using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Circuiting.Commands;
using Circuiting.Models;
using Circuiting.Services;
using Circuiting.Storage;
using Circuiting.UI.Dialogs;
using Circuiting.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json;

namespace Circuiting.UI.DockablePanel
{
    public partial class CircuitManagerPane : UserControl, IDockablePaneProvider, ICircuitTreeActions
    {
        private Document? _document;
        private ProjectData _project = new ProjectData();
        private List<BoardData> _boards
        {
            get => _project.Boards;
            set => _project.Boards = value;
        }
        private readonly CircuitEditHistory _history = new CircuitEditHistory();
        private bool _dataLoaded = false;
        private bool _requestPending;
        private bool _applyingSettings;
        private string _committedJson = string.Empty;
        private readonly HashSet<long> _pendingAddedIds = new HashSet<long>();
        private readonly HashSet<long> _pendingDeletedIds = new HashSet<long>();
        private bool _pendingReload;
        private readonly DocumentSessionCache<Document> _documentStates =
            new DocumentSessionCache<Document>(document => document.IsValidObject);
        private List<BoardTreeNode> _treeNodes = new List<BoardTreeNode>();
        private Dictionary<Guid, FixtureDetails> _fixtureDetails = new Dictionary<Guid, FixtureDetails>();
        public bool HasOpened { get; private set; }

        private static readonly JsonSerializerSettings _jsonSettings =
            new JsonSerializerSettings
            {
                ObjectCreationHandling = ObjectCreationHandling.Replace
            };

        public CircuitManagerPane()
        {
            InitializeComponent();
            PreviewKeyDown += OnPreviewKeyDown;
            IsEnabled = false;
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = this;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Tabbed,
                TabBehind = DockablePanes.BuiltInDockablePanes.ProjectBrowser
            };
        }

        public bool OpenDocument(Document? document, bool forceReload = false)
        {
            if (document == null || !document.IsValidObject || document.IsFamilyDocument)
            {
                ClearDocument();
                StatusLabel.Text = "Open a Revit project to manage circuits.";
                return false;
            }
            if (!forceReload && _dataLoaded && _document != null &&
                _document.IsValidObject && document.Equals(_document))
                return true;

            try
            {
                if (_document != null && (!_document.IsValidObject || !document.Equals(_document)))
                {
                    _pendingAddedIds.Clear();
                    _pendingDeletedIds.Clear();
                    _pendingReload = false;
                }
                ProjectData project = BoardRepository.LoadProjectData(document);
                _fixtureDetails = ReadFixtureDetails(document, project);
                SetProject(document, project);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Could not load circuit data: {0}", ex);
                ClearDocument();
                StatusLabel.Text = "Circuit data could not be loaded.";
                MessageBox.Show(ex.Message, "Circuit Data Load Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public bool StartTracking(Document? document)
        {
            HasOpened = true;
            return OpenDocument(document, true);
        }

        private void SetProject(Document document, ProjectData project)
        {
            bool switched = _document == null || !_document.IsValidObject || !document.Equals(_document);
            if (switched) CacheCurrentDocument();
            string committedJson = ProjectDataSerializer.Serialize(project);
            DocumentSessionState? saved = switched ? _documentStates.Restore(document, committedJson) : null;
            if (switched) _history.Restore(saved);
            else _history.ClearIfChanged(_committedJson, committedJson);
            _document = document;
            _project = project;
            _committedJson = committedJson;
            _dataLoaded = true;
            ApplyResolutionMode();
            IsEnabled = !_requestPending;
            RefreshTree();
            if (saved != null) RestoreExpandedState(saved.Expanded);
        }

        private void CacheCurrentDocument()
        {
            if (_document == null || !_document.IsValidObject) return;
            if (!_dataLoaded || _requestPending)
            {
                _documentStates.Forget(_document);
                return;
            }
            _documentStates.Remember(_document, _committedJson, _history.UndoTopFirst,
                _history.RedoTopFirst, GetExpandedGuids());
        }

        private void ApplyResolutionMode()
        {
            _applyingSettings = true;
            try
            {
                ModeToggle.IsChecked = _project.Settings.ResolutionMode == ResolutionMode.Manual;
            }
            finally
            {
                _applyingSettings = false;
            }
        }

        public void ClearDocument()
        {
            CacheCurrentDocument();
            _document = null;
            _project = new ProjectData();
            _committedJson = string.Empty;
            _dataLoaded = false;
            _history.Clear();
            _pendingAddedIds.Clear();
            _pendingDeletedIds.Clear();
            _pendingReload = false;
            IsEnabled = false;
            BoardTree.ItemsSource = null;
            _treeNodes.Clear();
            _fixtureDetails = new Dictionary<Guid, FixtureDetails>();
            if (_expandedBeforeSearch != null) _expandedBeforeSearch = new HashSet<Guid>();
        }

        public sealed class ClosingDocumentState
        {
            internal DocumentSessionHandle<Document> Handle { get; set; } = null!;
            internal Document? ShowingDocument { get; set; }
        }

        public ClosingDocumentState PrepareDocumentClose(Document document)
        {
            return new ClosingDocumentState
            {
                Handle = _documentStates.TrackClosing(document),
                ShowingDocument = _document != null && _document.IsValidObject && _document.Equals(document)
                    ? _document : null
            };
        }

        public void CompleteDocumentClose(ClosingDocumentState state, bool succeeded)
        {
            _documentStates.CompleteClosing(state.Handle, succeeded);
            if (succeeded && state.ShowingDocument != null && ReferenceEquals(_document, state.ShowingDocument))
            {
                _document = null;
                ClearDocument();
            }
        }

        public bool IsShowing(Document document) =>
            _dataLoaded && _document != null && _document.IsValidObject && _document.Equals(document);

        private bool IsCurrentProject(ProjectData expected)
        {
            if (_dataLoaded && !_requestPending && ReferenceEquals(_project, expected)) return true;
            MessageBox.Show("The active document or its circuit data changed. The operation was cancelled.",
                "Circuit Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private bool IsCurrentTarget(BoardData? board, ChassisData? chassis = null, CircuitData? circuit = null)
        {
            bool valid = _dataLoaded && !_requestPending &&
                (board != null ? _boards.Contains(board) :
                    chassis != null && _boards.Any(b => b.Chassis.Contains(chassis)));
            if (valid && board != null && chassis != null)
                valid = board.Chassis.Contains(chassis);
            if (valid && circuit != null)
                valid = chassis != null && chassis.Circuits.Contains(circuit);
            if (valid) return true;
            MessageBox.Show("The selected circuit data is no longer current. Reopen the menu and try again.",
                "Circuit Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        public void HandleModelChanged(Document document, IEnumerable<long> addedIds,
            IEnumerable<long> deletedIds, bool transactionCommitted)
        {
            if (!IsShowing(document)) return;
            if (_requestPending)
            {
                if (transactionCommitted)
                {
                    _pendingAddedIds.UnionWith(addedIds);
                    _pendingDeletedIds.UnionWith(deletedIds);
                }
                else
                {
                    _pendingReload = true;
                }
                return;
            }

            if (!OpenDocument(document, true)) return;
            if (transactionCommitted && _project.Settings.ResolutionMode == ResolutionMode.Automatic)
            {
                long[] added = addedIds.ToArray();
                long[] deleted = deletedIds.ToArray();
                if (added.Length > 0 || deleted.Length > 0)
                {
                    QueueRequest(RevitAction.ResolveChanges, ProjectDataSerializer.Serialize(_project),
                        addedIds: added, deletedIds: deleted);
                    return;
                }
            }
            StatusLabel.Text = "Model changed. Validate fixture assignments if elements were copied or deleted.";
        }

        public void UpdateStatus(string message)
        {
            StatusLabel.Text = message;
        }
        // ─── Auto-Save + Undo/Redo ──────────────────────────

        private void PushUndoState()
        {
            _history.Push(ProjectDataSerializer.Serialize(_project));
        }

        private void SaveAndRefresh()
        {
            if (!_dataLoaded || _document == null) return;
            try
            {
                string desired = ProjectDataSerializer.Serialize(_project);
                RefreshTree();
                QueueRequest(RevitAction.Save, desired);
            }
            catch (Exception ex)
            {
                ReportRequestFailure(ex.Message);
            }
        }

        private void QueueRequest(RevitAction action, string desired, Guid boardGuid = default,
            Guid chassisGuid = default, Guid circuitGuid = default,
            IEnumerable<long>? addedIds = null, IEnumerable<long>? deletedIds = null, Guid fixtureGuid = default)
        {
            if (!_dataLoaded || _document == null || _requestPending)
                throw new InvalidOperationException("The circuit panel is not ready for another Revit request.");
            Document document = _document;
            _requestPending = true;
            IsEnabled = false;
            try
            {
                var request = new RevitRequest(action, document, _committedJson, desired,
                    result => CompleteRequest(document, action, result), boardGuid, chassisGuid, circuitGuid,
                    addedIds, deletedIds, fixtureGuid);
                Circuiting.Application.EventHandler.Enqueue(request, Circuiting.Application.RevitEvent);
                StatusLabel.Text = action == RevitAction.Save ? "Saving..." : "Waiting for Revit...";
            }
            catch (Exception ex)
            {
                _requestPending = false;
                ReportRequestFailure(ex.Message);
            }
        }

        private void CompleteRequest(Document document, RevitAction action, RevitResult result)
        {
            _requestPending = false;
            if (!IsShowing(document))
            {
                IsEnabled = _dataLoaded;
                StatusLabel.Text = result.Success ? result.Message : "Previous document request failed: " + result.Message;
                if (!result.Success)
                    MessageBox.Show(result.Message, "Circuit Manager Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                if (_dataLoaded) ProcessPendingChanges();
                return;
            }

            if (!result.Success)
            {
                ReportRequestFailure(result.Message);
                return;
            }
            if (result.Changed && result.Project != null)
            {
                if (action == RevitAction.AssignFixtures || action == RevitAction.RemoveFixtures)
                    PushUndoState();
                if (action == RevitAction.Reload || action == RevitAction.Validate ||
                    action == RevitAction.ResolveChanges)
                {
                    _history.Clear();
                }
                _project = result.Project;
                _committedJson = ProjectDataSerializer.Serialize(_project);
                _fixtureDetails = ReadFixtureDetails(document, _project);
                ApplyResolutionMode();
                RefreshTree();
            }
            IsEnabled = true;
            StatusLabel.Text = result.Message;
            ProcessPendingChanges();
        }

        private void ProcessPendingChanges()
        {
            if (_pendingReload)
            {
                _pendingReload = false;
                _pendingAddedIds.Clear();
                _pendingDeletedIds.Clear();
                QueueRequest(RevitAction.Reload, ProjectDataSerializer.Serialize(_project));
            }
            else if (_project.Settings.ResolutionMode == ResolutionMode.Automatic &&
                (_pendingAddedIds.Count > 0 || _pendingDeletedIds.Count > 0))
            {
                long[] added = _pendingAddedIds.ToArray();
                long[] deleted = _pendingDeletedIds.ToArray();
                _pendingAddedIds.Clear();
                _pendingDeletedIds.Clear();
                QueueRequest(RevitAction.ResolveChanges, ProjectDataSerializer.Serialize(_project),
                    addedIds: added, deletedIds: deleted);
            }
            else
            {
                _pendingAddedIds.Clear();
                _pendingDeletedIds.Clear();
            }
        }

        private void ReportRequestFailure(string message)
        {
            System.Diagnostics.Trace.TraceError("Circuit manager operation failed: {0}", message);
            _pendingAddedIds.Clear();
            _pendingDeletedIds.Clear();
            _pendingReload = false;
            if (!string.IsNullOrEmpty(_committedJson))
            {
                _project = ProjectDataSerializer.Deserialize(_committedJson);
                _history.Clear();
                ApplyResolutionMode();
                RefreshTree();
            }
            _dataLoaded = false;
            IsEnabled = false;
            MessageBox.Show(message + "\n\nReopen the panel to reload the model before retrying.",
                "Circuit Manager Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusLabel.Text = "Operation failed. Reopen the panel.";
        }

        private void Undo()
        {
            if (!_history.CanUndo)
            {
                StatusLabel.Text = "Nothing to undo";
                return;
            }

            string previousState = _history.Undo(ProjectDataSerializer.Serialize(_project));
            _project = ProjectDataSerializer.Deserialize(previousState);
            ApplyResolutionMode();

            SaveAndRefresh();
            StatusLabel.Text =
                $"Undo ({_history.UndoCount} step(s) remaining)";
        }

        private void Redo()
        {
            if (!_history.CanRedo)
            {
                StatusLabel.Text = "Nothing to redo";
                return;
            }

            string nextState = _history.Redo(ProjectDataSerializer.Serialize(_project));
            _project = ProjectDataSerializer.Deserialize(nextState);
            ApplyResolutionMode();

            SaveAndRefresh();
            StatusLabel.Text =
                $"Redo ({_history.UndoCount} step(s) remaining)";
        }

        private void OnPreviewKeyDown(object sender,
            System.Windows.Input.KeyEventArgs e)
        {
            bool ctrl = (System.Windows.Input.Keyboard.Modifiers &
                System.Windows.Input.ModifierKeys.Control) != 0;

            if (!ctrl) return;

            if (e.Key == System.Windows.Input.Key.F)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
                return;
            }
            // Leave Ctrl+Z/Y to the search box's own text undo.
            if (SearchBox.IsKeyboardFocusWithin) return;

            if (e.Key == System.Windows.Input.Key.Z)
            {
                Undo();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Y)
            {
                Redo();
                e.Handled = true;
            }
        }
        // ─── Tree State Preservation ─────────────────────────────

        private HashSet<Guid> GetExpandedGuids()
        {
            if (_expandedBeforeSearch != null)
            {
                var remembered = new HashSet<Guid>(_expandedBeforeSearch);
                AddExpandedRows(remembered);
                return remembered;
            }
            var expanded = new HashSet<Guid>();
            foreach (BoardTreeNode board in _treeNodes)
            {
                if (board.IsExpanded) expanded.Add(board.Board.BoardGuid);
                foreach (ChassisTreeNode chassis in board.Chassis)
                    if (chassis.IsExpanded) expanded.Add(chassis.Chassis.ChassisGuid);
            }
            AddExpandedRows(expanded);
            return expanded;
        }

        /// <summary>Expanded circuit rows (showing circuited elements) are remembered by their circuit GUIDs.</summary>
        private void AddExpandedRows(HashSet<Guid> expanded)
        {
            foreach (BoardTreeNode board in _treeNodes)
            foreach (ChassisTreeNode chassis in board.Chassis)
            foreach (CircuitRowViewModel row in chassis.Rows)
            {
                if (row.LeftCircuit != null && row.LeftCircuit.Fixtures.Count > 0)
                    SetExpanded(expanded, row.LeftCircuit.CircuitGuid, row.IsExpanded);
                if (row.RightCircuit != null && row.RightCircuit.Fixtures.Count > 0)
                    SetExpanded(expanded, row.RightCircuit.CircuitGuid, row.IsExpanded);
            }
        }

        private static void SetExpanded(HashSet<Guid> expanded, Guid guid, bool isExpanded)
        {
            if (isExpanded) expanded.Add(guid);
            else expanded.Remove(guid);
        }

        private static Dictionary<Guid, FixtureDetails> ReadFixtureDetails(Document document, ProjectData project)
        {
            try
            {
                return FixtureDetailsReader.Read(document, project);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Could not read circuited element details: {0}", ex);
                return new Dictionary<Guid, FixtureDetails>();
            }
        }

        private void RestoreExpandedState(HashSet<Guid> expanded)
        {
            if (_search.IsActive)
            {
                _expandedBeforeSearch = new HashSet<Guid>(expanded);
                return;
            }
            foreach (BoardTreeNode board in _treeNodes)
            {
                board.IsExpanded = expanded.Contains(board.Board.BoardGuid);
                foreach (ChassisTreeNode chassis in board.Chassis)
                {
                    chassis.IsExpanded = expanded.Contains(chassis.Chassis.ChassisGuid);
                    foreach (CircuitRowViewModel row in chassis.Rows)
                        row.IsExpanded = row.HasFixtures &&
                            ((row.LeftCircuit != null && expanded.Contains(row.LeftCircuit.CircuitGuid)) ||
                             (row.RightCircuit != null && expanded.Contains(row.RightCircuit.CircuitGuid)));
                }
            }
        }

        // ─── Tree Building ───────────────────────────────────────

        public void RefreshTree() => RefreshTree(null);

        private void RefreshTree(HashSet<Guid>? expanded)
        {
            HashSet<Guid> expandedState = expanded ?? GetExpandedGuids();
            _treeNodes = CircuitTreeProjection.Build(_boards, this, expandedState, _search, out int matches);
            BoardTree.ItemsSource = _treeNodes;
            StatusLabel.Text = _search.IsActive
                ? $"{matches} match(es) for \"{_search.Text}\" in {_boards.Count} board(s)"
                : $"{_boards.Count} board(s) loaded";
        }

        private void TreeHeader_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement header || header.ContextMenu == null) return;
            DependencyObject? parent = VisualTreeHelper.GetParent(header);
            while (parent != null && parent is not TreeViewItem)
                parent = VisualTreeHelper.GetParent(parent);
            if (parent is not TreeViewItem item)
            {
                System.Diagnostics.Trace.TraceWarning("A circuit tree header has no item container.");
                return;
            }

            var menu = header.ContextMenu;
            header.ContextMenu = null;
            item.ContextMenu = menu;
        }

        // ─── Toolbar Handlers ────────────────────────────────────

        private void AddBoardButton_Click(
            object sender, RoutedEventArgs e)
        {
            ProjectData project = _project;
            var dialog = new AddBoardDialog(_boards);
            dialog.ShowDialog();
            if (!dialog.Confirmed || dialog.Result == null) return;
            if (!IsCurrentProject(project)) return;

            if (_search.IsActive) ClearSearch();
            PushUndoState();
            _boards.Add(dialog.Result);
            SaveAndRefresh();
        }

        private void UndoButton_Click(
            object sender, RoutedEventArgs e)
        {
            Undo();
        }

        private void RedoButton_Click(
            object sender, RoutedEventArgs e)
        {
            Redo();
        }

        private void RefreshButton_Click(
            object sender, RoutedEventArgs e)
        {
            QueueRequest(RevitAction.Reload, ProjectDataSerializer.Serialize(_project));
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            ProjectData project = _project;
            int fixtures = _boards.Sum(b => b.Chassis.Sum(c => c.Circuits.Sum(circuit => circuit.Fixtures.Count)));
            if (MessageBox.Show(
                $"Delete all {_boards.Count} board(s) and remove {fixtures} fixture assignment(s)?",
                "Clear All Circuit Data", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            if (!IsCurrentProject(project)) return;
            if (MessageBox.Show("Confirm that all circuit data should be cleared from this project.",
                "Confirm Clear All", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            if (!IsCurrentProject(project)) return;

            PushUndoState();
            _boards.Clear();
            SaveAndRefresh();
        }

        private void ValidateButton_Click(object sender, RoutedEventArgs e)
        {
            QueueRequest(RevitAction.Validate, ProjectDataSerializer.Serialize(_project));
        }

        private void ResolutionSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ProjectData project = _project;
            AutomaticResolutionRules rules = _project.Settings.AutoRules;
            var dialog = new ResolutionSettingsDialog(rules);
            if (dialog.ShowDialog() != true) return;
            if (!IsCurrentProject(project)) return;
            if (rules.OnFixtureCopied == dialog.CopiedAction &&
                rules.OnFixtureDeleted == dialog.DeletedAction &&
                rules.LogAutomaticActions == dialog.LogAutomaticActions)
                return;

            PushUndoState();
            rules.OnFixtureCopied = dialog.CopiedAction;
            rules.OnFixtureDeleted = dialog.DeletedAction;
            rules.LogAutomaticActions = dialog.LogAutomaticActions;
            SaveAndRefresh();
        }

        private void ExpandAllButton_Click(
            object sender, RoutedEventArgs e)
        {
            SetAllExpanded(true);
        }

        private void CollapseAllButton_Click(
            object sender, RoutedEventArgs e)
        {
            SetAllExpanded(false);
        }

        private void SetAllExpanded(bool expanded)
        {
            foreach (BoardTreeNode board in _treeNodes)
            {
                board.IsExpanded = expanded;
                foreach (ChassisTreeNode chassis in board.Chassis)
                    chassis.IsExpanded = expanded;
            }
        }

        private void ModeToggle_Checked(
            object sender, RoutedEventArgs e)
        {
            AutoModeLabel.Text = "MANUAL";
            AutoModeLabel.Foreground = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(255, 165, 0));
            ChangeResolutionMode(ResolutionMode.Manual);
        }

        private void ModeToggle_Unchecked(
            object sender, RoutedEventArgs e)
        {
            AutoModeLabel.Text = "AUTO";
            AutoModeLabel.Foreground = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(78, 201, 78));
            ChangeResolutionMode(ResolutionMode.Automatic);
        }

        private void ChangeResolutionMode(ResolutionMode mode)
        {
            if (_applyingSettings || !_dataLoaded || _project.Settings.ResolutionMode == mode) return;
            PushUndoState();
            _project.Settings.ResolutionMode = mode;
            _project.Settings.ModeLastChanged = DateTime.UtcNow;
            SaveAndRefresh();
        }
    }
}
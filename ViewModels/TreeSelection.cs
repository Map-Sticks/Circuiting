using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Circuiting.ViewModels
{
    public enum TreeSelectionKind
    {
        None,
        Circuits,
        Elements
    }

    /// <summary>
    /// Multi-selection of circuits or circuited elements in the circuit tree, keyed by circuit or fixture GUID
    /// so it survives tree rebuilds. A selection holds one kind at a time. Ctrl+click toggles, Shift+click
    /// selects the range from the anchor in visible tree order, Ctrl+Shift+click adds that range.
    /// </summary>
    public sealed class TreeSelection : ObservableObject
    {
        private readonly HashSet<Guid> _keys = new HashSet<Guid>();
        private Guid? _anchor;

        public TreeSelection(Action move, Action delete)
        {
            MoveCommand = new RelayCommand(move);
            DeleteCommand = new RelayCommand(delete);
            ClearCommand = new RelayCommand(Clear);
        }

        public TreeSelectionKind Kind { get; private set; }
        public int Count => _keys.Count;
        public IReadOnlyCollection<Guid> Keys => _keys;
        public bool Contains(Guid key) => _keys.Contains(key);
        public bool Contains(TreeSelectionKind kind, Guid key) => Kind == kind && _keys.Contains(key);

        public bool HasMultipleCircuits => Kind == TreeSelectionKind.Circuits && Count > 1;
        public bool HasMultipleElements => Kind == TreeSelectionKind.Elements && Count > 1;
        public string MoveCircuitsLabel => $"↔️  Move {Count} Selected Circuits...";
        public string DeleteCircuitsLabel => $"🗑️  Delete {Count} Selected Circuits";
        public string MoveElementsLabel => $"↔️  Move {Count} Selected Elements to Circuit...";
        public string DeleteElementsLabel => $"🔗  Unassign {Count} Selected Elements";
        public string ClearLabel => "✖  Clear Selection (Esc)";
        public IRelayCommand MoveCommand { get; }
        public IRelayCommand DeleteCommand { get; }
        public IRelayCommand ClearCommand { get; }

        public string Summary => Count == 0 ? string.Empty
            : $"{Count} {(Kind == TreeSelectionKind.Circuits ? "circuit" : "element")}{(Count == 1 ? string.Empty : "s")} " +
              "selected — right-click to move or delete, Alt+drag to move, Delete key, Esc to clear";

        /// <summary>Applies a click on <paramref name="key"/>; <paramref name="order"/> is the visible order of that kind.</summary>
        public void Click(TreeSelectionKind kind, Guid key, IReadOnlyList<Guid> order, bool ctrl, bool shift)
        {
            if (kind == TreeSelectionKind.None) throw new ArgumentOutOfRangeException(nameof(kind));
            if (Kind != kind)
            {
                _keys.Clear();
                _anchor = null;
                Kind = kind;
            }
            int from = _anchor.HasValue ? IndexOf(order, _anchor.Value) : -1;
            int to = IndexOf(order, key);
            if (shift && from >= 0 && to >= 0)
            {
                if (!ctrl) _keys.Clear();
                for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++) _keys.Add(order[i]);
            }
            else if (ctrl)
            {
                if (!_keys.Remove(key)) _keys.Add(key);
                _anchor = key;
            }
            else
            {
                _keys.Clear();
                _keys.Add(key);
                _anchor = key;
            }
            if (_keys.Count == 0) Kind = TreeSelectionKind.None;
            Changed();
        }

        public void Clear()
        {
            if (_keys.Count == 0 && Kind == TreeSelectionKind.None) return;
            _keys.Clear();
            _anchor = null;
            Kind = TreeSelectionKind.None;
            Changed();
        }

        /// <summary>Drops keys that no longer exist (deleted circuits or unassigned elements).</summary>
        public void Prune(Func<Guid, bool> exists)
        {
            int removed = _keys.RemoveWhere(key => !exists(key));
            if (_anchor.HasValue && !exists(_anchor.Value)) _anchor = null;
            if (_keys.Count == 0) Kind = TreeSelectionKind.None;
            if (removed > 0) Changed();
        }

        /// <summary>Updates the highlight flags of the tree nodes.</summary>
        public void ApplyTo(IEnumerable<BoardTreeNode> boards)
        {
            foreach (BoardTreeNode board in boards)
            foreach (ChassisTreeNode chassis in board.Chassis)
            foreach (CircuitRowViewModel row in chassis.Rows)
            {
                row.IsLeftSelected = row.LeftKey.HasValue && Contains(TreeSelectionKind.Circuits, row.LeftKey.Value);
                row.IsRightSelected = row.RightKey.HasValue && Contains(TreeSelectionKind.Circuits, row.RightKey.Value);
                foreach (FixtureTreeNode fixture in row.Fixtures)
                    fixture.IsSelected = Contains(TreeSelectionKind.Elements, fixture.Fixture.FixtureGuid);
            }
        }

        /// <summary>Circuit GUIDs in visible tree order (expanded boards and chassis; left then right per row).</summary>
        public static List<Guid> CircuitOrder(IEnumerable<BoardTreeNode> boards)
        {
            var order = new List<Guid>();
            var seen = new HashSet<Guid>();
            foreach (CircuitRowViewModel row in VisibleRows(boards))
            foreach (Guid? key in new[] { row.LeftKey, row.RightKey })
                if (key.HasValue && seen.Add(key.Value)) order.Add(key.Value);
            return order;
        }

        /// <summary>Element (fixture) GUIDs in visible tree order (expanded circuit rows only).</summary>
        public static List<Guid> ElementOrder(IEnumerable<BoardTreeNode> boards) =>
            VisibleRows(boards).Where(row => row.IsExpanded)
                .SelectMany(row => row.Fixtures).Select(f => f.Fixture.FixtureGuid).ToList();

        private static IEnumerable<CircuitRowViewModel> VisibleRows(IEnumerable<BoardTreeNode> boards) =>
            boards.Where(b => b.IsExpanded).SelectMany(b => b.Chassis)
                .Where(c => c.IsExpanded).SelectMany(c => c.Rows);

        private static int IndexOf(IReadOnlyList<Guid> order, Guid key)
        {
            for (int i = 0; i < order.Count; i++)
                if (order[i] == key) return i;
            return -1;
        }

        private void Changed() => OnPropertyChanged(string.Empty);
    }
}

namespace Circuiting.ViewModels
{
    public sealed class CircuitEditHistory
    {
        private const int Capacity = 50;
        private readonly Stack<string> _undo = new Stack<string>();
        private readonly Stack<string> _redo = new Stack<string>();

        public int UndoCount => _undo.Count;
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public IEnumerable<string> UndoTopFirst => _undo;
        public IEnumerable<string> RedoTopFirst => _redo;

        public void Push(string snapshot)
        {
            _undo.Push(snapshot);
            _redo.Clear();
            if (_undo.Count <= Capacity) return;

            string[] newest = _undo.Take(Capacity).ToArray();
            _undo.Clear();
            foreach (string state in Enumerable.Reverse(newest)) _undo.Push(state);
        }

        public string Undo(string current)
        {
            if (!CanUndo) throw new InvalidOperationException("There is nothing to undo.");
            _redo.Push(current);
            return _undo.Pop();
        }

        public string Redo(string current)
        {
            if (!CanRedo) throw new InvalidOperationException("There is nothing to redo.");
            _undo.Push(current);
            return _redo.Pop();
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }

        public void Restore(DocumentSessionState? state)
        {
            Clear();
            if (state == null) return;
            foreach (string snapshot in Enumerable.Reverse(state.UndoTopFirst)) _undo.Push(snapshot);
            foreach (string snapshot in Enumerable.Reverse(state.RedoTopFirst)) _redo.Push(snapshot);
        }

        public void ClearIfChanged(string previous, string current)
        {
            if (!string.Equals(previous, current, StringComparison.Ordinal)) Clear();
        }
    }
}

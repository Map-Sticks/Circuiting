namespace Circuiting.ViewModels
{
    public sealed class DocumentSessionState
    {
        public string CommittedJson { get; set; } = string.Empty;
        public string[] UndoTopFirst { get; set; } = Array.Empty<string>();
        public string[] RedoTopFirst { get; set; } = Array.Empty<string>();
        public HashSet<Guid> Expanded { get; set; } = new HashSet<Guid>();
    }

    public sealed class DocumentSessionHandle<TDocument> where TDocument : class
    {
        internal TDocument Document { get; set; } = null!;
        internal DocumentSessionState? State { get; set; }
        internal bool IsClosing { get; set; }
    }

    public sealed class DocumentSessionCache<TDocument> where TDocument : class
    {
        private readonly List<DocumentSessionHandle<TDocument>> _states =
            new List<DocumentSessionHandle<TDocument>>();
        private readonly Func<TDocument, bool> _canCompare;

        public DocumentSessionCache(Func<TDocument, bool>? canCompare = null)
        {
            _canCompare = canCompare ?? (_ => true);
        }

        public void Remember(TDocument document, string committedJson, IEnumerable<string> undoTopFirst,
            IEnumerable<string> redoTopFirst, IEnumerable<Guid> expanded)
        {
            DocumentSessionHandle<TDocument> handle = Find(document) ?? Create(document);
            handle.State = new DocumentSessionState
            {
                CommittedJson = committedJson,
                UndoTopFirst = undoTopFirst.ToArray(),
                RedoTopFirst = redoTopFirst.ToArray(),
                Expanded = new HashSet<Guid>(expanded)
            };
        }

        public DocumentSessionState? Restore(TDocument document, string committedJson)
        {
            DocumentSessionHandle<TDocument>? handle = Find(document);
            DocumentSessionState? state = handle?.State;
            if (state == null) return null;
            if (string.Equals(state.CommittedJson, committedJson, StringComparison.Ordinal)) return state;
            Forget(document);
            return null;
        }

        public void Forget(TDocument document)
        {
            DocumentSessionHandle<TDocument>? handle = Find(document);
            if (handle == null) return;
            handle.State = null;
            if (!handle.IsClosing) _states.Remove(handle);
        }

        public DocumentSessionHandle<TDocument> TrackClosing(TDocument document)
        {
            DocumentSessionHandle<TDocument> handle = Find(document) ?? Create(document);
            handle.IsClosing = true;
            return handle;
        }

        public void CompleteClosing(DocumentSessionHandle<TDocument> handle, bool succeeded)
        {
            if (!_states.Contains(handle))
                throw new InvalidOperationException("The document close was not tracked.");
            handle.IsClosing = false;
            if (succeeded || handle.State == null) _states.Remove(handle);
        }

        private DocumentSessionHandle<TDocument>? Find(TDocument document)
        {
            return _states.FirstOrDefault(handle =>
                ReferenceEquals(handle.Document, document) ||
                (_canCompare(handle.Document) &&
                 EqualityComparer<TDocument>.Default.Equals(handle.Document, document)));
        }

        private DocumentSessionHandle<TDocument> Create(TDocument document)
        {
            var handle = new DocumentSessionHandle<TDocument> { Document = document };
            _states.Add(handle);
            return handle;
        }
    }
}

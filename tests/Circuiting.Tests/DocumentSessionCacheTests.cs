using Circuiting.ViewModels;
using Xunit;

namespace Circuiting.Tests;

public class DocumentSessionCacheTests
{
    [Fact]
    public void HistoriesAndExpansionAreIsolatedByDocument()
    {
        var cache = new DocumentSessionCache<object>();
        var first = new object();
        var second = new object();
        var expanded = Guid.NewGuid();
        cache.Remember(first, "one", new[] { "newest", "oldest" }, new[] { "redo" }, new[] { expanded });
        cache.Remember(second, "two", new[] { "other" }, Array.Empty<string>(), Array.Empty<Guid>());

        var state = cache.Restore(first, "one");

        Assert.NotNull(state);
        Assert.Equal(new[] { "newest", "oldest" }, state.UndoTopFirst);
        Assert.Equal(new[] { "redo" }, state.RedoTopFirst);
        Assert.Contains(expanded, state.Expanded);
        Assert.Equal(new[] { "other" }, cache.Restore(second, "two")?.UndoTopFirst);
    }

    [Fact]
    public void ChangedOrClosedDocumentDoesNotRestoreStaleHistory()
    {
        var cache = new DocumentSessionCache<object>();
        var document = new object();
        cache.Remember(document, "old", new[] { "undo" }, Array.Empty<string>(), Array.Empty<Guid>());
        Assert.Null(cache.Restore(document, "new"));
        Assert.Null(cache.Restore(document, "old"));

        cache.Remember(document, "new", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<Guid>());
        cache.Forget(document);
        Assert.Null(cache.Restore(document, "new"));
    }

    [Fact]
    public void CancelledClosePreservesHistoryAndSuccessfulCloseRemovesIt()
    {
        var cache = new DocumentSessionCache<object>();
        var document = new object();
        cache.Remember(document, "current", new[] { "undo" }, Array.Empty<string>(), Array.Empty<Guid>());

        var cancelled = cache.TrackClosing(document);
        cache.CompleteClosing(cancelled, false);
        Assert.Equal(new[] { "undo" }, cache.Restore(document, "current")?.UndoTopFirst);

        var closed = cache.TrackClosing(document);
        cache.Forget(document);
        cache.Remember(document, "new", new[] { "later" }, Array.Empty<string>(), Array.Empty<Guid>());
        cache.CompleteClosing(closed, true);
        Assert.Null(cache.Restore(document, "new"));
    }

    [Fact]
    public void ClosedDocumentWrappersAreNotComparedWhileAnotherDocumentIsOpened()
    {
        var cache = new DocumentSessionCache<FakeDocument>(document => document.Valid);
        var closed = new FakeDocument();
        cache.Remember(closed, "old", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<Guid>());
        var handle = cache.TrackClosing(closed);
        closed.Valid = false;

        var next = new FakeDocument();
        cache.Remember(next, "new", new[] { "undo" }, Array.Empty<string>(), Array.Empty<Guid>());
        cache.CompleteClosing(handle, true);

        Assert.Equal(new[] { "undo" }, cache.Restore(next, "new")?.UndoTopFirst);
    }

    private sealed class FakeDocument
    {
        public bool Valid { get; set; } = true;
        public override bool Equals(object? other)
        {
            if (!Valid) throw new InvalidOperationException("The wrapper is no longer valid.");
            return ReferenceEquals(this, other);
        }
        public override int GetHashCode() => base.GetHashCode();
    }
}

using System.Collections.ObjectModel;
using Nickelony.IDEKit.Core.Bookmarks;
using Nickelony.IDEKit.Core.Notifications;
#if AVALONIAEDIT
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Text;
#else
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Text;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Bookmarks;

#else

namespace Nickelony.IDEKit.AvalonEdit.Bookmarks;

#endif

/// <summary>Tracks the bookmarked lines of the editor's <see cref="TextDocument"/> with anchors that follow its edits.</summary>
/// <remarks>
/// Bookmarks live in memory, and the tracker runs on the document's owner thread; persistence is optional through an <see cref="IBookmarkStore"/>,
/// for which <see cref="BookmarkSidecarStore"/> is ready-made. The anchors bind to the document the provider returns. A different document
/// reports no bookmarks, and the stale anchors are discarded on the next mutation, whose <see cref="Changed"/> notification reports the discard.
/// A host must restore bookmarks for a new document. A bookmark follows the marked line's content, so a line break inserted at the start
/// of a bookmarked line moves the bookmark down with that content.
/// </remarks>
public sealed class BookmarkTracker : IBookmarkSource, IChangeNotificationSource
{
	private readonly Func<TextDocument?> _documentProvider;
	private readonly List<TextAnchor> _bookmarkAnchors = [];

	private TextDocument? _bookmarkDocument;

	private readonly DocumentVersionCache<IReadOnlyList<int>> _bookmarkedLinesCache = new();

	// Returned on the unbound and absent-document paths, which a margin's render pass hits on every frame
	// while no document is bound; a shared instance avoids a per-render allocation.
	private static readonly IReadOnlyList<int> s_emptyLineNumbers = [];

	/// <summary>
	/// Raised after an explicit bookmark mutation, which is one of:
	/// <list type="bullet">
	/// <item><see cref="ToggleBookmark"/> adds or removes a bookmark;</item>
	/// <item><see cref="ClearBookmarks"/> removes all bookmarks, including when none exist;</item>
	/// <item><see cref="RestoreBookmarks"/> replaces them.</item>
	/// </list>
	/// </summary>
	/// <remarks>
	/// Edits do not raise this event. An edit that merges bookmarked lines can change the resolved bookmark set without a notification.
	/// A subscriber that needs the resolved set after edits must re-read <see cref="GetMarkedLineNumbers"/>; a <see cref="BookmarkMargin"/> needs
	/// no such read, because it repaints through the editor's visual-line changes.
	/// </remarks>
	public event EventHandler? Changed;

	/// <param name="documentProvider">
	/// Provides the editor's <see cref="TextDocument"/> used for bookmark operations, or <see langword="null"/> when no document is current;
	/// reads report the absence of bookmarks in that state instead of throwing.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="documentProvider"/> is <see langword="null"/>.</exception>
	public BookmarkTracker(Func<TextDocument?> documentProvider)
	{
		ArgumentNullException.ThrowIfNull(documentProvider);
		_documentProvider = documentProvider;
	}

	/// <summary>
	/// Whether the tracker's bookmarks are bound to the document the provider currently returns, which is the state in which they can be persisted.
	/// </summary>
	/// <remarks>
	/// While it is <see langword="false"/>, a save would persist
	/// an empty set and delete the stored bookmarks, so <see cref="SaveTo(IBookmarkStore, string)"/> fails instead. A host can query it to guard
	/// a save that runs from a <see cref="Changed"/> handler. Reading does not bind the tracker and reports <see langword="false"/> when the provider
	/// returns <see langword="null"/>.
	/// </remarks>
	public bool IsBoundToCurrentDocument
	{
		get
		{
			TextDocument? document = _documentProvider();
			return document is not null && ReferenceEquals(_bookmarkDocument, document);
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Reading does not modify the bookmarks or their anchors, and the returned read-only collection is cached while the document version
	/// and the bookmark set are unchanged. The numbers are one-based, ascending, and refer to the current document; reads never throw, so a provider
	/// that returns <see langword="null"/> yields an empty list and a margin's render pass cannot fail.
	/// </remarks>
	public IReadOnlyList<int> GetMarkedLineNumbers()
	{
		TextDocument? document = _documentProvider();

		if (document is null)
			return s_emptyLineNumbers;

		return BookmarksApplyTo(document) ? GetMarkedLineNumbersCached(document) : s_emptyLineNumbers;
	}

	/// <inheritdoc/>
	/// <exception cref="InvalidOperationException">The document provider returns <see langword="null"/>.</exception>
	/// <remarks>
	/// A toggle adds a bookmark to an unmarked line and clears the bookmark on a marked line. Anchors that resolve to the same line, which can happen
	/// after edits merge bookmarked lines, are removed by the next toggle so the visible bookmarks and the anchors stay in sync.
	/// </remarks>
	public void ToggleBookmark(int documentOffset)
	{
		TextDocument document = ReconcileDocument();

		PruneInvalidAnchors(document);

		DocumentLine currentLine = document.GetLineByOffset(document.ClampOffset(documentOffset));
		TextAnchor? bookmarkAnchor = FindBookmarkAnchor(document, currentLine);

		if (bookmarkAnchor is null)
			AddBookmark(document, currentLine);
		else
			_bookmarkAnchors.Remove(bookmarkAnchor);

		RaiseChanged();
	}

	/// <summary>
	/// Returns the first bookmarked line after the line containing the offset, wrapping to the first bookmark in the document when necessary.
	/// </summary>
	/// <param name="offset">
	/// The zero-based document offset whose containing line is the starting point. Values outside the document are clamped.
	/// </param>
	/// <returns>The next bookmarked line, or <see langword="null"/> when none exist.</returns>
	/// <remarks>
	/// Reads report the absence of bookmarks instead of throwing: a document provider that returns <see langword="null"/> yields
	/// <see langword="null"/> as well.
	/// </remarks>
	public DocumentLine? GetNextBookmarkLine(int offset) => GetAdjacentBookmarkLine(offset, searchForward: true);

	/// <summary>
	/// Returns the last bookmarked line before the line containing the offset, wrapping to the last bookmark in the document when necessary.
	/// </summary>
	/// <param name="offset">
	/// The zero-based document offset whose containing line is the starting point. Values outside the document are clamped.
	/// </param>
	/// <returns>The previous bookmarked line, or <see langword="null"/> when none exist.</returns>
	/// <remarks>
	/// Reads report the absence of bookmarks instead of throwing: a document provider that returns <see langword="null"/> yields
	/// <see langword="null"/> as well.
	/// </remarks>
	public DocumentLine? GetPreviousBookmarkLine(int offset) => GetAdjacentBookmarkLine(offset, searchForward: false);

	/// <summary>Removes all bookmarks of the document the provider currently returns.</summary>
	/// <exception cref="InvalidOperationException">The document provider returns <see langword="null"/>.</exception>
	/// <remarks>
	/// The tracker is bound to that document first, so the clear covers the current document and a subsequent save persists an empty
	/// set for it, which deletes the persisted bookmarks. Raises <see cref="Changed"/>, even when no bookmarks exist.
	/// </remarks>
	public void ClearBookmarks()
	{
		// Resolve the current document first: a clear belongs to the document being edited, so a later
		// save persists the clear for that document instead of failing as unbound.
		_ = ReconcileDocument();

		_bookmarkAnchors.Clear();
		RaiseChanged();
	}

	/// <summary>
	/// Replaces the current bookmarks with bookmarks for the supplied one-based line numbers. Line numbers outside the document and duplicate entries
	/// are ignored.
	/// </summary>
	/// <param name="lineNumbers">The one-based line numbers to bookmark.</param>
	/// <exception cref="ArgumentNullException"><paramref name="lineNumbers"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The document provider returns <see langword="null"/>.</exception>
	/// <remarks>
	/// The document is resolved before the bookmarks are replaced, so a provider failure leaves the existing bookmarks intact.
	/// Line numbers are matched against the current document, so a restore re-creates bookmarks by number rather than by original content.
	/// </remarks>
	public void RestoreBookmarks(IEnumerable<int> lineNumbers)
	{
		ArgumentNullException.ThrowIfNull(lineNumbers);

		// Resolve the document first: a provider failure must not discard the current bookmarks.
		TextDocument document = ReconcileDocument();

		_bookmarkAnchors.Clear();

		foreach (int lineNumber in lineNumbers)
		{
			if (lineNumber >= 1 && lineNumber <= document.LineCount)
			{
				DocumentLine documentLine = document.GetLineByNumber(lineNumber);

				// Matching each line number against the current anchors is linear in the bookmark count,
				// which is adequate for the counts an editor typically holds.
				if (FindBookmarkAnchor(document, documentLine) is null)
					AddBookmark(document, documentLine);
			}
		}

		RaiseChanged();
	}

	/// <summary>
	/// Finds the next or previous bookmarked line relative to the line containing the offset, wrapping around at the ends of the document.
	/// </summary>
	/// <param name="offset">The document offset whose line is the search origin.</param>
	/// <param name="searchForward">Whether to search toward the end of the document.</param>
	/// <returns>The adjacent bookmarked line, or <see langword="null"/> when no bookmarks are bound to the current document.</returns>
	private DocumentLine? GetAdjacentBookmarkLine(int offset, bool searchForward)
	{
		TextDocument? document = _documentProvider();

		if (document is null || !BookmarksApplyTo(document))
			return null;

		IReadOnlyList<int> bookmarkedLineNumbers = GetMarkedLineNumbersCached(document);

		if (bookmarkedLineNumbers.Count == 0)
			return null;

		int currentLineNumber = document.GetLineByOffset(document.ClampOffset(offset)).LineNumber;

		return AdjacentBookmarkLineResolver.Resolve(bookmarkedLineNumbers, currentLineNumber, searchForward) is int lineNumber
			? document.GetLineByNumber(lineNumber)
			: null;
	}

	/// <summary>Returns the document currently returned by the provider.</summary>
	/// <returns>The current document.</returns>
	/// <exception cref="InvalidOperationException">The document provider returns <see langword="null"/>.</exception>
	private TextDocument GetCurrentDocument() => _documentProvider() ?? throw new InvalidOperationException("The document provider returned null.");

	/// <summary>Saves the tracker's current one-based bookmark line numbers through the supplied store.</summary>
	/// <param name="store">The store that saves the bookmarks.</param>
	/// <param name="documentId">The identifier of the document the bookmarks belong to.</param>
	/// <returns><see langword="true"/> when the store reports success; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="documentId"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">
	/// The tracker's document provider returns <see langword="null"/>, or the bookmarks are not bound to the document its provider currently returns,
	/// for example after a document swap before bookmarks are restored for the new document. Saving would delete the persisted bookmarks, so the save
	/// fails instead.
	/// </exception>
	/// <remarks>
	/// The save fails while <see cref="IsBoundToCurrentDocument"/> is <see langword="false"/>; a host that saves from the tracker's
	/// <see cref="Changed"/> event can query that property to skip or defer the save in the unbound window.
	/// </remarks>
	public bool SaveTo(IBookmarkStore store, string documentId)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(documentId);

		// The persistence target is resolved fail-fast: an unbound or mismatched tracker would
		// otherwise persist an empty set, which deletes the sidecar instead of saving the bookmarks.
		ThrowIfNotBoundToCurrentDocument();

		IReadOnlyList<int> bookmarkLineNumbers = GetMarkedLineNumbers();

		return store.TrySave(documentId, bookmarkLineNumbers);
	}

	/// <summary>Restores the stored one-based bookmark line numbers into the tracker.</summary>
	/// <param name="store">The store that restores the bookmarks.</param>
	/// <param name="documentId">The identifier of the document the bookmarks belong to.</param>
	/// <returns>
	/// <see langword="true"/> when the store's load produced a result and the tracker's bookmarks were replaced;
	/// <see langword="false"/> when the load failed and nothing was changed.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="documentId"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">
	/// The tracker's document provider returns <see langword="null"/> and the load produced a result to restore.
	/// </exception>
	/// <remarks>
	/// Existing bookmarks are replaced, line numbers outside the document are ignored, and the tracker's <see cref="Changed"/> event is raised.
	/// A load that reports failure (see <see cref="IBookmarkStore.TryLoad"/>) leaves the tracker unchanged, so a tracker that has not bound yet stays
	/// unbound and a later <see cref="SaveTo(IBookmarkStore, string)"/> fails instead of deleting the stored bookmarks.
	/// </remarks>
	public bool RestoreFrom(IBookmarkStore store, string documentId)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(documentId);

		if (!store.TryLoad(documentId, out IReadOnlyList<int> bookmarkLineNumbers))
			return false;

		RestoreBookmarks(bookmarkLineNumbers);
		return true;
	}

	/// <summary>Refuses to persist when the tracker's bookmarks are not bound to the document the provider currently returns.</summary>
	/// <exception cref="InvalidOperationException">
	/// The document provider returns <see langword="null"/>, or the bookmarks are not bound to the current document.
	/// Restore bookmarks for the current document before saving.
	/// </exception>
	/// <remarks>
	/// Persisting a tracker whose anchors belong to a different document (or to no document yet) would store an empty set,
	/// which deletes the persisted bookmarks. This check turns that state into a failure the host can see instead of data loss.
	/// </remarks>
	private void ThrowIfNotBoundToCurrentDocument()
	{
		TextDocument document = GetCurrentDocument();

		if (_bookmarkDocument is null || !ReferenceEquals(_bookmarkDocument, document))
		{
			throw new InvalidOperationException(
				"Bookmarks are not bound to the current document. Restore bookmarks for the current document before saving."
			);
		}
	}

	/// <summary>Reports whether the tracker's bookmarks belong to the supplied document.</summary>
	/// <param name="document">The document to test.</param>
	/// <returns>
	/// <see langword="true"/> when the bookmarks are bound to the document or no document has been bound yet; otherwise, <see langword="false"/>.
	/// </returns>
	private bool BookmarksApplyTo(TextDocument document) => _bookmarkDocument is null || ReferenceEquals(_bookmarkDocument, document);

	/// <summary>
	/// Rebinds the tracker to the document currently returned by the provider, discarding the anchors when that document differs
	/// from the one that holds them.
	/// </summary>
	/// <remarks>
	/// Until the next mutation, the stale anchors keep the previous document alive.
	/// </remarks>
	private TextDocument ReconcileDocument()
	{
		TextDocument document = GetCurrentDocument();

		if (ReferenceEquals(_bookmarkDocument, document))
			return document;

		// The provider now returns a different document than the one holding the anchors.
		// Discard the anchors instead of reinterpreting their offsets in the new document.
		if (_bookmarkDocument is not null)
			_bookmarkAnchors.Clear();

		_bookmarkDocument = document;
		return document;
	}

	private void RaiseChanged()
	{
		// Bookmark mutations always invalidate the cached result.
		_bookmarkedLinesCache.Invalidate();
		Changed?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>
	/// Returns the cached line numbers for the supplied document, collecting them when the document version or the bookmark set changed.
	/// </summary>
	/// <param name="document">The document to read.</param>
	/// <returns>The cached or freshly collected line numbers.</returns>
	private IReadOnlyList<int> GetMarkedLineNumbersCached(TextDocument document) =>
		_bookmarkedLinesCache.GetOrCreate(document, CollectBookmarkedLineNumbers);

	/// <summary>Resolves every anchor to its current line and collects the de-duplicated, ascending line numbers.</summary>
	/// <param name="document">The document the anchors resolve against.</param>
	/// <returns>The read-only ascending line numbers.</returns>
	private ReadOnlyCollection<int> CollectBookmarkedLineNumbers(TextDocument document)
	{
		var lineNumbers = new List<int>();
		var seenLineNumbers = new HashSet<int>();

		foreach (TextAnchor anchor in _bookmarkAnchors)
		{
			int lineNumber = GetBookmarkedLine(document, anchor).LineNumber;

			if (!seenLineNumbers.Add(lineNumber))
				continue;

			lineNumbers.Add(lineNumber);
		}

		lineNumbers.Sort();
		return lineNumbers.AsReadOnly();
	}

	/// <summary>
	/// Removes anchors that collapsed onto a line that another anchor already resolves to, so the anchor list matches the de-duplicated
	/// bookmarked lines.
	/// </summary>
	private void PruneInvalidAnchors(TextDocument document)
	{
		var seenLineNumbers = new HashSet<int>();
		_bookmarkAnchors.RemoveAll(anchor => !seenLineNumbers.Add(GetBookmarkedLine(document, anchor).LineNumber));
	}

	/// <summary>Creates a bookmark anchor for the supplied line and binds the tracker to that line's document.</summary>
	/// <param name="document">The document that holds the new anchor.</param>
	/// <param name="line">The line to mark.</param>
	private void AddBookmark(TextDocument document, DocumentLine line)
	{
		// The anchor marks the start of the line and follows its content: the default movement moves
		// the anchor behind text inserted at the line start (for example a line break), so the bookmark
		// stays on the content instead of remaining on the inserted text.
		//
		// SurviveDeletion is set because a bookmark marks a line position that outlives the deletion of its
		// text.
		TextAnchor anchor = document.CreateAnchor(line.Offset);
		anchor.MovementType = AnchorMovementType.Default;
		anchor.SurviveDeletion = true;

		_bookmarkAnchors.Add(anchor);
		_bookmarkDocument = document;
	}

	/// <summary>Finds the anchor that currently resolves to the supplied line.</summary>
	/// <param name="document">The document the anchors resolve against.</param>
	/// <param name="line">The line to look up.</param>
	/// <returns>The matching anchor, or <see langword="null"/> when the line is not bookmarked.</returns>
	private TextAnchor? FindBookmarkAnchor(TextDocument document, DocumentLine line)
	{
		foreach (TextAnchor anchor in _bookmarkAnchors)
		{
			if (GetBookmarkedLine(document, anchor).LineNumber == line.LineNumber)
				return anchor;
		}

		return null;
	}

	/// <summary>Resolves an anchor to the line it currently marks.</summary>
	/// <remarks>
	/// Anchors are created with <see cref="TextAnchor.SurviveDeletion"/> set, so they are never flagged as deleted; the offset is clamped
	/// because a surviving anchor can sit one position past the end of the document after a deletion.
	/// </remarks>
	private static DocumentLine GetBookmarkedLine(TextDocument document, TextAnchor anchor)
	{
		int offset = document.ClampOffset(anchor.Offset);
		return document.GetLineByOffset(offset);
	}
}

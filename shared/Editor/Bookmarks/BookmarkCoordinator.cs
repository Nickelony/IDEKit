#if AVALONIAEDIT
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Documents;
#else
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Documents;
#endif
using Nickelony.IDEKit.Core.Bookmarks;
using Nickelony.IDEKit.Core.Notifications;
using System.Collections.ObjectModel;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Bookmarks;
#else
namespace Nickelony.IDEKit.AvalonEdit.Bookmarks;
#endif

/// <summary>
/// Manages bookmarks within the editor's <see cref="TextDocument"/>,
/// supporting bookmark toggling, navigation, and restoration.
/// </summary>
/// <remarks>
/// <para>
/// Bookmarks live in memory. Persistence is optional: <see cref="BookmarkSidecarStore"/> is a ready-made
/// <see cref="IBookmarkStore"/>, and a host may substitute any implementation. The coordinator is
/// expected to be used on the document's owner thread.
/// </para>
/// <para>
/// Bookmarks are anchored in the document returned by the provider when they are added or restored.
/// If the provider later returns a different document instance, bookmark reads report no bookmarks
/// for that document, and the stale anchors are discarded instead of being reinterpreted when the
/// coordinator next toggles, clears, or restores bookmarks. Discarding happens only on a mutation,
/// so the mutation's <see cref="Changed"/> notification covers it; a connected <see cref="BookmarkMargin"/>
/// also repaints when the editor's visual lines change, so the reported bookmark set is what the
/// margin renders. The host must restore bookmarks for the new document when it becomes current;
/// <see cref="IsBoundToCurrentDocument"/> reports whether the current document can be persisted.
/// </para>
/// <para>
/// A bookmark follows the marked line's content: inserting a line break at the start of a bookmarked
/// line keeps the bookmark on the content instead of leaving it on the inserted blank line.
/// </para>
/// </remarks>
public sealed class BookmarkCoordinator : IBookmarkSource, IChangeNotificationSource
{
	private readonly Func<TextDocument?> _documentProvider;
	private readonly List<TextAnchor> _bookmarkAnchors = [];

	private TextDocument? _bookmarkDocument;

	private readonly DocumentVersionCache<IReadOnlyList<int>> _bookmarkedLinesCache = new();

	/// <summary>
	/// Raised after an explicit bookmark mutation, which is one of:
	/// <list type="bullet">
	/// <item><see cref="ToggleBookmark"/> adds or removes a bookmark;</item>
	/// <item><see cref="ClearBookmarks"/> removes all bookmarks, including when none exist;</item>
	/// <item><see cref="RestoreBookmarks"/> replaces them.</item>
	/// </list>
	/// </summary>
	/// <remarks>
	/// Edits do not raise this event. An edit that merges bookmarked lines can change the resolved
	/// bookmark set without a notification. A subscriber that needs the resolved set after edits must
	/// re-read <see cref="GetMarkedLineNumbers"/>; a <see cref="BookmarkMargin"/> needs no such read,
	/// because it repaints through the editor's visual-line changes.
	/// </remarks>
	public event EventHandler? Changed;

	/// <summary>
	/// Initializes a new instance of the <see cref="BookmarkCoordinator"/> class.
	/// </summary>
	/// <param name="documentProvider">
	/// Provides the editor's <see cref="TextDocument"/> used for bookmark operations, or
	/// <see langword="null"/> when no document is current; reads report the absence of bookmarks in
	/// that state instead of throwing.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="documentProvider"/> is <see langword="null"/>.</exception>
	public BookmarkCoordinator(Func<TextDocument?> documentProvider)
	{
		ArgumentNullException.ThrowIfNull(documentProvider);
		_documentProvider = documentProvider;
	}

	/// <summary>
	/// Gets a value indicating whether the coordinator's bookmarks are bound to the document the
	/// provider currently returns, which is the state in which they can be persisted.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The property mirrors the precondition of
	/// <see cref="BookmarkStoreExtensions.SaveBookmarks(BookmarkCoordinator, IBookmarkStore, string)"/>:
	/// while it is <see langword="false"/>, a save would persist an empty set and delete the stored
	/// bookmarks, so the extension fails instead. A host can query this property to guard a save that
	/// runs from a <see cref="Changed"/> handler, for example after a document swap and before a
	/// restore.
	/// </para>
	/// <para>
	/// Reading this property does not bind the coordinator, and the read does not throw when the
	/// provider returns <see langword="null"/>; it reports <see langword="false"/> in that state.
	/// </para>
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
	/// Reading bookmarks does not modify the bookmarks or their anchors.
	/// The returned read-only collection is cached and reused while
	/// the document version and the bookmark set are unchanged; the line numbers are one-based, ascending, and
	/// refer to the current document. Reads report the absence of bookmarks instead of throwing: a document
	/// provider that returns <see langword="null"/> yields an empty list, so a margin's render pass cannot fail.
	/// </remarks>
	public IReadOnlyList<int> GetMarkedLineNumbers()
	{
		TextDocument? document = _documentProvider();

		if (document is null)
			return [];

		return BookmarksApplyTo(document)
			? GetMarkedLineNumbersCached(document)
			: [];
	}

	/// <inheritdoc/>
	/// <remarks>
	/// One toggle always clears the bookmark for a line: anchors that resolve to the same line, which
	/// can happen after edits merge bookmarked lines, are collapsed during bookmark mutations so the
	/// visible bookmarks and the anchors stay in sync.
	/// </remarks>
	/// <exception cref="InvalidOperationException">The document provider returns <see langword="null"/>.</exception>
	public void ToggleBookmark(int offset)
	{
		TextDocument document = ReconcileDocument();

		PruneInvalidAnchors(document);

		DocumentLine currentLine = document.GetLineByOffset(document.ClampOffset(offset));
		TextAnchor? bookmarkAnchor = FindBookmarkAnchor(document, currentLine);

		if (bookmarkAnchor is null)
			AddBookmark(document, currentLine);
		else
			_bookmarkAnchors.Remove(bookmarkAnchor);

		RaiseChanged();
	}

	/// <summary>
	/// Gets the first bookmarked line after the line containing the offset,
	/// wrapping to the first bookmark in the document when necessary.
	/// </summary>
	/// <param name="offset">
	/// The zero-based document offset whose containing line is the starting point. Values outside the document are clamped.
	/// </param>
	/// <returns>The next bookmarked line, or <see langword="null"/> when none exist.</returns>
	/// <remarks>
	/// Reads report the absence of bookmarks instead of throwing: a document provider that returns
	/// <see langword="null"/> yields <see langword="null"/> as well.
	/// </remarks>
	public DocumentLine? GetNextBookmarkLine(int offset)
		=> GetAdjacentBookmarkLine(offset, searchForward: true);

	/// <summary>
	/// Gets the last bookmarked line before the line containing the offset,
	/// wrapping to the last bookmark in the document when necessary.
	/// </summary>
	/// <param name="offset">
	/// The zero-based document offset whose containing line is the starting point. Values outside the document are clamped.
	/// </param>
	/// <returns>The previous bookmarked line, or <see langword="null"/> when none exist.</returns>
	/// <remarks>
	/// Reads report the absence of bookmarks instead of throwing: a document provider that returns
	/// <see langword="null"/> yields <see langword="null"/> as well.
	/// </remarks>
	public DocumentLine? GetPreviousBookmarkLine(int offset)
		=> GetAdjacentBookmarkLine(offset, searchForward: false);

	/// <summary>
	/// Removes all bookmarks of the document the provider currently returns.
	/// </summary>
	/// <remarks>
	/// The coordinator is bound to that document first, so the clear covers the current document and a
	/// subsequent save persists an empty set for it, which deletes the persisted bookmarks.
	/// Raises <see cref="Changed"/>, even when no bookmarks exist.
	/// </remarks>
	/// <exception cref="InvalidOperationException">The document provider returns <see langword="null"/>.</exception>
	public void ClearBookmarks()
	{
		// Resolve the current document first: a clear belongs to the document being edited, so a later
		// save persists the clear for that document instead of failing as unbound.
		_ = ReconcileDocument();

		_bookmarkAnchors.Clear();
		RaiseChanged();
	}

	/// <summary>
	/// Replaces the current bookmarks with bookmarks for the supplied one-based line numbers.
	/// Line numbers outside the document and duplicate entries are ignored.
	/// </summary>
	/// <remarks>
	/// The document is resolved before the current bookmarks are replaced, so a provider failure
	/// leaves the existing bookmarks intact. The stored numbers are matched against the current
	/// document, so a restore re-creates bookmarks by line number rather than by the original content.
	/// </remarks>
	/// <param name="lineNumbers">The one-based line numbers to bookmark.</param>
	/// <exception cref="ArgumentNullException"><paramref name="lineNumbers"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The document provider returns <see langword="null"/>.</exception>
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
	/// Finds the next or previous bookmarked line relative to the line containing the offset, wrapping
	/// around at the ends of the document.
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

		if (searchForward)
		{
			foreach (int lineNumber in bookmarkedLineNumbers)
			{
				if (lineNumber > currentLineNumber)
					return document.GetLineByNumber(lineNumber);
			}

			return document.GetLineByNumber(bookmarkedLineNumbers[0]);
		}

		for (int index = bookmarkedLineNumbers.Count - 1; index >= 0; index--)
		{
			if (bookmarkedLineNumbers[index] < currentLineNumber)
				return document.GetLineByNumber(bookmarkedLineNumbers[index]);
		}

		return document.GetLineByNumber(bookmarkedLineNumbers[^1]);
	}

	/// <summary>
	/// Gets the document currently returned by the provider.
	/// </summary>
	/// <returns>The current document.</returns>
	/// <exception cref="InvalidOperationException">The document provider returns <see langword="null"/>.</exception>
	private TextDocument GetCurrentDocument()
	{
		return _documentProvider()
			?? throw new InvalidOperationException("The document provider returned null.");
	}

	/// <summary>
	/// Gets the current document for persisting bookmarks, refusing to persist when the coordinator's
	/// bookmarks are not bound to it.
	/// </summary>
	/// <remarks>
	/// Persisting a coordinator whose anchors belong to a different document (or to no document yet)
	/// would store an empty set, which deletes the persisted bookmarks. This check turns that state into
	/// a failure the host can see instead of data loss.
	/// </remarks>
	/// <returns>The document the current bookmarks are bound to.</returns>
	/// <exception cref="InvalidOperationException">
	/// The document provider returns <see langword="null"/>, or the bookmarks are not bound to the
	/// document the provider currently returns. Restore bookmarks for the current document before saving.
	/// </exception>
	internal TextDocument GetDocumentForPersistence()
	{
		TextDocument document = GetCurrentDocument();

		if (_bookmarkDocument is null || !ReferenceEquals(_bookmarkDocument, document))
		{
			throw new InvalidOperationException(
				"Bookmarks are not bound to the current document. Restore bookmarks for the current document before saving.");
		}

		return document;
	}

	/// <summary>
	/// Reports whether the coordinator's bookmarks belong to the supplied document.
	/// </summary>
	/// <param name="document">The document to test.</param>
	/// <returns>
	/// <see langword="true"/> when the bookmarks are bound to the document or none are anchored yet;
	/// otherwise, <see langword="false"/>.
	/// </returns>
	private bool BookmarksApplyTo(TextDocument document)
		=> _bookmarkDocument is null || ReferenceEquals(_bookmarkDocument, document);

	/// <summary>
	/// Rebinds the coordinator to the document currently returned by the provider,
	/// discarding the anchors when that document differs from the one that holds them.
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
	/// Gets the cached line numbers for the supplied document, collecting them when the document version
	/// or the bookmark set changed.
	/// </summary>
	/// <param name="document">The document to read.</param>
	/// <returns>The cached or freshly collected line numbers.</returns>
	private IReadOnlyList<int> GetMarkedLineNumbersCached(TextDocument document)
		=> _bookmarkedLinesCache.GetOrCreate(document, CollectBookmarkedLineNumbers);

	/// <summary>
	/// Resolves every anchor to its current line and collects the de-duplicated, ascending line numbers.
	/// </summary>
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
	/// Removes anchors that collapsed onto a line that another anchor already resolves to, so the anchor
	/// list matches the de-duplicated bookmarked lines.
	/// </summary>
	private void PruneInvalidAnchors(TextDocument document)
	{
		var seenLineNumbers = new HashSet<int>();

		_bookmarkAnchors.RemoveAll(anchor => !seenLineNumbers.Add(GetBookmarkedLine(document, anchor).LineNumber));
	}

	/// <summary>
	/// Creates a bookmark anchor for the supplied line and binds the coordinator to the document when it
	/// was unbound.
	/// </summary>
	/// <param name="document">The document that holds the new anchor.</param>
	/// <param name="line">The line to mark.</param>
	private void AddBookmark(TextDocument document, DocumentLine line)
	{
		// The anchor marks the start of the line and follows its content: the default movement moves
		// the anchor behind text inserted at the line start (for example a line break), so the bookmark
		// stays on the content instead of remaining on the inserted text.
		TextAnchor anchor = document.CreateAnchor(line.Offset);
		anchor.MovementType = AnchorMovementType.Default;
		anchor.SurviveDeletion = true;

		_bookmarkAnchors.Add(anchor);
		_bookmarkDocument = document;
	}

	/// <summary>
	/// Finds the anchor that currently resolves to the supplied line.
	/// </summary>
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

	/// <summary>
	/// Resolves an anchor to the line it currently marks.
	/// </summary>
	/// <remarks>
	/// Anchors are created with <see cref="TextAnchor.SurviveDeletion"/> set, so they are never flagged as
	/// deleted; the offset is clamped because a surviving anchor can sit one position past the end of the
	/// document after a deletion.
	/// </remarks>
	private static DocumentLine GetBookmarkedLine(TextDocument document, TextAnchor anchor)
	{
		int offset = document.ClampOffset(anchor.Offset);
		return document.GetLineByOffset(offset);
	}
}

#if AVALONIAEDIT
using AvaloniaEdit.Document;
#else
using ICSharpCode.AvalonEdit.Document;
#endif
using System.Runtime.CompilerServices;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Editing;
#else
namespace Nickelony.IDEKit.AvalonEdit.Editing;
#endif

/// <summary>
/// Tracks the closing texts an auto-closing service inserted, per document, so the service's default
/// provenance modes recognize only the closing text it inserted itself. The state is keyed by the
/// document and released with it.
/// </summary>
/// <remarks>
/// An anchor keeps a recorded closing text's offset valid across edits, and a recorded text stops
/// qualifying as soon as its text is no longer present at the anchor (for example after the user
/// deleted or overwrote it). Removing the inserted closing text therefore ends its tracking, and a
/// later redo of that insertion does not restore it.
/// </remarks>
internal sealed class AutoClosingInsertionTracker
{
	private readonly ConditionalWeakTable<TextDocument, DocumentTracking> _tracking = new();

	/// <summary>
	/// Gets the resolver callback over the recorded insertions for <paramref name="document"/>, or
	/// <see langword="null"/> when there is no document or it has no tracked closing texts.
	/// </summary>
	/// <param name="document">The document whose tracking callback is requested, or <see langword="null"/>.</param>
	/// <returns>The tracking callback, or <see langword="null"/> when nothing is tracked.</returns>
	public Func<int, bool>? GetResolverCallback(TextDocument? document)
		=> document is not null && _tracking.TryGetValue(document, out DocumentTracking? tracking)
			? tracking.IsTrackedClosingText
			: null;

	/// <summary>
	/// Records a closing text the service inserted, so the default provenance modes recognize it later.
	/// </summary>
	/// <param name="document">The document the closing text was inserted into.</param>
	/// <param name="offset">The offset at which the inserted closing text starts.</param>
	/// <param name="closingText">The inserted closing text.</param>
	public void Track(TextDocument document, int offset, string closingText)
	{
		DocumentTracking tracking = _tracking.GetValue(document, static key => new DocumentTracking(key));

		// Entries that no longer mark their inserted text are retired as the list is extended, so the list
		// stays bounded by the closing texts that are still present in the document instead of growing for
		// the whole document session.
		tracking.RetireStaleEntries();

		// The anchor marks the start of the inserted closing text and must follow it across edits. The
		// default movement moves an anchor behind text inserted exactly at its position, which is where
		// the caret sits while the user types inside the pair; a BeforeInsertion anchor would stay in
		// front of the typed text and stop marking the closing text.
		TextAnchor anchor = document.CreateAnchor(offset);
		anchor.MovementType = AnchorMovementType.Default;

		tracking.Anchors.Add(new TrackedClosingText(anchor, closingText));
	}

	/// <summary>
	/// Gets the number of closing texts currently tracked for <paramref name="document"/>.
	/// </summary>
	/// <remarks>
	/// Test seam; not part of the supported surface. Stale entries are retired before the count is read,
	/// so the value reflects the closing texts that are still present.
	/// </remarks>
	/// <param name="document">The document to inspect.</param>
	/// <returns>The number of tracked closing texts.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
	public int GetTrackedCount(TextDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);

		if (!_tracking.TryGetValue(document, out DocumentTracking? tracking))
			return 0;

		tracking.RetireStaleEntries();

		return tracking.Anchors.Count;
	}

	/// <summary>
	/// The per-document tracking state of the closing texts an auto-closing service inserted: the
	/// recorded closing texts and the resolver callback bound to them.
	/// </summary>
	private sealed class DocumentTracking
	{
		private readonly TextDocument _document;

		/// <summary>
		/// Initializes the tracking state; the resolver callback is created once here, so passing it on
		/// the typing path allocates nothing beyond this state.
		/// </summary>
		/// <param name="document">The tracked document.</param>
		public DocumentTracking(TextDocument document)
		{
			_document = document;
			Anchors = [];
			IsTrackedClosingText = IsTrackedAt;
		}

		/// <summary>
		/// Gets the closing texts an auto-closing service inserted, in insertion order.
		/// </summary>
		public List<TrackedClosingText> Anchors { get; }

		/// <summary>
		/// Gets the resolver callback that reports whether a recorded closing text starts at the
		/// supplied offset and is still present.
		/// </summary>
		public Func<int, bool> IsTrackedClosingText { get; }

		/// <summary>
		/// Determines whether a recorded closing text starts at the supplied offset and is still present,
		/// so the default provenance modes recognize it.
		/// </summary>
		/// <remarks>
		/// The list is walked from the most recent insertion backwards, because a probe almost always
		/// targets the closing text that was just inserted. The tip is tested before the retirement scan,
		/// so the common case reads only one closing text instead of the whole list.
		/// </remarks>
		private bool IsTrackedAt(int offset)
		{
			int lastIndex = Anchors.Count - 1;

			if (lastIndex >= 0 && Anchors[lastIndex].IsAt(offset, _document))
				return true;

			RetireStaleEntries();

			for (int index = Anchors.Count - 1; index >= 0; index--)
			{
				if (Anchors[index].Anchor.Offset == offset)
					return true;
			}

			return false;
		}

		/// <summary>
		/// Removes the recorded closing texts whose text is no longer present at their anchor, so a consumed
		/// closing text stops being tracked and cannot be reported at a later offset.
		/// </summary>
		/// <remarks>
		/// The document is read on the document's owner thread, which is where the typing path and the resolver
		/// probe run.
		/// </remarks>
		public void RetireStaleEntries() => Anchors.RemoveAll(entry => !entry.IsPresent(_document));
	}

	/// <summary>
	/// One recorded closing text: the anchor that follows it and the text it marks.
	/// </summary>
	private sealed class TrackedClosingText
	{
		private readonly string _text;

		/// <summary>
		/// Initializes the entry.
		/// </summary>
		/// <param name="anchor">The anchor that follows the inserted closing text.</param>
		/// <param name="text">The inserted closing text.</param>
		public TrackedClosingText(TextAnchor anchor, string text)
		{
			Anchor = anchor;
			_text = text;
		}

		/// <summary>
		/// Gets the anchor that follows the inserted closing text.
		/// </summary>
		public TextAnchor Anchor { get; }

		/// <summary>
		/// Determines whether the recorded closing text starts at <paramref name="offset"/> and is still present.
		/// </summary>
		/// <param name="offset">The probed offset.</param>
		/// <param name="document">The tracked document.</param>
		/// <returns><see langword="true"/> when the recorded text still starts at the supplied offset.</returns>
		public bool IsAt(int offset, TextDocument document)
			=> !Anchor.IsDeleted && Anchor.Offset == offset && IsPresent(document);

		/// <summary>
		/// Determines whether the recorded closing text is still present at the anchor's current offset.
		/// </summary>
		/// <param name="document">The tracked document.</param>
		/// <returns><see langword="true"/> when the recorded text still starts at the anchor.</returns>
		public bool IsPresent(TextDocument document)
		{
			// IsDeleted must be checked first: reading Offset of a deleted anchor throws.
			if (Anchor.IsDeleted)
				return false;

			return DocumentTextMatch.MatchesAt(document, Anchor.Offset, _text);
		}
	}
}

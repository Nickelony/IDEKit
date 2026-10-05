using Nickelony.IDEKit.Core.Editing;
#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Editing;

#else

namespace Nickelony.IDEKit.AvalonEdit.Editing;

#endif

/// <summary>Applies prepared text-edit batches to the document of the editor's <see cref="TextEditor"/>.</summary>
/// <remarks>
/// A batch with at least one text-changing operation is applied as one replacement
/// of the span from its lowest to its highest changed offset, with an offset change map that holds one entry per operation, so the batch is one undo
/// step and raises <see cref="TextDocument.Changed"/> once, while anchors, the caret and the selection still move per operation. An all-no-op batch
/// is skipped entirely. The batch is a <see cref="PreparedTextEdits"/> instance, so it is validated on construction, and a document-level
/// failure (for example an out-of-range offset) surfaces before the document changes. The target tracks the editor's current document
/// through <see cref="CaptureVersion"/> without subscribing to its events, so it adds no event handler to the editor or its document.
/// </remarks>
public sealed class TextEditorEditTarget : ITextEditVersionTarget
{
	private readonly TextEditor _editor;

	private TextDocument? _trackedDocument;
	private ITextSourceVersion? _trackedVersion;
	private bool _isInitialized;

	/// <param name="editor">The editor whose document this target updates.</param>
	/// <exception cref="ArgumentNullException"><paramref name="editor"/> is <see langword="null"/>.</exception>
	public TextEditorEditTarget(TextEditor editor)
	{
		ArgumentNullException.ThrowIfNull(editor);
		_editor = editor;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The editor's current text; an editor without a document has no text, so the read reports an empty string.
	/// </remarks>
	public string Text => _editor.Document?.Text ?? string.Empty;

	/// <inheritdoc/>
	/// <remarks>
	/// A side-effect-free read of the stamp the last observation recorded. Because this target
	/// observes the editor only when <see cref="CaptureVersion"/> or an apply runs, a direct edit between two reads of this member
	/// is not reflected here; call <see cref="CaptureVersion"/> to observe it.
	/// </remarks>
	public long Version { get; private set; }

	/// <inheritdoc/>
	/// <remarks>
	/// Observes the editor's current document and advances the stamp when it differs from the last observed state:
	/// a direct edit, an edit through any target, an undo, or a document swap (including clearing the document) all advance it, while the first
	/// call only initializes the state, leaving the initial stamp at zero. The observation never throws, so a target whose editor has no document
	/// can still be compared, and it runs on the editor's thread like every other access to the target.
	/// </remarks>
	public long CaptureVersion()
	{
		ObserveDocumentState();
		return Version;
	}

	/// <summary>Observes the editor's current document and advances the stamp when its state differs from the last observed one.</summary>
	/// <remarks>
	/// The stamp advances on any edit, undo, document swap, or different current document (or no document), because a batch prepared
	/// against the earlier document does not apply to the current one.
	/// </remarks>
	private void ObserveDocumentState()
	{
		TextDocument? document = _editor.Document;

		if (!_isInitialized)
		{
			_isInitialized = true;
			_trackedDocument = document;
			_trackedVersion = document?.Version;
			return;
		}

		if (!ReferenceEquals(_trackedDocument, document))
		{
			_trackedDocument = document;
			_trackedVersion = document?.Version;
			Version++;
			return;
		}

		if (document is not null && !ReferenceEquals(_trackedVersion, document.Version))
		{
			_trackedVersion = document.Version;
			Version++;
		}
	}

	/// <inheritdoc/>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	/// <remarks>
	/// Operations that would change nothing (<see cref="TextEditOperation.IsNoOp"/>) are skipped,
	/// so an all-no-op batch changes neither the document nor its undo stack and needs no document. Applying an applicable batch advances
	/// the stamp even when the target was never observed before, so the documented capture-compare workflow sees the edit. A document-level failure
	/// (for example an out-of-range offset) surfaces before the document changes, because the batch is one replacement.
	/// </remarks>
	public void Apply(PreparedTextEdits edits)
	{
		ArgumentNullException.ThrowIfNull(edits);

		var applicable = ApplicableOperations.Find(edits);

		if (applicable.Count == 0)
			return;

		TextDocument document = BeginApply();

		try
		{
			ReplaceBatch(document, edits, applicable);
		}
		finally
		{
			EndApply(document);
		}
	}

	/// <summary>Replaces the span that the batch changes in one document change, mapping anchors through one entry per operation.</summary>
	/// <param name="document">The document to change.</param>
	/// <param name="edits">The batch to apply.</param>
	/// <param name="applicable">The operations of the batch that change text, with the span they cover and their offset change map.</param>
	/// <remarks>
	/// The operations run from the highest offset to the lowest, so each map entry names its operation's offset in the unchanged text,
	/// and the replacement text is built in one pass over the unchanged text between the operations. A single operation is replaced directly.
	/// </remarks>
	private static void ReplaceBatch(TextDocument document, PreparedTextEdits edits, ApplicableOperations applicable)
	{
		if (applicable.Map is not { } map)
		{
			// Without a map the batch holds exactly one operation that changes text.
			TextEditOperation only = applicable.First!;

			document.Replace(only.Range.Offset, only.Range.Length, only.NewText);
			return;
		}

		var replacement = new System.Text.StringBuilder();

		int cursor = applicable.SpanStart;

		for (int index = edits.Operations.Count - 1; index >= 0; index--)
		{
			TextEditOperation operation = edits.Operations[index];

			if (operation.IsNoOp)
				continue;

			replacement.Append(document.GetText(cursor, operation.Range.Offset - cursor)).Append(operation.NewText);
			cursor = operation.Range.EndOffset;
		}

		replacement.Append(document.GetText(cursor, applicable.SpanEnd - cursor));

		document.Replace(
			offset: applicable.SpanStart,
			length: applicable.SpanEnd - applicable.SpanStart,
			text: replacement.ToString(),
			offsetChangeMap: map
		);
	}

	/// <summary>
	/// Applies a single operation to the editor's document without the <see cref="PreparedTextEdits"/> carrier
	/// that <see cref="Apply(PreparedTextEdits)"/> takes.
	/// </summary>
	/// <param name="operation">The operation to apply.</param>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	/// <remarks>
	/// This is the single-operation fast path for the editing hot paths (typing, auto-closing, and backspace), where the caller
	/// always has exactly one operation and no batch to map. It applies the operation under the same document update, undo grouping, no-op skip,
	/// and version-stamp policy as <see cref="Apply(PreparedTextEdits)"/>. The caller owns the operation and may discard it after the call.
	/// </remarks>
	internal void ApplySingle(TextEditOperation operation)
	{
		if (operation.IsNoOp)
			return;

		TextDocument document = BeginApply();

		try
		{
			document.Replace(operation.Range.Offset, operation.Range.Length, operation.NewText);
		}
		finally
		{
			EndApply(document);
		}
	}

	/// <summary>
	/// Begins an apply: resolves the target document, records the pre-edit version, and opens the document update, which also groups the changes
	/// into one undo step.
	/// </summary>
	/// <returns>The document the apply targets.</returns>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	/// <remarks>
	/// The document is resolved once per apply: a <see cref="TextDocument.Changed"/> handler that swaps the editor's document must not leave
	/// this document's update open or route later operations to the new document.
	/// </remarks>
	private TextDocument BeginApply()
	{
		TextDocument document = TextEditorValidation.RequireDocument(_editor);

		// The pre-edit state is recorded first, so the post-edit observation sees this edit even when
		// the target was never observed before.
		ObserveDocumentState();
		document.BeginUpdate();

		return document;
	}

	/// <summary>Ends an apply started by <see cref="BeginApply"/>, closing the document update and observing the post-edit state.</summary>
	/// <param name="document">The document returned by <see cref="BeginApply"/>.</param>
	/// <remarks>
	/// The observation runs even when an operation failed: earlier operations may already have changed the document, and the stamp must follow them.
	/// </remarks>
	private void EndApply(TextDocument document)
	{
		document.EndUpdate();
		ObserveDocumentState();
	}

	/// <summary>Determines whether the batch contains at least one operation that changes text.</summary>
	/// <param name="edits">The batch to inspect.</param>
	/// <returns><see langword="true"/> when at least one operation is not a no-op; otherwise, <see langword="false"/>.</returns>
	private static bool HasApplicableOperation(PreparedTextEdits edits)
	{
		foreach (TextEditOperation operation in edits.Operations)
		{
			if (!operation.IsNoOp)
				return true;
		}

		return false;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The version check and the apply run in one call on the editor's thread, so a batch prepared against a stale stamp can never be applied,
	/// and a successful apply advances the stamp as the document changes. An editor without a document reports <see langword="false"/> for a batch
	/// with applicable operations, matching the Try convention of the editing helpers, while a batch without applicable operations needs no document
	/// and reports success, mirroring <see cref="Apply"/>.
	/// </remarks>
	public bool TryApply(PreparedTextEdits edits, long expectedVersion)
	{
		ArgumentNullException.ThrowIfNull(edits);

		if (_editor.Document is null)
			return !HasApplicableOperation(edits);

		if (CaptureVersion() != expectedVersion)
			return false;

		Apply(edits);
		return true;
	}

	/// <summary>The operations of a batch that change text, found in one pass over the batch.</summary>
	private readonly struct ApplicableOperations
	{
		private ApplicableOperations(int count, TextEditOperation? first, int spanStart, int spanEnd, OffsetChangeMap? map)
		{
			Count = count;
			First = first;
			SpanStart = spanStart;
			SpanEnd = spanEnd;
			Map = map;
		}

		/// <summary>The number of operations that change text.</summary>
		internal int Count { get; }

		/// <summary>The first operation that changes text, or <see langword="null"/> when none does.</summary>
		internal TextEditOperation? First { get; }

		/// <summary>The lowest offset that an operation changing text starts at.</summary>
		internal int SpanStart { get; }

		/// <summary>The highest offset that an operation changing text ends at.</summary>
		internal int SpanEnd { get; }

		/// <summary>
		/// The offset change map with one entry per operation that changes text in batch order, or <see langword="null"/> when fewer than two do.
		/// </summary>
		internal OffsetChangeMap? Map { get; }

		/// <summary>Finds the operations of a batch that change text.</summary>
		/// <param name="edits">The batch to inspect.</param>
		/// <returns>The operations that change text, with the span they cover and their offset change map.</returns>
		internal static ApplicableOperations Find(PreparedTextEdits edits)
		{
			TextEditOperation? first = null;
			OffsetChangeMap? map = null;

			int count = 0;
			int spanStart = int.MaxValue;
			int spanEnd = 0;

			foreach (TextEditOperation operation in edits.Operations)
			{
				if (operation.IsNoOp)
					continue;

				count++;
				spanStart = Math.Min(spanStart, operation.Range.Offset);
				spanEnd = Math.Max(spanEnd, operation.Range.EndOffset);

				if (first is null)
				{
					first = operation;
					continue;
				}

				// A single operation needs no map, so the map starts at the second one and takes the first one in before it.
				map ??= [CreateEntry(first)];

				map.Add(CreateEntry(operation));
			}

			return new ApplicableOperations(count, first, spanStart, spanEnd, map);
		}

		private static OffsetChangeMapEntry CreateEntry(TextEditOperation operation) =>
			new(operation.Range.Offset, operation.Range.Length, operation.NewText.Length);
	}
}

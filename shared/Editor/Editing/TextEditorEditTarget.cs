#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
#endif
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Editing;
#else
namespace Nickelony.IDEKit.AvalonEdit.Editing;
#endif

/// <summary>
/// Applies prepared text-edit batches to the document of the editor's <see cref="TextEditor"/>.
/// </summary>
/// <remarks>
/// <para>
/// A batch that contains at least one operation that changes text is applied inside one document update
/// and one undo step; a batch whose operations all change nothing is skipped entirely.
/// The per-change <see cref="TextDocument.Changed"/> event is raised once per operation, while the
/// aggregate events (<see cref="TextDocument.TextChanged"/> and property changes) are raised once when
/// the update ends.
/// </para>
/// <para>
/// The batch is a <see cref="PreparedTextEdits"/> instance, so it is validated when it is
/// constructed and cannot silently corrupt the document through a wrong order or a
/// <see langword="null"/> replacement text. A document-level failure (for example an out-of-range
/// offset) still surfaces while the batch is applied, and earlier operations may already be
/// applied.
/// </para>
/// <para>
/// The target tracks the editor's current document through <see cref="Version"/> without subscribing
/// to its events: the stamp compares the document's version object, which the editor replaces on
/// every change. The target holds no event subscriptions, so it never keeps the editor or its
/// document alive.
/// </para>
/// </remarks>
public sealed class TextEditorEditTarget : IVersionedTextEditTarget
{
	private readonly TextEditor _editor;

	private TextDocument? _trackedDocument;
	private ITextSourceVersion? _trackedVersion;
	private bool _initialized;
	private long _version;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextEditorEditTarget"/> class.
	/// </summary>
	/// <param name="editor">The editor whose document this target updates.</param>
	/// <exception cref="ArgumentNullException"><paramref name="editor"/> is <see langword="null"/>.</exception>
	public TextEditorEditTarget(TextEditor editor)
	{
		ArgumentNullException.ThrowIfNull(editor);
		_editor = editor;
	}

	/// <inheritdoc/>
	/// <remarks>
	/// The editor's current text; an editor without a document has no text, so the read reports an
	/// empty string.
	/// </remarks>
	public string Text => _editor.Document?.Text ?? string.Empty;

	/// <inheritdoc/>
	/// <remarks>
	/// Each read observes the editor's current document and advances the stamp when it differs from the
	/// last observed state: an edit made directly, through this target, through another target, or by an
	/// undo, and a document swap - including clearing the document - all advance it. The first read only
	/// initializes the state, so the initial stamp is zero. The read never throws, so a target whose
	/// editor has no document can still be compared. Like every other access to the target, the read
	/// runs on the editor's thread.
	/// </remarks>
	public long Version
	{
		get
		{
			ObserveDocumentState();
			return _version;
		}
	}

	/// <summary>
	/// Observes the editor's current document and advances the stamp when its state differs from the
	/// last observed one. The first observation initializes the state without advancing.
	/// </summary>
	/// <remarks>
	/// The stamp compares the document's <see cref="TextDocument.Version"/> object by reference:
	/// The editor replaces the version object on every change, so any edit is observed without a change
	/// subscription. A different current document (or no document) advances the stamp as well, because a
	/// batch prepared against the previous document is no longer valid.
	/// </remarks>
	private void ObserveDocumentState()
	{
		TextDocument? document = _editor.Document;

		if (!_initialized)
		{
			_initialized = true;
			_trackedDocument = document;
			_trackedVersion = document?.Version;
			return;
		}

		if (!ReferenceEquals(_trackedDocument, document))
		{
			_trackedDocument = document;
			_trackedVersion = document?.Version;
			_version++;
			return;
		}

		if (document is not null && !ReferenceEquals(_trackedVersion, document.Version))
		{
			_trackedVersion = document.Version;
			_version++;
		}
	}

	/// <inheritdoc/>
	/// <remarks>
	/// Operations that would change nothing (<see cref="TextEditOperation.IsNoOp"/>) are skipped, so an
	/// all-no-op batch changes neither the document nor its undo stack and needs no document. Applying an
	/// applicable batch advances the stamp even when the target was never observed before, so the
	/// documented capture-compare workflow sees the edit. A document-level failure (for example an
	/// out-of-range offset) still surfaces while the batch is applied, and earlier operations may already
	/// be applied; the stamp advances for those changes as well.
	/// </remarks>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	public void Apply(PreparedTextEdits edits)
	{
		ArgumentNullException.ThrowIfNull(edits);

		if (!HasApplicableOperation(edits))
			return;

		TextDocument document = BeginApply();

		try
		{
			foreach (TextEditOperation operation in edits.Operations)
			{
				if (operation.IsNoOp)
					continue;

				document.Replace(operation.StartOffset, operation.Length, operation.NewText);
			}
		}
		finally
		{
			EndApply(document);
		}
	}

	/// <summary>
	/// Applies a single operation to the editor's document without the <see cref="PreparedTextEdits"/>
	/// carrier that <see cref="Apply(PreparedTextEdits)"/> takes.
	/// </summary>
	/// <remarks>
	/// This is the single-operation fast path for the editing hot paths (typing, auto-closing, and
	/// backspace), where the caller always has exactly one operation and no batch to map. It applies the
	/// operation under the same document update, undo grouping, no-op skip, and version-stamp policy as
	/// <see cref="Apply(PreparedTextEdits)"/>. The caller owns the operation and no longer needs it after
	/// the call.
	/// </remarks>
	/// <param name="operation">The operation to apply.</param>
	/// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	internal void ApplySingle(TextEditOperation operation)
	{
		ArgumentNullException.ThrowIfNull(operation);

		if (operation.IsNoOp)
			return;

		TextDocument document = BeginApply();

		try
		{
			document.Replace(operation.StartOffset, operation.Length, operation.NewText);
		}
		finally
		{
			EndApply(document);
		}
	}

	/// <summary>
	/// Begins an apply: resolves the target document, records the pre-edit version, and opens the document
	/// update, which also groups the changes into one undo step.
	/// </summary>
	/// <remarks>
	/// The document is resolved once per apply: a <see cref="TextDocument.Changed"/> handler that swaps the
	/// editor's document must not leave this document's update open or route later operations to the new
	/// document.
	/// </remarks>
	/// <returns>The document the apply targets.</returns>
	/// <exception cref="InvalidOperationException">The editor has no document assigned.</exception>
	private TextDocument BeginApply()
	{
		TextDocument document = _editor.Document
			?? throw new InvalidOperationException("The editor has no document assigned.");

		// The pre-edit state is recorded first, so the post-edit observation sees this edit even when
		// the target was never observed before.
		ObserveDocumentState();
		document.BeginUpdate();

		return document;
	}

	/// <summary>
	/// Ends an apply started by <see cref="BeginApply"/>, closing the document update and observing the
	/// post-edit state.
	/// </summary>
	/// <remarks>
	/// The observation runs even when an operation failed: earlier operations may already have changed the
	/// document, and the stamp must follow them.
	/// </remarks>
	/// <param name="document">The document returned by <see cref="BeginApply"/>.</param>
	private void EndApply(TextDocument document)
	{
		document.EndUpdate();
		ObserveDocumentState();
	}

	/// <summary>
	/// Determines whether the batch contains at least one operation that changes text.
	/// </summary>
	/// <param name="edits">The batch to inspect.</param>
	/// <returns>
	/// <see langword="true"/> when at least one operation is not a no-op; otherwise, <see langword="false"/>.
	/// </returns>
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
	/// The version check and the apply run in one call on the editor's thread, which is the only
	/// thread that may touch the target, so a batch prepared against a stale stamp can never be
	/// applied. The stamp advances when the document changes, so a successful apply also advances it.
	/// An editor without a document reports <see langword="false"/> instead of throwing for a batch
	/// with applicable operations, matching the Try convention of the editing helpers: there is no
	/// document to apply the batch to. A batch without applicable operations needs no document and
	/// reports success, mirroring <see cref="Apply"/>.
	/// </remarks>
	public bool TryApply(PreparedTextEdits edits, long expectedVersion)
	{
		ArgumentNullException.ThrowIfNull(edits);

		bool hasApplicableOperation = HasApplicableOperation(edits);

		if (_editor.Document is null)
			return !hasApplicableOperation;

		if (Version != expectedVersion)
			return false;

		Apply(edits);
		return true;
	}
}

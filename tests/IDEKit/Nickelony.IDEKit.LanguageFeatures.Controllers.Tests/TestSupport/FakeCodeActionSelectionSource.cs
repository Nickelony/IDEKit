using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Core.Threading;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>A selection source over a document text and a caret the test sets, which raises the change notifications a real editor would.</summary>
/// <remarks>
/// The source mimics an editor without one: a caret move, a selection, and a document edit raise <see cref="Changed"/>
/// like the editor's events do, and <see cref="ReplaceDocument"/> raises <see cref="DocumentReplaced"/> after it installed the new text.
/// It counts its captures, its disposals, and the handlers attached to its events, so a test sees what the controller does with it. Like a real
/// editor it accepts a read only on the thread of its dispatcher, so a controller that reads it elsewhere fails the test.
/// </remarks>
internal sealed class FakeCodeActionSelectionSource(IUiDispatcher dispatcher) : ICodeActionSelectionSource
{
	private EventHandler? _changed;
	private EventHandler? _documentReplaced;

	/// <summary>The document text the source reports; <see langword="null"/> means the editor has no document.</summary>
	public string? DocumentText { get; private set; } = "one\ntwo\nthree";

	/// <summary>The caret offset the source reports.</summary>
	public int CaretOffset { get; private set; }

	/// <summary>The selected range; an empty range at the caret when nothing is selected.</summary>
	public TextRange Selection { get; private set; }

	/// <summary>The number of times the controller captured the context.</summary>
	public int CaptureCount { get; private set; }

	/// <summary>The number of times the controller disposed the source.</summary>
	public int DisposeCount { get; private set; }

	/// <summary>The number of handlers currently attached to <see cref="Changed"/>.</summary>
	public int ChangedSubscriberCount => _changed?.GetInvocationList().Length ?? 0;

	/// <summary>The number of handlers currently attached to <see cref="DocumentReplaced"/>.</summary>
	public int DocumentReplacedSubscriberCount => _documentReplaced?.GetInvocationList().Length ?? 0;

	/// <summary>Holds an optional replacement of the capture, for example to throw the way an editor in an invalid state does.</summary>
	public Func<CodeActionSelection?>? CaptureOverride { get; set; }

	/// <summary>Holds an optional action that runs when the source is disposed, for example to throw the way a faulty host source does.</summary>
	public Action? DisposeOverride { get; set; }

	/// <inheritdoc/>
	public event EventHandler? Changed
	{
		add => _changed += value;
		remove => _changed -= value;
	}

	/// <inheritdoc/>
	public event EventHandler? DocumentReplaced
	{
		add => _documentReplaced += value;
		remove => _documentReplaced -= value;
	}

	/// <inheritdoc/>
	public int? CaretLineNumber
	{
		get
		{
			dispatcher.VerifyAccess();

			if (DocumentText is null)
				return null;

			int clamped = Math.Clamp(CaretOffset, 0, DocumentText.Length);

			return 1 + DocumentText.AsSpan(0, clamped).Count('\n');
		}
	}

	/// <inheritdoc/>
	public CodeActionSelection? Capture()
	{
		dispatcher.VerifyAccess();
		CaptureCount++;

		if (CaptureOverride is { } capture)
			return capture();

		return DocumentText is null ? null : new CodeActionSelection(new StringTextSnapshot(DocumentText), CaretOffset, Selection);
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		DisposeCount++;
		DisposeOverride?.Invoke();
	}

	/// <summary>Moves the caret and collapses the selection, the way a caret move in the editor does, and raises <see cref="Changed"/>.</summary>
	/// <param name="offset">The new caret offset.</param>
	public void MoveCaret(int offset)
	{
		CaretOffset = offset;
		Selection = new TextRange(offset, 0);

		_changed?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>Selects a range with the caret at its start and raises <see cref="Changed"/>.</summary>
	/// <param name="offset">The start of the selection.</param>
	/// <param name="length">The length of the selection.</param>
	public void Select(int offset, int length)
	{
		CaretOffset = offset;
		Selection = new TextRange(offset, length);

		_changed?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>Replaces the text of the shown document and raises <see cref="Changed"/>, the way an edit does.</summary>
	/// <param name="text">The new text.</param>
	public void EditDocument(string text)
	{
		DocumentText = text;
		CaretOffset = Math.Min(CaretOffset, text.Length);
		Selection = new TextRange(CaretOffset, 0);

		_changed?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>Shows another document (or none) and raises <see cref="DocumentReplaced"/> after the new document is in place.</summary>
	/// <param name="text">The text of the new document, or <see langword="null"/> for an editor without a document.</param>
	public void ReplaceDocument(string? text)
	{
		DocumentText = text;
		CaretOffset = 0;
		Selection = new TextRange(0, 0);

		_documentReplaced?.Invoke(this, EventArgs.Empty);
	}
}

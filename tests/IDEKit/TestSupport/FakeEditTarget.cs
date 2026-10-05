using Nickelony.IDEKit.Core.Editing;
#if AVALONIAEDIT
using AvaloniaEdit;
#else
using ICSharpCode.AvalonEdit;
#endif

namespace Nickelony.IDEKit.Testing;

/// <summary>
/// An <see cref="ITextEditTarget"/> test double that honors the edit-target contract: it applies the operations to its own content and publishes
/// them to the editor document before returning.
/// </summary>
/// <remarks>
/// Use this double where the contract matters, for example caret or selection behavior derived from the editor document after the edit.
/// </remarks>
internal sealed class FakeEditTarget : ITextEditTarget
{
	private readonly TextEditor _editor;
	private readonly List<TextEditOperation> _operations = [];

	/// <param name="editor">The editor whose document content is reported and updated.</param>
	public FakeEditTarget(TextEditor editor)
	{
		ArgumentNullException.ThrowIfNull(editor);

		_editor = editor;
		Text = editor.Text;
	}

	/// <summary>The operations of every applied batch, in application order.</summary>
	public IReadOnlyList<TextEditOperation> Operations => _operations;

	/// <inheritdoc/>
	public string Text { get; private set; }

	/// <inheritdoc/>
	public void Apply(PreparedTextEdits edits)
	{
		ArgumentNullException.ThrowIfNull(edits);

		// Operations are ordered from highest to lowest source offset, so applying them in list order
		// keeps every remaining operation's offsets valid in both the string and the document.
		_editor.Document.BeginUpdate();

		try
		{
			foreach (TextEditOperation operation in edits.Operations)
			{
				_operations.Add(operation);

				Text = Text.Remove(operation.Range.Offset, operation.Range.Length).Insert(operation.Range.Offset, operation.NewText);
				_editor.Document.Replace(operation.Range.Offset, operation.Range.Length, operation.NewText);
			}
		}
		finally
		{
			_editor.Document.EndUpdate();
		}
	}
}

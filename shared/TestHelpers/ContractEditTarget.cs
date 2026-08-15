using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.Core.Editing;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Testing;

/// <summary>
/// An <see cref="ITextEditTarget"/> test double that honors the edit-target contract: it applies the
/// operations to its own content and publishes them to the editor document before returning.
/// </summary>
/// <remarks>
/// Use this double for tests where the contract matters, for example caret or selection behavior that
/// is derived from the editor document after the edit. <see cref="RecordingEditTarget"/> remains
/// available for tests that deliberately exercise the documented behavior of a target that does not
/// update the editor document.
/// </remarks>
internal sealed class ContractEditTarget : ITextEditTarget
{
	private readonly TextEditor _editor;
	private readonly List<TextEditOperation> _operations = [];

	private string _text;

	/// <summary>
	/// Initializes a new instance of the <see cref="ContractEditTarget"/> class bound to an editor.
	/// </summary>
	/// <param name="editor">The editor whose document content is reported and updated.</param>
	public ContractEditTarget(TextEditor editor)
	{
		ArgumentNullException.ThrowIfNull(editor);

		_editor = editor;
		_text = editor.Text;
	}

	/// <summary>
	/// Gets the operations of every applied batch, in application order.
	/// </summary>
	public IReadOnlyList<TextEditOperation> Operations => _operations;

	/// <inheritdoc/>
	public string Text => _text;

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

				_text = _text.Remove(operation.StartOffset, operation.Length).Insert(operation.StartOffset, operation.NewText);
				_editor.Document.Replace(operation.StartOffset, operation.Length, operation.NewText);
			}
		}
		finally
		{
			_editor.Document.EndUpdate();
		}
	}
}

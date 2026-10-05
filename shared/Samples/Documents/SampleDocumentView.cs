using Nickelony.IDEKit.Workspace.Views;
#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.Threading;
#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.Threading;
#endif

namespace Nickelony.Samples;

/// <summary>
/// Shows one workspace document in an editor and keeps the editor text in step with the document store through the view coordinator.
/// </summary>
internal sealed class SampleDocumentView : WorkspaceDocumentTextViewBase
{
	private readonly TextEditor _editor;

	/// <param name="viewId">The stable identifier of the view, which the coordinator reports in its results.</param>
	/// <param name="editor">The editor the view shows the document in.</param>
	/// <param name="publishDelay">
	/// How long typing pauses before the view publishes the editor text, or <see langword="null"/> to publish only when asked.
	/// </param>
	internal SampleDocumentView(string viewId, TextEditor editor, TimeSpan? publishDelay = null)
		: base(viewId, UiDispatcher.From(editor.Dispatcher), publishDelay)
	{
		_editor = editor;
		_editor.TextChanged += HandleEditorTextChanged;
	}

	protected override bool IsReadOnly
	{
		get => _editor.IsReadOnly;
		set => _editor.IsReadOnly = value;
	}

	protected override string ReadText() => _editor.Text;

	protected override void ReplaceText(string text) => _editor.ReplaceContent(text);

	protected override void Dispose(bool disposing)
	{
		if (disposing)
			_editor.TextChanged -= HandleEditorTextChanged;

		base.Dispose(disposing);
	}

	private void HandleEditorTextChanged(object? sender, EventArgs e) => HandleTextChanged();
}

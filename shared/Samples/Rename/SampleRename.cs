using Nickelony.IDEKit.LanguageFeatures.Editing;
using Nickelony.IDEKit.LanguageFeatures.Rename;
#if AVALONIAEDIT
using AvaloniaEdit;
#else
using ICSharpCode.AvalonEdit;
#endif

namespace Nickelony.Samples;

/// <summary>Asks a rename provider for the edits that rename the symbol at the caret of one editor.</summary>
/// <remarks>
/// The edits can name several documents, so they go to the document authority, which applies them through the view coordinator;
/// see <see cref="SampleDocuments.ApplyEditAsync"/>.
/// </remarks>
internal sealed class SampleRename
{
	private readonly TextEditor _editor;
	private readonly IRenameProvider? _provider;
	private readonly string _documentId;

	/// <param name="editor">The editor whose caret names the symbol.</param>
	/// <param name="provider">Answers the rename requests, or <see langword="null"/> when the document cannot rename.</param>
	/// <param name="documentId">The identity of the edited document, which every request carries.</param>
	internal SampleRename(TextEditor editor, IRenameProvider? provider, string documentId)
	{
		_editor = editor;
		_provider = provider;
		_documentId = documentId;
	}

	/// <summary>Asks for the edits that rename the symbol at the caret.</summary>
	/// <param name="newName">The name the symbol takes.</param>
	/// <returns>The edits, or <see langword="null"/> when no provider is configured or the caret is not on a symbol.</returns>
	internal Task<WorkspaceEdit?> RequestEditsAsync(string newName)
	{
		if (_provider is not { } provider)
			return Task.FromResult<WorkspaceEdit?>(null);

		return provider.GetRenameEditsAsync(
			new RenameRequest(documentId: _documentId, documentText: _editor.Text, symbolOffset: _editor.CaretOffset, newName)
		);
	}
}

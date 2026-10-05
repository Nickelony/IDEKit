using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Editing;
using Nickelony.IDEKit.LanguageFeatures.Rename;

namespace Nickelony.Samples;

/// <summary>Renames every occurrence of the dotted name at the caret in the document, which is all the sample language needs.</summary>
internal sealed class IdentifierRenameProvider : IRenameProvider
{
	public Task<WorkspaceEdit?> GetRenameEditsAsync(RenameRequest request, CancellationToken cancellationToken = default)
	{
		if (IdentifierScanner.FindName(request.DocumentText, request.SymbolOffset) is not { } symbol)
			return Task.FromResult<WorkspaceEdit?>(null);

		string name = symbol.GetText(request.DocumentText);
		var lineMap = TextLineMap.Build(request.DocumentText);
		List<TextEdit> edits = [];

		foreach (TextRange occurrence in IdentifierScanner.FindOccurrences(request.DocumentText, name))
		{
			edits.Add(
				new TextEdit(
					new TextPositionRange(lineMap.GetPosition(occurrence.Offset), lineMap.GetPosition(occurrence.EndOffset)),
					request.NewName
				)
			);
		}

		return Task.FromResult<WorkspaceEdit?>(new WorkspaceEdit([new TextDocumentEdit(request.DocumentId, edits)]));
	}
}

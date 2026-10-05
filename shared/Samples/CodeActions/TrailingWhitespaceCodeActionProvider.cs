using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Editing;

namespace Nickelony.Samples;

/// <summary>Offers a quick fix that trims the trailing whitespace of the line under the selection.</summary>
internal sealed class TrailingWhitespaceCodeActionProvider : ICodeActionProvider
{
	public Task<IReadOnlyList<CodeAction>> GetCodeActionsAsync(CodeActionRequest request, CancellationToken cancellationToken = default)
	{
		var lineMap = TextLineMap.Build(request.DocumentText);
		TextPosition start = lineMap.GetPosition(request.Range.Offset);

		string line = lineMap.GetLineText(start.Line);
		string trimmed = line.TrimEnd(' ', '\t');

		if (trimmed.Length == line.Length)
			return Task.FromResult<IReadOnlyList<CodeAction>>([]);

		var range = new TextPositionRange(new TextPosition(start.Line, trimmed.Length), new TextPosition(start.Line, line.Length));
		var edit = new WorkspaceEdit([new TextDocumentEdit(request.DocumentId, [new TextEdit(range, string.Empty)])]);

		return Task.FromResult<IReadOnlyList<CodeAction>>([
			new CodeAction(title: "Trim trailing whitespace", edit, kind: "quickfix", isPreferred: true),
		]);
	}
}

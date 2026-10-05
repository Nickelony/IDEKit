using Nickelony.IDEKit.LanguageFeatures.Editing;
using Nickelony.IDEKit.LanguageFeatures.Formatting;

#if AVALONIAEDIT
namespace Nickelony.Samples.Avalonia.Tests;

#else
namespace Nickelony.Samples.Wpf.Tests;

#endif

/// <summary>Answers a formatting request with one edit per line that removes the trailing spaces of the line.</summary>
internal sealed class FakeFormattingProvider : IFormattingProvider
{
	public Task<WorkspaceEdit?> GetFormattingEditsAsync(FormattingRequest request, CancellationToken cancellationToken = default)
	{
		List<TextEdit> edits = [];
		string[] lines = request.DocumentText.Split('\n');

		for (int line = 0; line < lines.Length; line++)
		{
			int trimmed = lines[line].TrimEnd(' ').Length;

			if (trimmed < lines[line].Length)
				edits.Add(new TextEdit(new(new(line, trimmed), new(line, lines[line].Length)), string.Empty));
		}

		return Task.FromResult<WorkspaceEdit?>(new WorkspaceEdit([new TextDocumentEdit(request.DocumentId, edits)]));
	}
}

using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Provides the document and the locations that the definition resolver tests share.</summary>
internal static class DefinitionTestHelpers
{
	internal const string DocumentId = "script.txt";

	internal const string DocumentText = "sample alpha = 1\nsample bravo = 2\nsample charlie = 3";

	internal static ITextSnapshot CreateSnapshot() => new StringTextSnapshot(DocumentText, DocumentId);

	internal static DefinitionLocation LocationAt(int oneBasedLine, int oneBasedColumn = 1, string? documentId = null)
	{
		return new(
			new TextPositionRange(new TextPosition(oneBasedLine - 1, oneBasedColumn - 1), new TextPosition(oneBasedLine - 1, oneBasedColumn - 1)),
			documentId
		);
	}
}

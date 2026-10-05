#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

public sealed partial class SemanticTokensColorizerTests
{
	[TestMethod]
	public void ApplyTokens_OnTheSecondLineOfACrLfDocument_AppliesToThatLine()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("one\r\ntwo", resolver);

		using (window)
		{
			// Offset 5 is the first character of the second line; the token is assigned to the line
			// containing its start offset, counting the CRLF delimiter as a single line break.
			Assert.IsTrue(colorizer.ApplyTokens([new SemanticToken(new TextRange(5, 3), SemanticTokenTypes.Variable)]));
			Assert.AreEqual(Brushes.Red, GetElementPropertiesOnVisualLine(editor, 2, 5).ForegroundBrush);
		}
	}

	private static VisualLineElementTextRunProperties GetElementPropertiesOnVisualLine(TextEditor editor, int lineNumber, int offset)
	{
		editor.TextArea.TextView.EnsureVisualLines();

		VisualLine visualLine =
			editor.TextArea.TextView.GetVisualLine(lineNumber) ?? throw new InvalidOperationException($"Visual line {lineNumber} is not available.");

		int relativeOffset = offset - visualLine.FirstDocumentLine.Offset;

		foreach (VisualLineElement element in visualLine.Elements)
		{
			if (relativeOffset < element.DocumentLength)
				return element.TextRunProperties;

			relativeOffset -= element.DocumentLength;
		}

		throw new InvalidOperationException($"No visual line element exists at offset {offset}.");
	}
}

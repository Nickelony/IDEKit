#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;
using Brush = Avalonia.Media.IBrush;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;
using Brush = System.Windows.Media.Brush;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

public sealed partial class SemanticTokensColorizerTests
{
	[TestMethod]
	public void ApplyTokens_InsertionBeforeAToken_MovesItsColorWithItsText()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Brush? unstyledForeground = GetPropertiesAt(editor, 0).ForegroundBrush;

			colorizer.ApplyTokens([new SemanticToken(new TextRange(6, 5), SemanticTokenTypes.Variable)]);

			editor.Document.Insert(0, "local ");

			// "value" now starts at offset 12, and the text that slid under its pushed offset stays unstyled.
			Assert.AreEqual(Brushes.Red, GetPropertiesAt(editor, 12).ForegroundBrush);
			Assert.AreEqual(Brushes.Red, GetPropertiesAt(editor, 16).ForegroundBrush);
			Assert.AreEqual(unstyledForeground, GetPropertiesAt(editor, 6).ForegroundBrush);
			Assert.AreEqual(unstyledForeground, GetPropertiesAt(editor, 17).ForegroundBrush);
		}
	}

	[TestMethod]
	public void ApplyTokens_SetForTheTextBeforeAnEdit_StaysOnItsWordsWhenALineIsTypedAbove()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("a = 1\nfinal value = 1", resolver);

		using (window)
		{
			Brush? unstyledForeground = GetPropertiesAt(editor, 0).ForegroundBrush;

			// The set describes the text before the edit: "value" on the second line.
			colorizer.ApplyTokens([new SemanticToken(new TextRange(12, 5), SemanticTokenTypes.Variable)]);

			editor.Document.Insert(0, "local b = 2\n");

			// The set is still the one pushed for the older text, and its color moved with "value" to the third line instead of painting the
			// line and character it was pushed for.
			int valueOffset = editor.Text.IndexOf("value", StringComparison.Ordinal);

			Assert.AreEqual(3, editor.Document.GetLineByOffset(valueOffset).LineNumber);
			Assert.AreEqual(Brushes.Red, GetPropertiesAt(editor, valueOffset).ForegroundBrush);
			Assert.AreEqual(unstyledForeground, GetPropertiesAt(editor, 12).ForegroundBrush);
		}
	}

	[TestMethod]
	public void ApplyTokens_ThenADocumentReplacement_ShowsNoStylingUntilTheNextPush()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Brush? unstyledForeground = GetPropertiesAt(editor, 0).ForegroundBrush;
			var token = new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable);

			colorizer.ApplyTokens([token]);
			editor.Document = new TextDocument("final value = 1");

			// The spans belong to the replaced document, so the new one is unstyled, and the same set is no repeat on it.
			Assert.AreEqual(unstyledForeground, GetPropertiesAt(editor, 0).ForegroundBrush);
			Assert.IsTrue(colorizer.ApplyTokens([token]));
			Assert.AreEqual(Brushes.Red, GetPropertiesAt(editor, 0).ForegroundBrush);
		}
	}

	[TestMethod]
	public void ApplyTokens_LineBreakTypedInsideAToken_KeepsItsColorOnTheStartLineOnly()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Brush? unstyledForeground = GetPropertiesAt(editor, 6).ForegroundBrush;

			colorizer.ApplyTokens([new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable)]);

			// The span now covers "fi", the line break and "nal", and it is painted on the line it starts on and clipped there.
			editor.Document.Insert(2, "\n");

			Assert.AreEqual(Brushes.Red, GetPropertiesAt(editor, 1).ForegroundBrush);
			Assert.AreEqual(unstyledForeground, GetPropertiesAt(editor, 3).ForegroundBrush);
			Assert.AreEqual(unstyledForeground, GetPropertiesAt(editor, 5).ForegroundBrush);
		}
	}

	[TestMethod]
	public void Rebuild_AfterTheWholeTextIsReplaced_PaintsNothing()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Brush? unstyledForeground = GetPropertiesAt(editor, 6).ForegroundBrush;

			colorizer.ApplyTokens([new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable)]);

			// Replacing the whole text deletes every token's text, so the spans are empty and the rebuild styles nothing.
			editor.Document.Text = "final value = 2";

			Assert.IsTrue(colorizer.Rebuild());
			Assert.AreEqual(unstyledForeground, GetPropertiesAt(editor, 0).ForegroundBrush);
		}
	}

	[TestMethod]
	public void ApplyTokens_TokenPastTheEndOrEmpty_IsNotResolved()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor _, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			colorizer.ApplyTokens([
				new SemanticToken(new TextRange(10_000, 5), SemanticTokenTypes.Variable),
				new SemanticToken(new TextRange(15, 0), SemanticTokenTypes.Variable),
				new SemanticToken(new TextRange(3, 0), SemanticTokenTypes.Variable),
				new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable),
			]);

			Assert.AreEqual(1, resolver.ResolveCallCount);
		}
	}

	[TestMethod]
	public void ApplyTokens_TokensWithOneStart_PaintTheLongerThenTheLaterOverTheOthers()
	{
		var resolver = new StubSemanticTokenStyleResolver();

		resolver.SetStyleFactory(token => new TextRunStyle(
			Foreground: token.Type switch
			{
				SemanticTokenTypes.Function => Brushes.Lime,
				SemanticTokenTypes.Method => Brushes.Blue,
				_ => Brushes.Red,
			},

			IsBold: false,
			IsItalic: false,
			TextDecorations: null
		));

		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			// The longer token is painted after the shorter one, so it wins where they overlap whatever their order in the set.
			colorizer.ApplyTokens([
				new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Function),
				new SemanticToken(new TextRange(0, 3), SemanticTokenTypes.Method),
			]);

			Assert.AreEqual(Brushes.Lime, GetPropertiesAt(editor, 1).ForegroundBrush);

			// With the same start and length, the later token in the set wins.
			colorizer.ApplyTokens([
				new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Method),
				new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Function),
			]);

			Assert.AreEqual(Brushes.Lime, GetPropertiesAt(editor, 1).ForegroundBrush);

			colorizer.ApplyTokens([
				new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Function),
				new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Method),
			]);

			Assert.AreEqual(Brushes.Blue, GetPropertiesAt(editor, 1).ForegroundBrush);
		}
	}

	// Reads the run properties of the element at an offset on any line, which the view lays out first.
	private static VisualLineElementTextRunProperties GetPropertiesAt(TextEditor editor, int offset)
	{
		editor.TextArea.TextView.EnsureVisualLines();

		int lineNumber = editor.Document.GetLineByOffset(offset).LineNumber;

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

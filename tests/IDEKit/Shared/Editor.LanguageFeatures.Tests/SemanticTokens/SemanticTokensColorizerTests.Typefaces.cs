#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;
using FontStyles = Avalonia.Media.FontStyle;
using FontWeights = Avalonia.Media.FontWeight;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;
using FontStyles = System.Windows.FontStyles;
using FontWeights = System.Windows.FontWeights;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

public sealed partial class SemanticTokensColorizerTests
{
	[TestMethod]
	public void ApplyTokens_BoldTokens_ReuseOneDerivedTypefaceAcrossPasses()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		resolver.SetStyle(Brushes.Red, isBold: true);

		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			colorizer.ApplyTokens([
				new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable),
				new SemanticToken(new TextRange(6, 5), SemanticTokenTypes.Variable),
			]);

			Typeface first = GetFirstElementProperties(editor).Typeface;

			Assert.AreEqual(FontWeights.Bold, first.Weight);

			for (int pass = 0; pass < 5; pass++)
			{
				editor.TextArea.TextView.Redraw();
				AssertSameTypeface(first, GetFirstElementProperties(editor).Typeface);
			}

			// Both tokens and every pass share the one typeface derived for the base typeface of the view.
			Assert.AreEqual(1, colorizer.DerivedTypefaceCount);
		}
	}

	[TestMethod]
	public void ApplyTokens_BoldAndItalicTokens_KeepOneDerivedTypefaceEach()
	{
		var resolver = new StubSemanticTokenStyleResolver();

		resolver.SetStyleFactory(token => new TextRunStyle(
			Foreground: Brushes.Red,
			IsBold: token.Type == SemanticTokenTypes.Variable,
			IsItalic: token.Type == SemanticTokenTypes.Function,
			TextDecorations: null
		));

		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			colorizer.ApplyTokens([
				new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable),
				new SemanticToken(new TextRange(6, 5), SemanticTokenTypes.Function),
			]);

			for (int pass = 0; pass < 3; pass++)
			{
				editor.TextArea.TextView.Redraw();

				Typeface bold = GetElementPropertiesAtOffset(editor, 0).Typeface;
				Typeface italic = GetElementPropertiesAtOffset(editor, 6).Typeface;

				Assert.AreEqual(FontWeights.Bold, bold.Weight);
				Assert.AreEqual(FontStyles.Italic, italic.Style);
			}

			Assert.AreEqual(2, colorizer.DerivedTypefaceCount);
		}
	}

	[TestMethod]
	public void ApplyTokens_NoBoldOrItalicToken_DerivesNoTypeface()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		resolver.SetStyle(Brushes.Red);

		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			colorizer.ApplyTokens([new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable)]);

			_ = GetFirstElementProperties(editor);

			Assert.AreEqual(0, colorizer.DerivedTypefaceCount);
		}
	}

	private static void AssertSameTypeface(Typeface expected, Typeface actual)
	{
#if AVALONIAEDIT
		Assert.AreEqual(expected, actual);
#else
		Assert.AreSame(expected, actual);
#endif
	}
}

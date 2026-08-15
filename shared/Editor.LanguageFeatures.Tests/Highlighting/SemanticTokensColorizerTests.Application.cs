#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Highlighting;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.SemanticTokens;
using Brush = Avalonia.Media.IBrush;
using FontStyles = Avalonia.Media.FontStyle;
using FontWeights = Avalonia.Media.FontWeight;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Highlighting;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.SemanticTokens;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using FontStyles = System.Windows.FontStyles;
using FontWeights = System.Windows.FontWeights;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

public sealed partial class SemanticTokensColorizerTests
{
	[TestMethod]
	public void SetTokens_AppliesResolvedStyles()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Brush? unstyledForeground = GetFirstElementProperties(editor).ForegroundBrush;

			Assert.IsTrue(colorizer.SetTokens([CreateToken(editor, 0, TextSemanticTokenTypes.Variable)]));

			// The resolver's style is applied to the document line the token covers.
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);
			Assert.AreNotEqual(unstyledForeground, GetFirstElementProperties(editor).ForegroundBrush);
		}
	}

	[TestMethod]
	public void ClearTokens_RestoresTheUnstyledForeground()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Brush? unstyledForeground = GetFirstElementProperties(editor).ForegroundBrush;

			Assert.IsTrue(colorizer.SetTokens([CreateToken(editor, 0, TextSemanticTokenTypes.Variable)]));
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);

			Assert.IsTrue(colorizer.ClearTokens());

			// Clearing the applied set restores the unstyled foreground of the line.
			Assert.AreEqual(unstyledForeground, GetFirstElementProperties(editor).ForegroundBrush);
		}
	}

	[TestMethod]
	public void SetTokens_WithChangedRange_Reapplies()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Brush? unstyledForeground = GetElementPropertiesAtOffset(editor, 0).ForegroundBrush;

			Assert.IsTrue(colorizer.SetTokens([new TextSemanticToken(new TextRange(0, 5), TextSemanticTokenTypes.Variable)]));

			int resolveCalls = resolver.ResolveCallCount;

			// A moved range must be applied and replaces the previous styling for the line.
			Assert.IsTrue(colorizer.SetTokens([new TextSemanticToken(new TextRange(2, 3), TextSemanticTokenTypes.Variable)]));
			Assert.AreEqual(resolveCalls + 1, resolver.ResolveCallCount);
			Assert.AreEqual(Brushes.Red, GetElementPropertiesAtOffset(editor, 3).ForegroundBrush);
			Assert.AreEqual(unstyledForeground, GetElementPropertiesAtOffset(editor, 0).ForegroundBrush);
		}
	}

	[TestMethod]
	public void SetTokens_WithChangedType_Reapplies()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		resolver.SetStyleFactory(token => new TextRunStyle(
			token.Type == TextSemanticTokenTypes.Function ? Brushes.Lime : Brushes.Red,
			IsBold: false,
			IsItalic: false,
			TextDecorations: null));

		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsTrue(colorizer.SetTokens([new TextSemanticToken(new TextRange(0, 5), TextSemanticTokenTypes.Variable)]));
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);

			// Same range, different semantic type: the cached style is stale and the push must be applied.
			Assert.IsTrue(colorizer.SetTokens([new TextSemanticToken(new TextRange(0, 5), TextSemanticTokenTypes.Function)]));
			Assert.AreEqual(Brushes.Lime, GetFirstElementProperties(editor).ForegroundBrush);
		}
	}

	[TestMethod]
	public void SetTokens_WithChangedModifiers_Reapplies()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		resolver.SetStyle(Brushes.Red, boldModifier: TextSemanticTokenModifiers.Deprecated);

		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsTrue(colorizer.SetTokens(
			[
				new TextSemanticToken(
					new TextRange(0, 5),
					TextSemanticTokenTypes.Variable,
					[TextSemanticTokenModifiers.Deprecated])
			]));

			Assert.AreEqual(FontWeights.Bold, GetFirstElementProperties(editor).Typeface.Weight);

			// Dropping the modifier changes the resolved style, so the equal-range push must be applied.
			Assert.IsTrue(colorizer.SetTokens([new TextSemanticToken(new TextRange(0, 5), TextSemanticTokenTypes.Variable)]));
			Assert.AreNotEqual(FontWeights.Bold, GetFirstElementProperties(editor).Typeface.Weight);
		}
	}

	[TestMethod]
	public void SetTokens_WithChangedTokenCount_Reapplies()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsTrue(colorizer.SetTokens([new TextSemanticToken(new TextRange(0, 5), TextSemanticTokenTypes.Variable)]));

			int resolveCalls = resolver.ResolveCallCount;

			Assert.IsTrue(colorizer.SetTokens(
			[
				new TextSemanticToken(new TextRange(0, 5), TextSemanticTokenTypes.Variable),
				new TextSemanticToken(new TextRange(6, 5), TextSemanticTokenTypes.Property)
			]));

			Assert.AreEqual(resolveCalls + 2, resolver.ResolveCallCount);
			Assert.AreEqual(Brushes.Red, GetElementPropertiesAtOffset(editor, 8).ForegroundBrush);
		}
	}

	[TestMethod]
	public void SetTokens_AfterDocumentReplaced_Reapplies()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsTrue(colorizer.SetTokens([new TextSemanticToken(new TextRange(0, 5), TextSemanticTokenTypes.Variable)]));

			// A replaced document invalidates the anchors even though the token ranges are numerically equal.
			editor.Document = new TextDocument("final value = 1");

			Assert.IsTrue(colorizer.SetTokens([new TextSemanticToken(new TextRange(0, 5), TextSemanticTokenTypes.Variable)]));
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);
		}
	}

	[TestMethod]
	public void DocumentChange_AfterSetTokens_RedrawKeepsTheClippedStyle()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			colorizer.SetTokens([CreateToken(editor, 0, TextSemanticTokenTypes.Variable)]);

			// Redrawing after the document shrinks must not throw for the existing token set: the stored
			// anchors stay in place and the style is clipped to the shortened line.
			editor.Text = "ab";
			editor.TextArea.TextView.Redraw();

			// The clipped element still carries the resolved style after the redraw; the document text itself
			// is the edit's, so only the style pipeline is asserted here.
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);
		}
	}

	[TestMethod]
	public void SetTokens_ItalicStyle_AppliesItalicTypeface()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			resolver.SetStyle(Brushes.Red, isItalic: true);
			colorizer.SetTokens([CreateToken(editor, 0, TextSemanticTokenTypes.Variable)]);

			Assert.AreEqual(FontStyles.Italic, GetFirstElementProperties(editor).Typeface.Style);
		}
	}

	[TestMethod]
	public void SetTokens_EmptyCollection_ClearsAppliedStyles()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Brush? unstyledForeground = GetFirstElementProperties(editor).ForegroundBrush;

			Assert.IsTrue(colorizer.SetTokens([CreateToken(editor, 0, TextSemanticTokenTypes.Variable)]));
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);

			// An empty token collection is a clear request; a second empty push has nothing left to clear.
			Assert.IsTrue(colorizer.SetTokens([]));
			Assert.IsFalse(colorizer.SetTokens([]));

			Assert.AreEqual(unstyledForeground, GetFirstElementProperties(editor).ForegroundBrush);
		}
	}

	[TestMethod]
	public void SetTokens_TokenWithoutFormatting_IsSkipped()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Brush? unstyledForeground = GetFirstElementProperties(editor).ForegroundBrush;

			resolver.SetStyle(foreground: null);
			colorizer.SetTokens([CreateToken(editor, 0, TextSemanticTokenTypes.Variable)]);

			Assert.AreEqual(unstyledForeground, GetFirstElementProperties(editor).ForegroundBrush);
		}
	}

	[TestMethod]
	public void SetTokens_TextDecorations_AreApplied()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			resolver.SetStyle(Brushes.Red, decorations: TextDecorations.Strikethrough);
			colorizer.SetTokens([CreateToken(editor, 0, TextSemanticTokenTypes.Variable)]);

			TextDecorationCollection? decorations = GetFirstElementProperties(editor).TextDecorations;

			Assert.IsNotNull(decorations);
			Assert.IsTrue(decorations.Count > 0);
		}
	}

	[TestMethod]
	public void SetTokens_AfterSameLengthEditThatMovesAnAnchor_ReappliesTheSameTokenSet()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value", resolver);

		using (window)
		{
			// The token covers "value" at offset 6 on the first line.
			Assert.IsTrue(colorizer.SetTokens([new TextSemanticToken(new TextRange(6, 5), TextSemanticTokenTypes.Variable)]));
			Assert.AreEqual(Brushes.Red, GetElementPropertiesAtOffset(editor, 6).ForegroundBrush);

			int resolveCalls = resolver.ResolveCallCount;

			// A same-length replacement of the space turns the document into two lines: the token offsets are
			// unchanged, but its line/character anchor moved, so the pushed equal set must be applied again
			// instead of being mistaken for a repeat.
			editor.Document.Replace(5, 1, "\n");

			Assert.IsTrue(colorizer.SetTokens([new TextSemanticToken(new TextRange(6, 5), TextSemanticTokenTypes.Variable)]));
			Assert.AreEqual(resolveCalls + 1, resolver.ResolveCallCount);

			editor.TextArea.TextView.EnsureVisualLines();

			VisualLine secondLine = editor.TextArea.TextView.GetVisualLine(2)
				?? throw new InvalidOperationException("The second visual line is not available.");
			VisualLineElement firstElement = secondLine.Elements.First(element => element.DocumentLength > 0);

			Assert.AreEqual(Brushes.Red, firstElement.TextRunProperties.ForegroundBrush);
		}
	}
}

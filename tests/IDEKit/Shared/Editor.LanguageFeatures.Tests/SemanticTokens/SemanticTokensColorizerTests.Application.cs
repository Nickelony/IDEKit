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
using FontStyles = Avalonia.Media.FontStyle;
using FontWeights = Avalonia.Media.FontWeight;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;
using Brush = System.Windows.Media.Brush;
using FontStyles = System.Windows.FontStyles;
using FontWeights = System.Windows.FontWeights;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

public sealed partial class SemanticTokensColorizerTests
{
	[TestMethod]
	public void ApplyTokens_AppliesResolvedStyles()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Brush? unstyledForeground = GetFirstElementProperties(editor).ForegroundBrush;

			Assert.IsTrue(colorizer.ApplyTokens([CreateToken(editor, 0, SemanticTokenTypes.Variable)]));

			// The resolver's style is applied to the document line the token covers.
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);
			Assert.AreNotEqual(unstyledForeground, GetFirstElementProperties(editor).ForegroundBrush);
		}
	}

	[TestMethod]
	public void ClearTokens_RestoresTheUnstyledForeground()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Brush? unstyledForeground = GetFirstElementProperties(editor).ForegroundBrush;

			Assert.IsTrue(colorizer.ApplyTokens([CreateToken(editor, 0, SemanticTokenTypes.Variable)]));
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);

			Assert.IsTrue(colorizer.ClearTokens());

			// Clearing the applied set restores the unstyled foreground of the line.
			Assert.AreEqual(unstyledForeground, GetFirstElementProperties(editor).ForegroundBrush);
		}
	}

	[TestMethod]
	public void ApplyTokens_WithChangedRange_Reapplies()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Brush? unstyledForeground = GetElementPropertiesAtOffset(editor, 0).ForegroundBrush;

			Assert.IsTrue(colorizer.ApplyTokens([new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable)]));

			int resolveCalls = resolver.ResolveCallCount;

			// A moved range must be applied and replaces the previous styling for the line.
			Assert.IsTrue(colorizer.ApplyTokens([new SemanticToken(new TextRange(2, 3), SemanticTokenTypes.Variable)]));
			Assert.AreEqual(resolveCalls + 1, resolver.ResolveCallCount);
			Assert.AreEqual(Brushes.Red, GetElementPropertiesAtOffset(editor, 3).ForegroundBrush);
			Assert.AreEqual(unstyledForeground, GetElementPropertiesAtOffset(editor, 0).ForegroundBrush);
		}
	}

	[TestMethod]
	public void ApplyTokens_WithChangedType_Reapplies()
	{
		var resolver = new StubSemanticTokenStyleResolver();

		resolver.SetStyleFactory(token => new TextRunStyle(
			Foreground: token.Type == SemanticTokenTypes.Function ? Brushes.Lime : Brushes.Red,
			IsBold: false,
			IsItalic: false,
			TextDecorations: null
		));

		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsTrue(colorizer.ApplyTokens([new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable)]));
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);

			// Same range, different semantic type: the cached style is stale and the push must be applied.
			Assert.IsTrue(colorizer.ApplyTokens([new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Function)]));
			Assert.AreEqual(Brushes.Lime, GetFirstElementProperties(editor).ForegroundBrush);
		}
	}

	[TestMethod]
	public void ApplyTokens_WithChangedModifiers_Reapplies()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		resolver.SetStyle(Brushes.Red, boldModifier: SemanticTokenModifiers.Deprecated);

		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsTrue(
				colorizer.ApplyTokens([new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable, [SemanticTokenModifiers.Deprecated])])
			);

			Assert.AreEqual(FontWeights.Bold, GetFirstElementProperties(editor).Typeface.Weight);

			// Dropping the modifier changes the resolved style, so the equal-range push must be applied.
			Assert.IsTrue(colorizer.ApplyTokens([new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable)]));
			Assert.AreNotEqual(FontWeights.Bold, GetFirstElementProperties(editor).Typeface.Weight);
		}
	}

	[TestMethod]
	public void ApplyTokens_WithChangedTokenCount_Reapplies()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsTrue(colorizer.ApplyTokens([new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable)]));

			int resolveCalls = resolver.ResolveCallCount;

			Assert.IsTrue(
				colorizer.ApplyTokens([
					new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable),
					new SemanticToken(new TextRange(6, 5), SemanticTokenTypes.Property),
				])
			);

			Assert.AreEqual(resolveCalls + 2, resolver.ResolveCallCount);
			Assert.AreEqual(Brushes.Red, GetElementPropertiesAtOffset(editor, 8).ForegroundBrush);
		}
	}

	[TestMethod]
	public void ApplyTokens_AfterDocumentReplaced_Reapplies()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsTrue(colorizer.ApplyTokens([new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable)]));

			// A replaced document invalidates the anchors even though the token ranges are numerically equal.
			editor.Document = new TextDocument("final value = 1");

			Assert.IsTrue(colorizer.ApplyTokens([new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable)]));
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);
		}
	}

	[TestMethod]
	public void DocumentChange_DeletionInsideAToken_ShrinksItsStyle()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Brush? unstyledForeground = GetElementPropertiesAtOffset(editor, 6).ForegroundBrush;

			colorizer.ApplyTokens([new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable)]);

			// The deletion leaves "fal value = 1", so the token covers the three characters that remain of its text.
			editor.Document.Remove(1, 2);
			editor.TextArea.TextView.Redraw();

			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);
			Assert.AreEqual(Brushes.Red, GetElementPropertiesAtOffset(editor, 2).ForegroundBrush);
			Assert.AreEqual(unstyledForeground, GetElementPropertiesAtOffset(editor, 4).ForegroundBrush);
		}
	}

	[TestMethod]
	public void ApplyTokens_ItalicStyle_AppliesItalicTypeface()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			resolver.SetStyle(Brushes.Red, isItalic: true);
			colorizer.ApplyTokens([CreateToken(editor, 0, SemanticTokenTypes.Variable)]);

			Assert.AreEqual(FontStyles.Italic, GetFirstElementProperties(editor).Typeface.Style);
		}
	}

	[TestMethod]
	public void ApplyTokens_EmptyCollection_ClearsAppliedStyles()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Brush? unstyledForeground = GetFirstElementProperties(editor).ForegroundBrush;

			Assert.IsTrue(colorizer.ApplyTokens([CreateToken(editor, 0, SemanticTokenTypes.Variable)]));
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);

			// An empty token collection is a clear request; a second empty push has nothing left to clear.
			Assert.IsTrue(colorizer.ApplyTokens([]));
			Assert.IsFalse(colorizer.ApplyTokens([]));

			Assert.AreEqual(unstyledForeground, GetFirstElementProperties(editor).ForegroundBrush);
		}
	}

	[TestMethod]
	public void ApplyTokens_TokenWithoutFormatting_IsSkipped()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Brush? unstyledForeground = GetFirstElementProperties(editor).ForegroundBrush;

			resolver.SetStyle(foreground: null);
			colorizer.ApplyTokens([CreateToken(editor, 0, SemanticTokenTypes.Variable)]);

			Assert.AreEqual(unstyledForeground, GetFirstElementProperties(editor).ForegroundBrush);
		}
	}

	[TestMethod]
	public void ApplyTokens_TextDecorations_AreApplied()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			resolver.SetStyle(Brushes.Red, decorations: TextDecorations.Strikethrough);
			colorizer.ApplyTokens([CreateToken(editor, 0, SemanticTokenTypes.Variable)]);

			TextDecorationCollection? decorations = GetFirstElementProperties(editor).TextDecorations;

			Assert.IsNotNull(decorations);
			Assert.IsGreaterThan(0, decorations.Count);
		}
	}

	[TestMethod]
	public void ApplyTokens_AfterSameLengthEditThatMovesAnAnchor_ReappliesTheSameTokenSet()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value", resolver);

		using (window)
		{
			// The token covers "value" at offset 6 on the first line.
			Assert.IsTrue(colorizer.ApplyTokens([new SemanticToken(new TextRange(6, 5), SemanticTokenTypes.Variable)]));
			Assert.AreEqual(Brushes.Red, GetElementPropertiesAtOffset(editor, 6).ForegroundBrush);

			int resolveCalls = resolver.ResolveCallCount;

			// A same-length replacement of the space turns the document into two lines: the token offsets are
			// unchanged, but its line/character anchor moved, so the pushed equal set must be applied again
			// instead of being mistaken for a repeat.
			editor.Document.Replace(5, 1, "\n");

			Assert.IsTrue(colorizer.ApplyTokens([new SemanticToken(new TextRange(6, 5), SemanticTokenTypes.Variable)]));
			Assert.AreEqual(resolveCalls + 1, resolver.ResolveCallCount);

			editor.TextArea.TextView.EnsureVisualLines();

			VisualLine secondLine =
				editor.TextArea.TextView.GetVisualLine(2) ?? throw new InvalidOperationException("The second visual line is not available.");

			VisualLineElement firstElement = secondLine.Elements.First(element => element.DocumentLength > 0);

			Assert.AreEqual(Brushes.Red, firstElement.TextRunProperties.ForegroundBrush);
		}
	}
}

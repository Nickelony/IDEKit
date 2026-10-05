#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;
using Brush = Avalonia.Media.IBrush;
using FontWeights = Avalonia.Media.FontWeight;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.SemanticTokens;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;
using Brush = System.Windows.Media.Brush;
using FontWeights = System.Windows.FontWeights;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

public sealed partial class SemanticTokensColorizerTests
{
	[TestMethod]
	public void Rebuild_AfterResolverConfigurationChange_RefreshesCachedStyles()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsTrue(colorizer.ApplyTokens([CreateToken(editor, 0, SemanticTokenTypes.Variable)]));
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);

			// Cached styles stay in place until the cache is rebuilt from the resolver's new configuration.
			resolver.SetStyle(Brushes.Lime, isBold: true);

			// The unchanged push is a repeat, so it must not re-resolve; the refresh happens through Rebuild.
			Assert.IsFalse(colorizer.ApplyTokens([CreateToken(editor, 0, SemanticTokenTypes.Variable)]));

			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);
			Assert.AreNotEqual(FontWeights.Bold, GetFirstElementProperties(editor).Typeface.Weight);

			Assert.IsTrue(colorizer.Rebuild());

			Assert.AreEqual(Brushes.Lime, GetFirstElementProperties(editor).ForegroundBrush);
			Assert.AreEqual(FontWeights.Bold, GetFirstElementProperties(editor).Typeface.Weight);
		}
	}

	[TestMethod]
	public void ApplyTokens_WithIdenticalTokenSet_IsIgnoredWithoutResolvingAgain()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsTrue(
				colorizer.ApplyTokens([
					new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable),
					new SemanticToken(new TextRange(6, 5), SemanticTokenTypes.Property),
				])
			);

			int resolveCalls = resolver.ResolveCallCount;

			Assert.AreEqual(2, resolveCalls);

			// A fresh but equal token set is a repeat: no resolver round-trips, no rebuilt map, no redraw.
			Assert.IsFalse(
				colorizer.ApplyTokens([
					new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable),
					new SemanticToken(new TextRange(6, 5), SemanticTokenTypes.Property),
				])
			);

			Assert.AreEqual(resolveCalls, resolver.ResolveCallCount);
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);
		}
	}

	[TestMethod]
	public void ApplyTokens_AfterAnEdit_WithTheSameSet_AppliesAgain()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsTrue(colorizer.ApplyTokens([new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable)]));

			int resolveCalls = resolver.ResolveCallCount;

			// The edit changes the document version, so an equal set describes the edited text and is applied again, even though the replaced
			// character lies outside every applied range.
			editor.Document.Replace(8, 1, "L");

			Assert.IsTrue(colorizer.ApplyTokens([new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable)]));
			Assert.AreEqual(resolveCalls + 1, resolver.ResolveCallCount);
			Assert.AreEqual(Brushes.Red, GetElementPropertiesAtOffset(editor, 1).ForegroundBrush);

			// With no edit since, the same set is a repeat again.
			Assert.IsFalse(colorizer.ApplyTokens([new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable)]));
		}
	}

	[TestMethod]
	public void Rebuild_OnAnotherDocument_PaintsNothing_AndTheNextPushStylesIt()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			var token = new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable);

			Assert.IsTrue(colorizer.ApplyTokens([token]));

			Brush? styledForeground = GetFirstElementProperties(editor).ForegroundBrush;

			editor.Document = new TextDocument("final value = 1");

			Brush? unstyledForeground = GetFirstElementProperties(editor).ForegroundBrush;
			int resolveCalls = resolver.ResolveCallCount;

			// The spans belong to the replaced document, so there is nothing to restyle on the new one.
			Assert.IsFalse(colorizer.Rebuild());
			Assert.AreEqual(resolveCalls, resolver.ResolveCallCount);
			Assert.AreNotEqual(styledForeground, unstyledForeground);
			Assert.AreEqual(unstyledForeground, GetFirstElementProperties(editor).ForegroundBrush);

			Assert.IsTrue(colorizer.ApplyTokens([token]));
			Assert.AreEqual(styledForeground, GetFirstElementProperties(editor).ForegroundBrush);
		}
	}

	[TestMethod]
	public void ApplyTokens_AfterClear_ReappliesTheSameTokens()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsTrue(colorizer.ApplyTokens([new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable)]));
			Assert.IsTrue(colorizer.ClearTokens());

			// With no applied set there is nothing to recognize as a repeat.
			Assert.IsTrue(colorizer.ApplyTokens([new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable)]));
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);
		}
	}

	[TestMethod]
	public void ClearTokens_AndRebuild_WithoutAppliedTokens_ReturnFalse()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsFalse(colorizer.ClearTokens());
			Assert.IsFalse(colorizer.Rebuild());
		}
	}

	[TestMethod]
	public void ApplyTokens_TakesOwnedSnapshot_AndRebuildKeepsTheMovedSpans()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			var tokens = new List<SemanticToken> { CreateToken(editor, 0, SemanticTokenTypes.Variable) };

			colorizer.ApplyTokens(tokens);

			// Mutating the caller's list after ApplyTokens must not affect the colorizer.
			tokens.Clear();

			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);

			Brush? unstyledForeground = GetElementPropertiesAtOffset(editor, 1).ForegroundBrush;

			// The rebuild re-reads the resolver and styles the span where the edit moved it, not at the token's pushed offset.
			editor.Document.Insert(0, "x ");
			resolver.SetStyle(Brushes.Lime);

			Assert.IsTrue(colorizer.Rebuild());
			Assert.AreEqual(unstyledForeground, GetFirstElementProperties(editor).ForegroundBrush);
			Assert.AreEqual(Brushes.Lime, GetElementPropertiesAtOffset(editor, 2).ForegroundBrush);
		}
	}

	[TestMethod]
	public void ApplyTokens_RepeatedIdenticalPush_DoesNotRedraw()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsTrue(colorizer.ApplyTokens([new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable)]));

			editor.TextArea.TextView.EnsureVisualLines();
			Assert.IsTrue(editor.TextArea.TextView.VisualLinesValid);

			// A redraw throws the visual lines away, so lines that stay valid prove the repeat redrew nothing.
			Assert.IsFalse(colorizer.ApplyTokens([new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable)]));
			Assert.IsTrue(editor.TextArea.TextView.VisualLinesValid);

			Assert.IsTrue(colorizer.ApplyTokens([new SemanticToken(new TextRange(6, 5), SemanticTokenTypes.Variable)]));
			Assert.IsFalse(editor.TextArea.TextView.VisualLinesValid);
		}
	}

	[TestMethod]
	public void ApplyTokens_ResolverThrows_LeavesPreviouslyAppliedSetInEffect()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsTrue(colorizer.ApplyTokens([new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable)]));
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);

			resolver.SetStyleFactory(_ => throw new InvalidOperationException("Resolver failed."));

			Assert.ThrowsExactly<InvalidOperationException>(() =>
				colorizer.ApplyTokens([new SemanticToken(new TextRange(6, 5), SemanticTokenTypes.Property)])
			);

			// The failed push must not commit a half-updated set: the previous styling stays in effect and
			// the failed set can be re-pushed once the resolver configuration is fixed.
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);

			resolver.SetStyle(Brushes.Lime);

			Assert.IsTrue(colorizer.ApplyTokens([new SemanticToken(new TextRange(6, 5), SemanticTokenTypes.Property)]));
			Assert.AreEqual(Brushes.Lime, GetElementPropertiesAtOffset(editor, 8).ForegroundBrush);
		}
	}

	[TestMethod]
	public void Rebuild_ResolverThrows_LeavesPreviouslyAppliedSetInEffect()
	{
		var resolver = new StubSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsTrue(colorizer.ApplyTokens([new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable)]));
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);

			resolver.SetStyleFactory(_ => throw new InvalidOperationException("Resolver failed."));

			Assert.ThrowsExactly<InvalidOperationException>(() => colorizer.Rebuild());

			// The failed rebuild keeps the previous styling in effect and the applied set can be rebuilt
			// again once the resolver configuration is fixed.
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);

			resolver.SetStyle(Brushes.Lime);

			Assert.IsTrue(colorizer.Rebuild());
			Assert.AreEqual(Brushes.Lime, GetFirstElementProperties(editor).ForegroundBrush);
		}
	}

	[TestMethod]
	public void ApplyTokens_WhileTheViewIsDetached_AppliesWhenTheDocumentArrives()
	{
		// A text view without a document is the documented detached state: tokens cannot be anchored, so
		// the styled map stays empty and the resolver is not asked until a document exists.
		var textView = new TextView { Document = null };
		var resolver = new StubSemanticTokenStyleResolver();
		var colorizer = new SemanticTokensColorizer(textView, resolver);

		var token = new SemanticToken(new TextRange(0, 5), SemanticTokenTypes.Variable);

		Assert.IsTrue(colorizer.ApplyTokens([token]));

		// The same set pushed again while the view is still detached is a repeat, because the token sequence alone decides it.
		Assert.IsFalse(colorizer.ApplyTokens([token]));

		// Nothing is anchored while detached, so Rebuild reports no work instead of claiming a rebuild that
		// styled nothing, and the resolver is not asked until a document exists.
		Assert.IsFalse(colorizer.Rebuild());
		Assert.AreEqual(0, resolver.ResolveCallCount);

		// Attaching a document makes the next push of the same set a real application instead of a repeat.
		textView.Document = new TextDocument("final value = 1");

		Assert.IsTrue(colorizer.ApplyTokens([token]));
		Assert.AreEqual(1, resolver.ResolveCallCount);

		Assert.IsTrue(colorizer.ClearTokens());
	}
}

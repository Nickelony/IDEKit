#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Highlighting;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.SemanticTokens;
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
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.SemanticTokens;
using System.Windows;
using System.Windows.Media;
using FontWeights = System.Windows.FontWeights;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

public sealed partial class SemanticTokensColorizerTests
{
	[TestMethod]
	public void Rebuild_AfterResolverConfigurationChange_RefreshesCachedStyles()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsTrue(colorizer.SetTokens([CreateToken(editor, 0, TextSemanticTokenTypes.Variable)]));
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);

			// Cached styles stay in place until the cache is rebuilt from the resolver's new configuration.
			resolver.SetStyle(Brushes.Lime, isBold: true);

			// The unchanged push is a repeat, so it must not re-resolve; the refresh happens through Rebuild.
			Assert.IsFalse(colorizer.SetTokens([CreateToken(editor, 0, TextSemanticTokenTypes.Variable)]));

			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);
			Assert.AreNotEqual(FontWeights.Bold, GetFirstElementProperties(editor).Typeface.Weight);

			Assert.IsTrue(colorizer.Rebuild());

			Assert.AreEqual(Brushes.Lime, GetFirstElementProperties(editor).ForegroundBrush);
			Assert.AreEqual(FontWeights.Bold, GetFirstElementProperties(editor).Typeface.Weight);
		}
	}

	[TestMethod]
	public void SetTokens_WithIdenticalTokenSet_IsIgnoredWithoutResolvingAgain()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsTrue(colorizer.SetTokens(
			[
				new TextSemanticToken(new TextRange(0, 5), TextSemanticTokenTypes.Variable),
				new TextSemanticToken(new TextRange(6, 5), TextSemanticTokenTypes.Property)
			]));

			int resolveCalls = resolver.ResolveCallCount;

			Assert.AreEqual(2, resolveCalls);

			// A fresh but equal token set is a repeat: no resolver round-trips, no rebuilt map, no redraw.
			Assert.IsFalse(colorizer.SetTokens(
			[
				new TextSemanticToken(new TextRange(0, 5), TextSemanticTokenTypes.Variable),
				new TextSemanticToken(new TextRange(6, 5), TextSemanticTokenTypes.Property)
			]));

			Assert.AreEqual(resolveCalls, resolver.ResolveCallCount);
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);
		}
	}

	[TestMethod]
	public void SetTokens_AfterEditThatLeavesAppliedTokensUnchanged_IsIgnored()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsTrue(colorizer.SetTokens([new TextSemanticToken(new TextRange(0, 5), TextSemanticTokenTypes.Variable)]));

			int resolveCalls = resolver.ResolveCallCount;

			// Replacing one character inside a later word moves no applied range, so the anchors still hold
			// and the provider's unchanged result is recognized as a repeat.
			editor.Text = "final vaLue = 1";

			Assert.IsFalse(colorizer.SetTokens([new TextSemanticToken(new TextRange(0, 5), TextSemanticTokenTypes.Variable)]));
			Assert.AreEqual(resolveCalls, resolver.ResolveCallCount);
			Assert.AreEqual(Brushes.Red, GetElementPropertiesAtOffset(editor, 1).ForegroundBrush);
		}
	}

	[TestMethod]
	public void SetTokens_AfterClear_ReappliesTheSameTokens()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsTrue(colorizer.SetTokens([new TextSemanticToken(new TextRange(0, 5), TextSemanticTokenTypes.Variable)]));
			Assert.IsTrue(colorizer.ClearTokens());

			// With no applied set there is nothing to recognize as a repeat.
			Assert.IsTrue(colorizer.SetTokens([new TextSemanticToken(new TextRange(0, 5), TextSemanticTokenTypes.Variable)]));
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);
		}
	}

	[TestMethod]
	public void ClearTokens_AndRebuild_WithoutAppliedTokens_ReturnFalse()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsFalse(colorizer.ClearTokens());
			Assert.IsFalse(colorizer.Rebuild());
		}
	}

	[TestMethod]
	public void SetTokens_TakesOwnedSnapshot_AndRebuildSurvivesDocumentChanges()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			var tokens = new List<TextSemanticToken> { CreateToken(editor, 0, TextSemanticTokenTypes.Variable) };

			colorizer.SetTokens(tokens);

			// Mutating the caller's list after SetTokens must not affect the colorizer.
			tokens.Clear();

			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);

			// Rebuilding after the document shrank must not throw and must re-read the resolver.
			editor.Text = "x";
			resolver.SetStyle(Brushes.Lime);
			colorizer.Rebuild();

			Assert.AreEqual("x", editor.Text);
			Assert.AreEqual(Brushes.Lime, GetFirstElementProperties(editor).ForegroundBrush);
		}
	}

	[TestMethod]
	public void SetTokens_ResolverThrows_LeavesPreviouslyAppliedSetInEffect()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsTrue(colorizer.SetTokens([new TextSemanticToken(new TextRange(0, 5), TextSemanticTokenTypes.Variable)]));
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);

			resolver.SetStyleFactory(_ => throw new InvalidOperationException("Resolver failed."));

			Assert.ThrowsExactly<InvalidOperationException>(
				() => colorizer.SetTokens([new TextSemanticToken(new TextRange(6, 5), TextSemanticTokenTypes.Property)]));

			// The failed push must not commit a half-updated set: the previous styling stays in effect and
			// the failed set can be re-pushed once the resolver configuration is fixed.
			Assert.AreEqual(Brushes.Red, GetFirstElementProperties(editor).ForegroundBrush);

			resolver.SetStyle(Brushes.Lime);

			Assert.IsTrue(colorizer.SetTokens([new TextSemanticToken(new TextRange(6, 5), TextSemanticTokenTypes.Property)]));
			Assert.AreEqual(Brushes.Lime, GetElementPropertiesAtOffset(editor, 8).ForegroundBrush);
		}
	}

	[TestMethod]
	public void Rebuild_ResolverThrows_LeavesPreviouslyAppliedSetInEffect()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsTrue(colorizer.SetTokens([new TextSemanticToken(new TextRange(0, 5), TextSemanticTokenTypes.Variable)]));
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
	public void SetTokens_WhileTheViewIsDetached_AppliesWhenTheDocumentArrives()
	{
		// A text view without a document is the documented detached state: tokens cannot be anchored, so
		// the styled map stays empty and the resolver is not asked until a document exists.
		var textView = new TextView { Document = null };
		var resolver = new TestSemanticTokenStyleResolver();
		var colorizer = new SemanticTokensColorizer(textView, resolver);

		var token = new TextSemanticToken(new TextRange(0, 5), TextSemanticTokenTypes.Variable);

		Assert.IsTrue(colorizer.SetTokens([token]));
		Assert.IsTrue(colorizer.Rebuild());
		Assert.AreEqual(0, resolver.ResolveCallCount);

		// Attaching a document makes the next push of the same set a real application instead of a repeat.
		textView.Document = new TextDocument("final value = 1");

		Assert.IsTrue(colorizer.SetTokens([token]));
		Assert.AreEqual(1, resolver.ResolveCallCount);

		Assert.IsTrue(colorizer.ClearTokens());
	}
}

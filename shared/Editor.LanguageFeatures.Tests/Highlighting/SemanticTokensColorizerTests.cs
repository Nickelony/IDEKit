#if AVALONIAEDIT
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Highlighting;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.SemanticTokens;
using Brush = Avalonia.Media.IBrush;
using FontWeights = Avalonia.Media.FontWeight;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Highlighting;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.SemanticTokens;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using FontWeights = System.Windows.FontWeights;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

[STATestClass]
#if !AVALONIAEDIT
[TestCategory(TestCategories.InteractiveWindow)]
#endif
public sealed partial class SemanticTokensColorizerTests
{
	[TestMethod]
	public void MalformedRange_OutsideDocument_IsIgnoredWithoutThrowing()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Brush? unstyledForeground = GetFirstElementProperties(editor).ForegroundBrush;

			// A range that starts beyond the document end must be ignored without changing the text or styling.
			colorizer.SetTokens(
			[
				new TextSemanticToken(new TextRange(10_000, 5), TextSemanticTokenTypes.Variable)
			]);

			Assert.AreEqual(unstyledForeground, GetFirstElementProperties(editor).ForegroundBrush);
		}
	}

	[TestMethod]
	public void SetTokens_WithOverlappingTokens_LaterTokensWinInOverlapRegions()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		resolver.SetStyleFactory(token => new TextRunStyle(
			token.Type switch
			{
				TextSemanticTokenTypes.Function => Brushes.Lime,
				TextSemanticTokenTypes.Method => Brushes.Blue,
				_ => Brushes.Red
			},
			IsBold: false,
			IsItalic: false,
			TextDecorations: null));

		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			// The first line is the styled one, so its unstyled neighbors are compared against a baseline that
			// must survive the styling pass unchanged.
			Brush? unstyledForeground = GetElementPropertiesAtOffset(editor, 1).ForegroundBrush;

			colorizer.SetTokens(
			[
				new TextSemanticToken(new TextRange(0, 10), TextSemanticTokenTypes.Variable),
				new TextSemanticToken(new TextRange(3, 4), TextSemanticTokenTypes.Function),
				new TextSemanticToken(new TextRange(6, 5), TextSemanticTokenTypes.Method)
			]);

			// Tokens are applied in order, so the later token wins inside the overlap while the earlier
			// token remains visible outside it and unstyled text stays unstyled.
			Assert.AreEqual(Brushes.Red, GetElementPropertiesAtOffset(editor, 1).ForegroundBrush);
			Assert.AreEqual(Brushes.Lime, GetElementPropertiesAtOffset(editor, 3).ForegroundBrush);
			Assert.AreEqual(Brushes.Blue, GetElementPropertiesAtOffset(editor, 6).ForegroundBrush);

			// Offset 11 is past every token range, so the default styling must survive the styling pass.
			Assert.AreEqual(unstyledForeground, GetElementPropertiesAtOffset(editor, 11).ForegroundBrush);
		}
	}

	[TestMethod]
	public void SetTokens_WithModifiers_AppliesModifierDependentStyle()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		resolver.SetStyle(Brushes.Red, boldModifier: TextSemanticTokenModifiers.Deprecated);

		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			colorizer.SetTokens(
			[
				new TextSemanticToken(
					new TextRange(0, 5),
					TextSemanticTokenTypes.Variable,
					[TextSemanticTokenModifiers.Deprecated])
			]);

			// The modifier reaches the resolver, whose modifier-dependent bold style is applied.
			VisualLineElementTextRunProperties properties = GetFirstElementProperties(editor);

			Assert.AreEqual(Brushes.Red, properties.ForegroundBrush);
			Assert.AreEqual(FontWeights.Bold, properties.Typeface.Weight);
		}
	}

	[TestMethod]
	public void SetTokens_TokenSpanningMultipleLines_IsClippedToItsStartLine()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("local\nvalue", resolver);

		using (window)
		{
			editor.TextArea.TextView.EnsureVisualLines();

			VisualLine secondLine = editor.TextArea.TextView.GetVisualLine(2)
				?? throw new InvalidOperationException("The second visual line is not available.");
			VisualLineElement secondElement = secondLine.Elements.First(element => element.DocumentLength > 0);
			Brush? unstyledSecondLineForeground = secondElement.TextRunProperties.ForegroundBrush;

			// The token spans both lines but must only style the line containing its start offset.
			colorizer.SetTokens([new TextSemanticToken(new TextRange(0, 11), TextSemanticTokenTypes.Variable)]);

			Assert.AreEqual(Brushes.Red, GetElementPropertiesAtOffset(editor, 0).ForegroundBrush);
			Assert.AreEqual(Brushes.Red, GetElementPropertiesAtOffset(editor, 4).ForegroundBrush);

			editor.TextArea.TextView.EnsureVisualLines();

			secondLine = editor.TextArea.TextView.GetVisualLine(2)
				?? throw new InvalidOperationException("The second visual line is not available.");
			secondElement = secondLine.Elements.First(element => element.DocumentLength > 0);

			Assert.AreEqual(unstyledSecondLineForeground, secondElement.TextRunProperties.ForegroundBrush);
		}
	}

	[TestMethod]
	public void Constructor_NullArguments_ThrowArgumentNullException()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer _, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.ThrowsExactly<ArgumentNullException>(() => new SemanticTokensColorizer(null!, resolver));
			Assert.ThrowsExactly<ArgumentNullException>(() => new SemanticTokensColorizer(editor.TextArea.TextView, null!));
		}
	}

	[TestMethod]
	public void SetTokens_NullCollection_ThrowsArgumentNullException()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor _, SemanticTokensColorizer colorizer, HostWindow window) = CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.ThrowsExactly<ArgumentNullException>(() => colorizer.SetTokens(null!));
		}
	}

	[TestMethod]
	public void Mutators_OffThread_ThrowInvalidOperationException()
	{
		var resolver = new TestSemanticTokenStyleResolver();
		(TextEditor editor, SemanticTokensColorizer colorizer, HostWindow window) =
			CreateHostedColorizer("final value = 1", resolver);

		using (window)
		{
			Assert.IsNotNull(editor.Document);

			// The mutators verify the UI-thread affinity their remarks document; a thread-pool thread is
			// not the thread that owns the text view.
			Exception? setTokensFailure = CaptureOffThreadFailure(() => colorizer.SetTokens(
			[
				new TextSemanticToken(new TextRange(0, 5), TextSemanticTokenTypes.Variable)
			]));
			Exception? rebuildFailure = CaptureOffThreadFailure(() => colorizer.Rebuild());
			Exception? clearFailure = CaptureOffThreadFailure(() => colorizer.ClearTokens());

			Assert.IsInstanceOfType<InvalidOperationException>(setTokensFailure);
			Assert.IsInstanceOfType<InvalidOperationException>(rebuildFailure);
			Assert.IsInstanceOfType<InvalidOperationException>(clearFailure);
		}
	}

	private static Exception? CaptureOffThreadFailure(Action action)
	{
		Exception? failure = null;

#if AVALONIAEDIT
		// Avalonia divergence: a Task.Run from a dispatched test body runs inline on the same headless UI
		// thread, so the mutators would not observe an off-thread caller. A dedicated thread runs genuinely
		// off the editor thread, which is what the UI-thread affinity check must reject.
		var thread = new Thread(() =>
#else
		Task.Run(() =>
#endif
		{
			try
			{
				action();
			}
			catch (Exception exception)
			{
				failure = exception;
			}
#if AVALONIAEDIT
		});

		thread.Start();
		thread.Join();
#else
		}).GetAwaiter().GetResult();
#endif

		return failure;
	}

	private static (TextEditor Editor, SemanticTokensColorizer Colorizer, HostWindow Window) CreateHostedColorizer(
		string text,
		ISemanticTokenStyleResolver resolver)
	{
		(TextEditor editor, HostWindow window) = TestHost.ShowHostedEditor(text);
		var colorizer = new SemanticTokensColorizer(editor.TextArea.TextView, resolver);
		editor.TextArea.TextView.LineTransformers.Add(colorizer);

		return (editor, colorizer, window);
	}

	private static TextSemanticToken CreateToken(TextEditor editor, int offset, string type)
		=> new(new TextRange(offset, 1), type);

	private static VisualLineElementTextRunProperties GetFirstElementProperties(TextEditor editor)
	{
		editor.TextArea.TextView.EnsureVisualLines();

		VisualLine visualLine = editor.TextArea.TextView.GetVisualLine(1)
			?? throw new InvalidOperationException("The first visual line is not available.");

		foreach (VisualLineElement element in visualLine.Elements)
		{
			if (element.DocumentLength > 0)
				return element.TextRunProperties;
		}

		throw new InvalidOperationException("The first visual line has no text elements.");
	}

	private static VisualLineElementTextRunProperties GetElementPropertiesAtOffset(TextEditor editor, int offset)
	{
		editor.TextArea.TextView.EnsureVisualLines();

		VisualLine visualLine = editor.TextArea.TextView.GetVisualLine(1)
			?? throw new InvalidOperationException("The first visual line is not available.");
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

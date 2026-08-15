#if AVALONIAEDIT
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

public sealed partial class CompletionWindowSizingTests
{
	[TestMethod]
	public void MeasureRequiredWidth_TextBlockWhoseTextWasSetThroughInlines_FallsBackToTheFilterText()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			var options = TextCompletionControllerOptions.Default with
			{
				WindowMinContentWidth = 1,
				ItemIconWidth = 0.0,
				ItemDetailSpacing = 0.0
			};

#if AVALONIAEDIT
			// The text block's text lives in its inline collection, so the block's own Text stays empty.
			var contentBlock = new TextBlock();
			contentBlock.Inlines!.Add(new Run("a very long item name"));

			var richItem = new RichContentCompletionData(filterText: "a", content: contentBlock);
#else
			var richItem = new RichContentCompletionData(
				filterText: "a",
				content: new TextBlock { Inlines = { new Run("a very long item name") } });
#endif

			double richWidth = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				[richItem]);

			double plainWidth = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				[new TestCompletionData("a")]);

			// A text block whose text lives in Inlines reports an empty Text; the measurement falls back
			// to the filter text instead of sizing the window for an empty string.
			Assert.AreEqual(plainWidth, richWidth);
		}
	}

	[TestMethod]
	public void MeasureRequiredWidth_NonTextContent_FallsBackToTheFilterText()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			var options = TextCompletionControllerOptions.Default with
			{
				WindowMinContentWidth = 1,
				ItemIconWidth = 0.0,
				ItemDetailSpacing = 0.0
			};

			// An arbitrary element cannot be read as text, so the measurement falls back to the filter text.
			var richItem = new RichContentCompletionData(filterText: "a", content: new Border());

			double richWidth = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				[richItem]);

			double plainWidth = CompletionWindowSizing.MeasureRequiredWidth(
				textArea,
				options,
				getDisplayInfo: null,
				measureItemWidth: null,
				[new TestCompletionData("a")]);

			Assert.AreEqual(plainWidth, richWidth);
		}
	}

	/// <summary>
	/// Supplies a completion item whose displayed content is an arbitrary element, for measurement tests
	/// that exercise the rich-content fallback.
	/// </summary>
	private sealed class RichContentCompletionData(string filterText, object content) : ICompletionData
	{
#if AVALONIAEDIT
		public IImage? Image => null;
#else
		public ImageSource? Image => null;
#endif

		public string Text => filterText;

		public object Content => content;

#if AVALONIAEDIT
		public object? Description => null;
#else
		public object Description => null!;
#endif

		public double Priority => 0.0;

		public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
			=> throw new NotSupportedException("Completion insertion is not exercised by these tests.");
	}
}

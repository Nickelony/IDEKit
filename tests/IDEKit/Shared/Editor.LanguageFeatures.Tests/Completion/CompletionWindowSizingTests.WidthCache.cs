#if AVALONIAEDIT
using AvaloniaEdit.Editing;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

public sealed partial class CompletionWindowSizingTests
{
	[TestMethod]
	public void WidthCacheMatches_UnchangedTextArea_ReturnsTrue()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			var cache = new CompletionWindowSizing.CompletionWidthCache(textArea);
			Assert.IsTrue(cache.Matches(textArea));
		}
	}

	[TestMethod]
	public void WidthCacheMatches_ChangedFontSize_ReturnsFalse()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			var cache = new CompletionWindowSizing.CompletionWidthCache(textArea);

			textArea.FontSize += 2.0;

			Assert.IsFalse(cache.Matches(textArea));
		}
	}
#if !AVALONIAEDIT

	[TestMethod]
	public void WidthCacheMatches_ChangedRootDpi_ReturnsFalse()
	{
		(TextArea textArea, HostWindow hostWindow) = CreateHostedTextArea();

		using (hostWindow)
		{
			VisualTreeHelper.SetRootDpi(hostWindow, new DpiScale(1.0, 1.0));

			var cache = new CompletionWindowSizing.CompletionWidthCache(textArea);

			VisualTreeHelper.SetRootDpi(hostWindow, new DpiScale(2.0, 2.0));

			Assert.IsFalse(cache.Matches(textArea));
		}
	}
#endif
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Navigation;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

public sealed partial class MarkdownRendererTests
{
	[TestMethod]
	public void CreateContent_HyperlinkRequestNavigate_IsSuppressed()
	{
		// The renderer owns activation, so an ambient NavigationService must never navigate the link
		// a second time; the handler marks the request as handled.
		FrameworkElement element = MarkdownRenderer.CreateContent("[docs](https://example.com)");
		var viewer = (FlowDocumentScrollViewer)element;
		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		var requestNavigateEvent = new RequestNavigateEventArgs(new Uri("https://example.com"), "https://example.com");
		hyperlink.RaiseEvent(requestNavigateEvent);

		Assert.IsTrue(requestNavigateEvent.Handled);
	}

	[TestMethod]
	public void CreateContentTree_ContentInteractionEnabled_HyperlinkTakesKeyboardFocus()
	{
		var options = new MarkdownRenderOptions { AllowContentInteraction = true };
		FlowDocument document = MarkdownRenderer.CreateContentTree("[docs](https://example.com)", MarkdownRenderTheme.Default, options);

		Hyperlink hyperlink = FindAll<Hyperlink>(document).Single();

		Assert.IsTrue(hyperlink.Focusable);
	}

	[TestMethod]
	public void CreateContentTree_DefaultOptions_HyperlinkStaysUnfocusable()
	{
		FlowDocument document = MarkdownRenderer.CreateContentTree("[docs](https://example.com)");

		Hyperlink hyperlink = FindAll<Hyperlink>(document).Single();

		Assert.IsFalse(hyperlink.Focusable);
	}
}

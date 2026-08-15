using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Navigation;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

public sealed partial class MarkdownRendererTests
{
	[TestMethod]
	public void CreateContent_HttpsLink_CreatesNavigableHyperlink()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("[docs](https://example.com)");
		var viewer = (FlowDocumentScrollViewer)element;

		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		Assert.AreEqual("https://example.com/", hyperlink.NavigateUri?.AbsoluteUri);
		Assert.AreEqual(Cursors.Hand, hyperlink.Cursor);
	}

	[TestMethod]
	public void CreateContent_Autolink_CreatesHyperlink()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("See <https://example.com> for details.");
		var viewer = (FlowDocumentScrollViewer)element;

		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		Assert.AreEqual("https://example.com/", hyperlink.NavigateUri?.AbsoluteUri);
		Assert.AreEqual(Cursors.Hand, hyperlink.Cursor);
	}

	[TestMethod]
	public void CreateContent_UnsupportedSchemeLink_IsNotNavigable()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("[x](javascript:alert(1))");
		var viewer = (FlowDocumentScrollViewer)element;

		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		Assert.IsNull(hyperlink.NavigateUri);
		Assert.AreEqual(Cursors.Arrow, hyperlink.Cursor);
	}

	[TestMethod]
	public void CreateContent_ReferenceLink_ResolvesTargetFromDefinition()
	{
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("[docs][ref]\n\n[ref]: https://example.com");

		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		Assert.AreEqual("https://example.com/", hyperlink.NavigateUri?.AbsoluteUri);
	}

	[TestMethod]
	public void CreateContent_LinkedImage_RendersLinkWithAltTextOnly()
	{
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("[![alt text](https://example.com/image.png)](https://example.com)");

		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		Assert.AreEqual("https://example.com/", hyperlink.NavigateUri?.AbsoluteUri);
		Assert.AreEqual("alt text", GetHyperlinkText(hyperlink));
		Assert.AreEqual(0, FindAll<InlineUIContainer>(viewer.Document).Count());
	}

	[TestMethod]
	public void CreateContent_BareUrlAutolink_CreatesHyperlink()
	{
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("See https://example.com for details.");

		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		Assert.AreEqual("https://example.com/", hyperlink.NavigateUri?.AbsoluteUri);
	}

	[TestMethod]
	public void CreateContent_MailtoAutolink_KeepsTargetAndDisplayedText()
	{
		var options = new MarkdownRenderOptions
		{
			SupportedHyperlinkSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mailto" }
		};
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("Write to <mailto:user@example.com> today.", MarkdownRenderTheme.Default, options);

		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		Assert.AreEqual("mailto:user@example.com", hyperlink.NavigateUri?.AbsoluteUri);
		Assert.AreEqual("mailto:user@example.com", GetHyperlinkText(hyperlink));
	}

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
	public void CreateContent_SchemeCasingDiffersFromTheAssignedSet_OpensTheLink()
	{
		var options = new MarkdownRenderOptions
		{
			SupportedHyperlinkSchemes = new HashSet<string>(StringComparer.Ordinal) { "FTP" }
		};

		FrameworkElement element = MarkdownRenderer.CreateContent("[file](ftp://example.com/file)", MarkdownRenderTheme.Default, options);
		var viewer = (FlowDocumentScrollViewer)element;

		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		// The options copy compares schemes case-insensitively, so the assigned "FTP" allows the "ftp"
		// scheme the URI reports.
		Assert.AreEqual("ftp", hyperlink.NavigateUri?.Scheme);
		Assert.AreEqual(Cursors.Hand, hyperlink.Cursor);
	}

	[TestMethod]
	public void CreateContent_OpenHyperlinkDeclines_UsesExternalOpener()
	{
		Uri? externalOpenedUri = null;

		var options = new MarkdownRenderOptions
		{
			OpenHyperlink = _ => false,
			OpenExternalUri = uri =>
			{
				externalOpenedUri = uri;
				return true;
			}
		};

		FrameworkElement element = MarkdownRenderer.CreateContent("[docs](https://example.com)", MarkdownRenderTheme.Default, options);
		var viewer = (FlowDocumentScrollViewer)element;
		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		var clickEvent = new RoutedEventArgs(Hyperlink.ClickEvent);
		hyperlink.RaiseEvent(clickEvent);

		Assert.AreEqual("https://example.com/", externalOpenedUri?.AbsoluteUri);
		Assert.IsTrue(clickEvent.Handled);
	}

	[TestMethod]
	public void CreateContent_OpenHyperlinkThrows_LogsWarningAndUsesExternalOpener()
	{
		var logger = new CapturingLogger();
		bool externalOpenerCalled = false;

		var options = new MarkdownRenderOptions
		{
			OpenHyperlink = _ => throw new InvalidOperationException("opener failure"),
			OpenExternalUri = _ =>
			{
				externalOpenerCalled = true;
				return true;
			},
			Logger = logger
		};

		FrameworkElement element = MarkdownRenderer.CreateContent("[docs](https://example.com)", MarkdownRenderTheme.Default, options);
		var viewer = (FlowDocumentScrollViewer)element;
		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		hyperlink.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));

		Assert.IsTrue(externalOpenerCalled);

		CapturingLogger.LogEntry entry = logger.Entries.Single();

		Assert.AreEqual(2001, entry.EventId.Id);
		Assert.AreEqual(LogLevel.Warning, entry.Level);
		Assert.IsTrue(entry.Message.Contains("https://example.com", StringComparison.Ordinal));
	}

	[TestMethod]
	public void CreateContent_EmailAutolink_UsesMailtoTargetWithBareAddressText()
	{
		var options = new MarkdownRenderOptions
		{
			SupportedHyperlinkSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mailto" }
		};

		FrameworkElement element = MarkdownRenderer.CreateContent("Write to <user@example.com> today.", MarkdownRenderTheme.Default, options);
		var viewer = (FlowDocumentScrollViewer)element;
		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		Assert.AreEqual("mailto:user@example.com", hyperlink.NavigateUri?.AbsoluteUri);
		Assert.AreEqual("user@example.com", GetHyperlinkText(hyperlink));
		Assert.AreEqual(Cursors.Hand, hyperlink.Cursor);
	}

	[TestMethod]
	public void CreateContent_EmailAutolinkWithoutMailtoScheme_IsNotNavigable()
	{
		FrameworkElement element = MarkdownRenderer.CreateContent("Write to <user@example.com> today.");
		var viewer = (FlowDocumentScrollViewer)element;
		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		Assert.IsNull(hyperlink.NavigateUri);
		Assert.AreEqual(Cursors.Arrow, hyperlink.Cursor);
	}

	[TestMethod]
	public void CreateContent_HyperlinkActivation_InvokesConfiguredOpener()
	{
		Uri? openedUri = null;

		var options = new MarkdownRenderOptions
		{
			OpenHyperlink = uri =>
			{
				openedUri = uri;
				return true;
			}
		};

		FrameworkElement element = MarkdownRenderer.CreateContent("[docs](https://example.com)", MarkdownRenderTheme.Default, options);
		var viewer = (FlowDocumentScrollViewer)element;
		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		var clickEvent = new RoutedEventArgs(Hyperlink.ClickEvent);
		hyperlink.RaiseEvent(clickEvent);

		Assert.AreEqual("https://example.com/", openedUri?.AbsoluteUri);
		Assert.IsTrue(clickEvent.Handled);
	}

	[TestMethod]
	public void CreateContent_DefaultHyperlink_DoesNotTakeKeyboardFocus()
	{
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("[docs](https://example.com)");
		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		Assert.IsFalse(hyperlink.Focusable);
	}

	[TestMethod]
	public void CreateContent_ContentInteractionEnabled_HyperlinkTakesKeyboardFocus()
	{
		var options = new MarkdownRenderOptions { AllowContentInteraction = true };
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("[docs](https://example.com)", MarkdownRenderTheme.Default, options);
		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		Assert.IsTrue(hyperlink.Focusable);
	}

	[TestMethod]
	public void CreateFlowDocument_ContentInteractionEnabled_HyperlinkTakesKeyboardFocus()
	{
		var options = new MarkdownRenderOptions { AllowContentInteraction = true };
		FlowDocument document = MarkdownRenderer.CreateFlowDocument("[docs](https://example.com)", MarkdownRenderTheme.Default, options);

		Hyperlink hyperlink = FindAll<Hyperlink>(document).Single();

		Assert.IsTrue(hyperlink.Focusable);
	}

	[TestMethod]
	public void CreateFlowDocument_DefaultOptions_HyperlinkStaysUnfocusable()
	{
		FlowDocument document = MarkdownRenderer.CreateFlowDocument("[docs](https://example.com)");

		Hyperlink hyperlink = FindAll<Hyperlink>(document).Single();

		Assert.IsFalse(hyperlink.Focusable);
	}

	[TestMethod]
	public void CreateContent_LinkAffordances_StayFixedForOpenableAndDeadLinks()
	{
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("[docs](https://example.com) and [x](javascript:alert(1))");
		var hyperlinks = FindAll<Hyperlink>(viewer.Document).ToList();

		Assert.AreEqual(2, hyperlinks.Count);

		foreach (Hyperlink hyperlink in hyperlinks)
		{
			Assert.AreSame(MarkdownRenderTheme.Default.LinkForeground, hyperlink.Foreground);
			Assert.IsTrue(hyperlink.TextDecorations.Any(decoration => decoration.Location == TextDecorationLocation.Underline));
		}
	}

	[TestMethod]
	public void CreateContent_ContentInteractionEnabled_UnsupportedSchemeLink_StaysUnfocusable()
	{
		var options = new MarkdownRenderOptions { AllowContentInteraction = true };
		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("[x](javascript:alert(1))", MarkdownRenderTheme.Default, options);
		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		// A link the renderer cannot open must not become a dead keyboard stop.
		Assert.IsFalse(hyperlink.Focusable);
	}

	[TestMethod]
	public void CreateContent_NoOpenersConfigured_ActivationIsInertAndNotReportedAsFailure()
	{
		var logger = new CapturingLogger();
		var options = new MarkdownRenderOptions { Logger = logger };

		FrameworkElement element = MarkdownRenderer.CreateContent("[docs](https://example.com)", MarkdownRenderTheme.Default, options);
		var viewer = (FlowDocumentScrollViewer)element;
		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		var clickEvent = new RoutedEventArgs(Hyperlink.ClickEvent);
		hyperlink.RaiseEvent(clickEvent);

		// The renderer never opens a link on its own, so an activation without configured openers is
		// inert; it is not a failure and produces no diagnostic.
		Assert.IsTrue(clickEvent.Handled);
		Assert.AreEqual(0, logger.Entries.Count);
	}

	[TestMethod]
	public void CreateContent_CallbacksDecline_ActivationEndsWithoutFailure()
	{
		var logger = new CapturingLogger();
		var options = new MarkdownRenderOptions
		{
			OpenHyperlink = _ => false,
			OpenExternalUri = _ => false,
			Logger = logger
		};

		FrameworkElement element = MarkdownRenderer.CreateContent("[docs](https://example.com)", MarkdownRenderTheme.Default, options);
		var viewer = (FlowDocumentScrollViewer)element;
		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		var clickEvent = new RoutedEventArgs(Hyperlink.ClickEvent);
		hyperlink.RaiseEvent(clickEvent);

		Assert.IsTrue(clickEvent.Handled);
		Assert.AreEqual(0, logger.Entries.Count);
	}

	[TestMethod]
	public void CreateContent_OpenHyperlinkHandles_SkipsExternalOpener()
	{
		bool externalOpenerCalled = false;

		var options = new MarkdownRenderOptions
		{
			OpenHyperlink = _ => true,
			OpenExternalUri = _ =>
			{
				externalOpenerCalled = true;
				return true;
			}
		};

		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("[docs](https://example.com)", MarkdownRenderTheme.Default, options);
		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		hyperlink.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));

		Assert.IsFalse(externalOpenerCalled, "A handled activation ends the chain.");
	}

	[TestMethod]
	public void CreateContent_OpenExternalUriThrows_LogsWarningEvent2001()
	{
		var logger = new CapturingLogger();
		var options = new MarkdownRenderOptions
		{
			OpenExternalUri = _ => throw new InvalidOperationException("opener failure"),
			Logger = logger
		};

		var viewer = (FlowDocumentScrollViewer)MarkdownRenderer.CreateContent("[docs](https://example.com)", MarkdownRenderTheme.Default, options);
		Hyperlink hyperlink = FindAll<Hyperlink>(viewer.Document).Single();

		hyperlink.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));

		CapturingLogger.LogEntry entry = logger.Entries.Single();

		Assert.AreEqual(2001, entry.EventId.Id);
		Assert.AreEqual(LogLevel.Warning, entry.Level);
	}
}

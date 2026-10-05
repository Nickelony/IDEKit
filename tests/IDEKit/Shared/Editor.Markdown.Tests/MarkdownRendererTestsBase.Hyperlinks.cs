#if AVALONIAEDIT
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Media;
using SharedControl = Avalonia.Controls.Control;

namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;

#else
using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.Core.Media;
using SharedControl = System.Windows.FrameworkElement;

namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;

#endif

public abstract partial class MarkdownRendererTestsBase
{
	/// <summary>
	/// Returns the target the renderer can open (<see langword="null"/> when it cannot open the link), the text, and the keyboard focusability
	/// of the only hyperlink in the content.
	/// </summary>
	protected abstract (Uri? OpenableUri, string Text, bool Focusable) GetSingleLink(SharedControl content);

	/// <summary>
	/// Activates the only hyperlink in the content with a primary click, and returns whether the activation marked its event handled.
	/// </summary>
	protected abstract bool ActivateSingleLink(SharedControl content);

	/// <summary>Returns the look of each hyperlink in the content, in document order.</summary>
	protected abstract IReadOnlyList<RenderedLinkLook> GetLinkLooks(SharedControl content);

	/// <summary>The look of one rendered hyperlink.</summary>
	/// <param name="ShowsHandCursor">Whether the pointer shows the hand cursor over the link, which only a link the renderer can open does.</param>
	/// <param name="Foreground">The solid color of the link text.</param>
	/// <param name="IsUnderlined">Whether the link text is underlined.</param>
	protected readonly record struct RenderedLinkLook(bool ShowsHandCursor, ArgbColor Foreground, bool IsUnderlined);

	[TestMethod]
	public void CreateContent_HttpsLink_ProducesOpenableLinkThatStaysOutOfTheTabOrder()
	{
		(Uri? openableUri, string text, bool focusable) = GetSingleLink(MarkdownRenderer.CreateContent("[docs](https://example.com)"));

		Assert.AreEqual("https://example.com/", openableUri?.AbsoluteUri);
		Assert.AreEqual("docs", text);
		Assert.IsFalse(focusable);
	}

	[TestMethod]
	public void CreateContent_UnsupportedSchemeLink_HasNoOpenableUri()
	{
		(Uri? openableUri, _, bool focusable) = GetSingleLink(MarkdownRenderer.CreateContent("[x](javascript:alert(1))"));

		Assert.IsNull(openableUri);
		Assert.IsFalse(focusable);
	}

	[TestMethod]
	public void CreateContent_EmailAutolinkWithoutMailtoScheme_HasNoOpenableUri()
	{
		(Uri? openableUri, string text, _) = GetSingleLink(MarkdownRenderer.CreateContent("Write to <user@example.com> today."));

		Assert.IsNull(openableUri);
		Assert.AreEqual("user@example.com", text);
	}

	[TestMethod]
	public void CreateContent_ContentInteractionEnabled_MakesOpenableLinkFocusable()
	{
		var options = new MarkdownRenderOptions { AllowContentInteraction = true };

		(_, _, bool focusable) = GetSingleLink(MarkdownRenderer.CreateContent("[docs](https://example.com)", MarkdownRenderTheme.Default, options));

		Assert.IsTrue(focusable);
	}

	[TestMethod]
	public void CreateContent_ContentInteractionEnabled_UnsupportedSchemeLinkStaysUnfocusable()
	{
		var options = new MarkdownRenderOptions { AllowContentInteraction = true };

		(_, _, bool focusable) = GetSingleLink(MarkdownRenderer.CreateContent("[x](javascript:alert(1))", MarkdownRenderTheme.Default, options));

		// A link the renderer cannot open must not become a dead keyboard stop.
		Assert.IsFalse(focusable);
	}

	[TestMethod]
	public void CreateContent_LinkActivation_InvokesConfiguredOpener()
	{
		Uri? openedUri = null;

		var options = new MarkdownRenderOptions
		{
			OpenHyperlink = uri =>
			{
				openedUri = uri;
				return true;
			},
		};

		bool handled = ActivateSingleLink(MarkdownRenderer.CreateContent("[docs](https://example.com)", MarkdownRenderTheme.Default, options));

		Assert.AreEqual("https://example.com/", openedUri?.AbsoluteUri);
		Assert.IsTrue(handled);
	}

	[TestMethod]
	public void CreateContent_OpenHyperlinkThrows_LogsWarningAndUsesExternalOpener()
	{
		var logger = new RecordingLogger();
		bool externalOpenerCalled = false;

		var options = new MarkdownRenderOptions
		{
			OpenHyperlink = _ => throw new InvalidOperationException("opener failure"),

			OpenExternalUri = _ =>
			{
				externalOpenerCalled = true;
				return true;
			},

			Logger = logger,
		};

		ActivateSingleLink(MarkdownRenderer.CreateContent("[docs](https://example.com)", MarkdownRenderTheme.Default, options));

		Assert.IsTrue(externalOpenerCalled);

		RecordingLogger.LogEntry entry = logger.Entries.Single();

		Assert.AreEqual(2001, entry.EventId.Id);
		Assert.AreEqual(LogLevel.Warning, entry.Level);
		Assert.IsTrue(entry.Message.Contains("https://example.com", StringComparison.Ordinal));
	}

	[TestMethod]
	public void CreateContent_OpenableLink_ShowsHandCursor()
	{
		RenderedLinkLook look = GetLinkLooks(MarkdownRenderer.CreateContent("[docs](https://example.com)")).Single();
		Assert.IsTrue(look.ShowsHandCursor);
	}

	[DataRow("[x](javascript:alert(1))", DisplayName = "UnsupportedScheme")]
	[DataRow("Write to <user@example.com> today.", DisplayName = "EmailWithoutMailtoScheme")]
	[TestMethod]
	public void CreateContent_LinkWithoutOpenableTarget_KeepsArrowCursor(string markdown)
	{
		SharedControl content = MarkdownRenderer.CreateContent(markdown);

		(Uri? openableUri, _, _) = GetSingleLink(content);

		Assert.IsNull(openableUri);
		Assert.IsFalse(GetLinkLooks(content).Single().ShowsHandCursor);
	}

	[TestMethod]
	public void CreateContent_Autolink_CreatesHyperlink()
	{
		SharedControl content = MarkdownRenderer.CreateContent("See <https://example.com> for details.");

		(Uri? openableUri, _, _) = GetSingleLink(content);

		Assert.AreEqual("https://example.com/", openableUri?.AbsoluteUri);
		Assert.IsTrue(GetLinkLooks(content).Single().ShowsHandCursor);
	}

	[TestMethod]
	public void CreateContent_ReferenceLink_ResolvesTargetFromDefinition()
	{
		(Uri? openableUri, _, _) = GetSingleLink(MarkdownRenderer.CreateContent("[docs][ref]\n\n[ref]: https://example.com"));
		Assert.AreEqual("https://example.com/", openableUri?.AbsoluteUri);
	}

	[TestMethod]
	public void CreateContent_LinkedImage_RendersLinkWithAltTextOnly()
	{
		SharedControl content = MarkdownRenderer.CreateContent("[![alt text](https://example.com/image.png)](https://example.com)");

		(Uri? openableUri, string text, _) = GetSingleLink(content);

		Assert.AreEqual("https://example.com/", openableUri?.AbsoluteUri);
		Assert.AreEqual("alt text", text);
		Assert.AreEqual(0, CountInlines(content, RenderedInlineKind.Embedded));
	}

	[TestMethod]
	public void CreateContent_BareUrlAutolink_CreatesHyperlink()
	{
		(Uri? openableUri, _, _) = GetSingleLink(MarkdownRenderer.CreateContent("See https://example.com for details."));
		Assert.AreEqual("https://example.com/", openableUri?.AbsoluteUri);
	}

	[TestMethod]
	public void CreateContent_MailtoAutolink_KeepsTargetAndDisplayedText()
	{
		var options = new MarkdownRenderOptions { SupportedHyperlinkSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mailto" } };

		(Uri? openableUri, string text, _) = GetSingleLink(
			MarkdownRenderer.CreateContent("Write to <mailto:user@example.com> today.", MarkdownRenderTheme.Default, options)
		);

		Assert.AreEqual("mailto:user@example.com", openableUri?.AbsoluteUri);
		Assert.AreEqual("mailto:user@example.com", text);
	}

	[TestMethod]
	public void CreateContent_EmailAutolink_UsesMailtoTargetWithBareAddressText()
	{
		var options = new MarkdownRenderOptions { SupportedHyperlinkSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mailto" } };

		SharedControl content = MarkdownRenderer.CreateContent("Write to <user@example.com> today.", MarkdownRenderTheme.Default, options);

		(Uri? openableUri, string text, _) = GetSingleLink(content);

		Assert.AreEqual("mailto:user@example.com", openableUri?.AbsoluteUri);
		Assert.AreEqual("user@example.com", text);
		Assert.IsTrue(GetLinkLooks(content).Single().ShowsHandCursor);
	}

	[TestMethod]
	public void CreateContent_SchemeCasingDiffersFromTheAssignedSet_OpensTheLink()
	{
		var options = new MarkdownRenderOptions { SupportedHyperlinkSchemes = new HashSet<string>(StringComparer.Ordinal) { "FTP" } };

		SharedControl content = MarkdownRenderer.CreateContent("[file](ftp://example.com/file)", MarkdownRenderTheme.Default, options);

		(Uri? openableUri, _, _) = GetSingleLink(content);

		// The options copy compares schemes case-insensitively, so the assigned "FTP" allows the "ftp"
		// scheme the URI reports.
		Assert.AreEqual("ftp", openableUri?.Scheme);
		Assert.IsTrue(GetLinkLooks(content).Single().ShowsHandCursor);
	}

	[TestMethod]
	public void CreateContent_LinkAffordances_StayFixedForOpenableAndDeadLinks()
	{
		IReadOnlyList<RenderedLinkLook> looks = GetLinkLooks(
			MarkdownRenderer.CreateContent("[docs](https://example.com) and [x](javascript:alert(1))")
		);

		Assert.HasCount(2, looks);

		foreach (RenderedLinkLook look in looks)
		{
			Assert.AreEqual(ArgbColor.FromRgb(0x00, 0x66, 0xCC), look.Foreground);
			Assert.IsTrue(look.IsUnderlined);
		}
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
			},
		};

		bool handled = ActivateSingleLink(MarkdownRenderer.CreateContent("[docs](https://example.com)", MarkdownRenderTheme.Default, options));

		Assert.AreEqual("https://example.com/", externalOpenedUri?.AbsoluteUri);
		Assert.IsTrue(handled);
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
			},
		};

		ActivateSingleLink(MarkdownRenderer.CreateContent("[docs](https://example.com)", MarkdownRenderTheme.Default, options));

		Assert.IsFalse(externalOpenerCalled, "A handled activation ends the chain.");
	}

	[TestMethod]
	public void CreateContent_NoOpenersConfigured_ActivationIsInertAndNotReportedAsFailure()
	{
		var logger = new RecordingLogger();
		var options = new MarkdownRenderOptions { Logger = logger };

		bool handled = ActivateSingleLink(MarkdownRenderer.CreateContent("[docs](https://example.com)", MarkdownRenderTheme.Default, options));

		// The renderer never opens a link on its own, so an activation without configured openers is
		// inert; it is not a failure and produces no diagnostic.
		Assert.IsTrue(handled);
		Assert.IsEmpty(logger.Entries);
	}

	[TestMethod]
	public void CreateContent_CallbacksDecline_ActivationEndsWithoutFailure()
	{
		var logger = new RecordingLogger();

		var options = new MarkdownRenderOptions
		{
			OpenHyperlink = _ => false,
			OpenExternalUri = _ => false,
			Logger = logger,
		};

		bool handled = ActivateSingleLink(MarkdownRenderer.CreateContent("[docs](https://example.com)", MarkdownRenderTheme.Default, options));

		Assert.IsTrue(handled);
		Assert.IsEmpty(logger.Entries);
	}

	[TestMethod]
	public void CreateContent_OpenExternalUriThrows_LogsWarningEvent2001()
	{
		var logger = new RecordingLogger();
		var options = new MarkdownRenderOptions { OpenExternalUri = _ => throw new InvalidOperationException("opener failure"), Logger = logger };

		ActivateSingleLink(MarkdownRenderer.CreateContent("[docs](https://example.com)", MarkdownRenderTheme.Default, options));

		RecordingLogger.LogEntry entry = logger.Entries.Single();

		Assert.AreEqual(2001, entry.EventId.Id);
		Assert.AreEqual(LogLevel.Warning, entry.Level);
	}
}

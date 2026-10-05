using Microsoft.Extensions.Logging;

namespace Nickelony.IDEKit.Markdown;

/// <summary>
/// Runs the Markdown emitters' shared hyperlink-activation chain: the configured opener callbacks in order, the failure report,
/// and the handled result.
/// </summary>
public static partial class MarkdownHyperlinkActivator
{
	[LoggerMessage(EventId = 2001, EventName = "HyperlinkOpenFailed", Level = LogLevel.Warning, Message = "Opening hyperlink '{Uri}' failed.")]
	private static partial void LogHyperlinkOpenFailed(ILogger logger, string uri, Exception? exception);

	/// <summary>Runs the opener chain for an openable hyperlink target.</summary>
	/// <param name="uri">The openable target.</param>
	/// <param name="options">The rendering options that carry the opener callbacks.</param>
	/// <returns><see langword="true"/> when a callback handled the activation; otherwise, <see langword="false"/>.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="uri"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// The link is already known to be openable when this runs: the activation is attached only for a supported target, so the support check
	/// is not repeated here.
	/// </remarks>
	public static bool TryOpenHyperlink(Uri uri, MarkdownRenderOptions options)
	{
		ArgumentNullException.ThrowIfNull(uri);
		ArgumentNullException.ThrowIfNull(options);

		// A callback result other than true counts as not handled, so the chain falls through to the
		// next callback exactly as it would without the handler. When no callback handles the
		// activation, the activation is ignored: the renderer never opens a link on its own.
		if (options.OpenHyperlink is { } openHyperlink && TryInvokeOpener(openHyperlink, uri, options))
			return true;

		return options.OpenExternalUri is { } openExternalUri && TryInvokeOpener(openExternalUri, uri, options);
	}

	private static bool TryInvokeOpener(Func<Uri, bool> opener, Uri uri, MarkdownRenderOptions options)
	{
		try
		{
			return opener(uri);
		}
		catch (Exception exception) when (exception is not (OutOfMemoryException or OperationCanceledException))
		{
			// Opening a link is a best-effort host action: report the failure and fall through to the
			// next opener in the chain. A fatal exception (out of memory, or a canceled operation) is not
			// absorbed here and propagates instead, matching the render path's failure policy.
			ReportHyperlinkOpenFailure(uri.AbsoluteUri, exception, options);
			return false;
		}
	}

	private static void ReportHyperlinkOpenFailure(string uri, Exception exception, MarkdownRenderOptions options)
	{
		if (options.Logger is { } logger)
			LogHyperlinkOpenFailed(logger, uri, exception);
	}
}

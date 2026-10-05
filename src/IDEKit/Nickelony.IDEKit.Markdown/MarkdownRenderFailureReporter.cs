using Microsoft.Extensions.Logging;

namespace Nickelony.IDEKit.Markdown;

/// <summary>Reports a caught Markdown render failure through the rendering options' logger, so the plain-text fallback stays observable.</summary>
internal static partial class MarkdownRenderFailureReporter
{
	[LoggerMessage(
		EventId = 2000,
		EventName = "MarkdownRenderFailed",
		Level = LogLevel.Warning,
		Message = "Markdown rendering failed; showing plain text instead."
	)]
	private static partial void LogRenderFailed(ILogger logger, Exception? exception);

	/// <summary>Reports a caught render failure when a logger is configured.</summary>
	/// <param name="exception">The failure to report.</param>
	/// <param name="options">The rendering options that carry the logger.</param>
	internal static void ReportFailure(Exception exception, MarkdownRenderOptions options)
	{
		if (options.Logger is { } logger)
			LogRenderFailed(logger, exception);
	}
}

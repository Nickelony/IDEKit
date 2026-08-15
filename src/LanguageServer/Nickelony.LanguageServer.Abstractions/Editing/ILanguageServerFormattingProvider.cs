namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Produces edits for document formatting.
/// </summary>
public interface ILanguageServerFormattingProvider
{
	/// <summary>
	/// Gets a value indicating whether document formatting is supported by the current ready session.
	/// </summary>
	/// <remarks>
	/// This flag is <see langword="false"/> until a language-server session is ready and supports document
	/// formatting; the lazy-startup and capability-gating contract is on
	/// <see cref="ILanguageServerIntelliSenseProvider"/>.
	/// </remarks>
	bool SupportsFormatting { get; }

	/// <summary>
	/// Produces formatting edits for the supplied document.
	/// </summary>
	/// <param name="request">The document and formatting options.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The workspace edit to apply, or <see langword="null"/> when formatting is unsupported or no changes are available.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	Task<TextWorkspaceEdit?> FormatDocumentAsync(LanguageServerFormattingRequest request, CancellationToken cancellationToken = default);
}

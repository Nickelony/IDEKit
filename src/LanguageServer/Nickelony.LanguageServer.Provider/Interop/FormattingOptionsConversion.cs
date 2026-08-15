namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Projects the consumer-facing <see cref="TextFormattingOptions"/> contract onto the document-formatting wire
/// payload.
/// </summary>
/// <remarks>
/// <see cref="TextFormattingOptions"/> is the single authoritative formatting shape: it is what a consumer
/// supplies and it owns the defaulting rule (<see cref="TextFormattingOptions.DefaultTabSize"/>). The wire
/// payload is a separate type only because the protocol package does not reference the abstraction package, so
/// this projection keeps the mapping explicit and in one place instead of open-coding it at each request site.
/// </remarks>
public static class FormattingOptionsConversion
{
	/// <summary>
	/// Projects <paramref name="options"/> onto the document-formatting wire payload.
	/// </summary>
	/// <param name="options">The authoritative formatting options to project.</param>
	/// <returns>The wire payload for a document-formatting request.</returns>
	public static FormattingOptionsPayload ToPayload(TextFormattingOptions options)
		=> new(options.TabSize, options.InsertSpaces);
}

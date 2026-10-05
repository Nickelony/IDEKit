namespace Nickelony.LanguageServer.Client;

/// <summary>Reports a request outcome that was discarded because it does not belong to the active language-server connection.</summary>
/// <remarks>
/// This is the connection-usability failure reported when an outcome completes but is discarded because the connection that produced
/// it was superseded or marked unhealthy. Catch <see cref="LanguageServerConnectionException"/> for the whole connection-usability family.
/// </remarks>
public sealed class LanguageServerConnectionSupersededException : LanguageServerConnectionException
{
	private const string DefaultMessage = "The language server connection was superseded before the request completed.";

	public LanguageServerConnectionSupersededException()
		: base(DefaultMessage) { }

	/// <param name="message">The exception message.</param>
	public LanguageServerConnectionSupersededException(string? message)
		: base(message ?? DefaultMessage) { }

	/// <param name="message">The exception message.</param>
	/// <param name="innerException">The inner exception.</param>
	public LanguageServerConnectionSupersededException(string? message, Exception? innerException)
		: base(message ?? DefaultMessage, innerException) { }
}

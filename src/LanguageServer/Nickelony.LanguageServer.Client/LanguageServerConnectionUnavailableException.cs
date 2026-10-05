namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Reports that no ready language-server connection is available for a request or notification, or that the active connection became unavailable
/// while one was in flight.
/// </summary>
/// <remarks>
/// Reports a connection-usability failure before a send starts (the client was never started or its session was invalidated) and when a send fails
/// on a live session. Catch <see cref="LanguageServerConnectionException"/> for the whole connection-usability family.
/// </remarks>
public sealed class LanguageServerConnectionUnavailableException : LanguageServerConnectionException
{
	private const string DefaultMessage = "The language server connection became unavailable before the operation completed.";

	public LanguageServerConnectionUnavailableException()
		: base(DefaultMessage) { }

	/// <param name="message">The exception message.</param>
	public LanguageServerConnectionUnavailableException(string? message)
		: base(message ?? DefaultMessage) { }

	/// <param name="message">The exception message.</param>
	/// <param name="innerException">The inner exception.</param>
	public LanguageServerConnectionUnavailableException(string? message, Exception? innerException)
		: base(message ?? DefaultMessage, innerException) { }
}

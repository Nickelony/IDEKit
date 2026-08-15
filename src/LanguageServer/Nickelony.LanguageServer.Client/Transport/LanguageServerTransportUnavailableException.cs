namespace Nickelony.LanguageServer.Client;

/// <summary>
/// The exception that is thrown when no ready language-server transport is available for a request or notification, or
/// when the active transport becomes unavailable while one is in flight.
/// </summary>
/// <remarks>
/// This is the transport-usability failure reported before a send can even start (the client was never started or its
/// session was already invalidated) as well as for a send that fails on a live session. Catch
/// <see cref="LanguageServerTransportException"/> for the whole transport-usability family.
/// </remarks>
public sealed class LanguageServerTransportUnavailableException : LanguageServerTransportException
{
	private const string DefaultMessage = "The language server transport became unavailable before the operation completed.";

	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerTransportUnavailableException"/> class.
	/// </summary>
	public LanguageServerTransportUnavailableException()
		: base(DefaultMessage)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerTransportUnavailableException"/> class.
	/// </summary>
	/// <param name="message">The exception message.</param>
	public LanguageServerTransportUnavailableException(string? message)
		: base(message ?? DefaultMessage)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerTransportUnavailableException"/> class.
	/// </summary>
	/// <param name="message">The exception message.</param>
	/// <param name="innerException">The inner exception.</param>
	public LanguageServerTransportUnavailableException(string? message, Exception? innerException)
		: base(message ?? DefaultMessage, innerException)
	{ }
}

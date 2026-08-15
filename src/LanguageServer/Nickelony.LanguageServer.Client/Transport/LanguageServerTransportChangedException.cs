namespace Nickelony.LanguageServer.Client;

/// <summary>
/// The exception that is thrown when a request result no longer belongs to the active language-server transport.
/// </summary>
/// <remarks>
/// This is the transport-usability failure reported when an outcome completes but is discarded because the transport
/// that produced it was superseded or marked unhealthy. Catch <see cref="LanguageServerTransportException"/> for the
/// whole transport-usability family.
/// </remarks>
public sealed class LanguageServerTransportChangedException : LanguageServerTransportException
{
	private const string DefaultMessage = "The language server transport changed before the request completed.";

	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerTransportChangedException"/> class.
	/// </summary>
	public LanguageServerTransportChangedException()
		: base(DefaultMessage)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerTransportChangedException"/> class.
	/// </summary>
	/// <param name="message">The exception message.</param>
	public LanguageServerTransportChangedException(string? message)
		: base(message ?? DefaultMessage)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerTransportChangedException"/> class.
	/// </summary>
	/// <param name="message">The exception message.</param>
	/// <param name="innerException">The inner exception.</param>
	public LanguageServerTransportChangedException(string? message, Exception? innerException)
		: base(message ?? DefaultMessage, innerException)
	{ }
}

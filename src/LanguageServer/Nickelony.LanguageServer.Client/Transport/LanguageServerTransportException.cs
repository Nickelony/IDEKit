namespace Nickelony.LanguageServer.Client;

/// <summary>
/// The base exception for every failure caused by the language-server transport being unusable: no ready transport
/// is active, the transport became unavailable while an operation was in flight, or the operation outcome was
/// discarded because the transport was superseded.
/// </summary>
/// <remarks>
/// <para>
/// Every transport-usability failure that <see cref="ILanguageServerClient"/> reports derives from this type, and
/// this type derives from <see cref="IOException"/>, so a host can catch one type for the whole family while code
/// that already treats a transport failure as an I/O failure keeps working.
/// </para>
/// <para>
/// The client only ever throws the derived types (<see cref="LanguageServerTransportUnavailableException"/> and
/// <see cref="LanguageServerTransportChangedException"/>); the base is public so a host that surfaces its own
/// transport failure through a client-shaped boundary can report the same family.
/// </para>
/// </remarks>
public class LanguageServerTransportException : IOException
{
	private const string DefaultMessage = "The language server transport is unusable.";

	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerTransportException"/> class.
	/// </summary>
	public LanguageServerTransportException()
		: base(DefaultMessage)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerTransportException"/> class.
	/// </summary>
	/// <param name="message">The exception message.</param>
	public LanguageServerTransportException(string? message)
		: base(message ?? DefaultMessage)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerTransportException"/> class.
	/// </summary>
	/// <param name="message">The exception message.</param>
	/// <param name="innerException">The inner exception.</param>
	public LanguageServerTransportException(string? message, Exception? innerException)
		: base(message ?? DefaultMessage, innerException)
	{ }
}

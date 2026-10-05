namespace Nickelony.LanguageServer.Client;

/// <summary>
/// The base exception for every failure caused by the language-server connection being unusable: no ready connection is active, the connection became
/// unavailable while an operation was in flight, or the operation outcome was discarded because the connection was superseded.
/// </summary>
/// <remarks>
/// <para>
/// Every connection-usability failure that <see cref="ILanguageServerClient"/> reports derives from this type, and this type
/// derives from <see cref="IOException"/>, so a host can catch one type for the whole family while code that already treats a connection failure
/// as an I/O failure keeps working.
/// </para>
/// <para>
/// The client only ever throws the derived types
/// (<see cref="LanguageServerConnectionUnavailableException"/> and <see cref="LanguageServerConnectionSupersededException"/>); the base is public
/// so a host that surfaces its own connection failure through a client-shaped boundary can report the same family.
/// </para>
/// </remarks>
public class LanguageServerConnectionException : IOException
{
	private const string DefaultMessage = "The language server connection is unusable.";

	public LanguageServerConnectionException()
		: base(DefaultMessage) { }

	/// <param name="message">The exception message.</param>
	public LanguageServerConnectionException(string? message)
		: base(message ?? DefaultMessage) { }

	/// <param name="message">The exception message.</param>
	/// <param name="innerException">The inner exception.</param>
	public LanguageServerConnectionException(string? message, Exception? innerException)
		: base(message ?? DefaultMessage, innerException) { }
}

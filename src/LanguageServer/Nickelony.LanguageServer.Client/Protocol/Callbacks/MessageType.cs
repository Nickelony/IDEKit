namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Describes the message severity of a window message, using the LSP <c>MessageType</c> mapping.
/// </summary>
/// <remarks>
/// This is transport detail rather than client surface: the numeric values match the protocol values, and the
/// member is only read from inbound server messages. A value outside the defined range stays representable as an
/// unnamed enum value; a missing severity is treated like <see cref="Log"/>, which is the least intrusive level.
/// </remarks>
internal enum MessageType
{
	/// <summary>
	/// An error message.
	/// </summary>
	Error = 1,

	/// <summary>
	/// A warning message.
	/// </summary>
	Warning = 2,

	/// <summary>
	/// An information message.
	/// </summary>
	Information = 3,

	/// <summary>
	/// A log message.
	/// </summary>
	Log = 4
}

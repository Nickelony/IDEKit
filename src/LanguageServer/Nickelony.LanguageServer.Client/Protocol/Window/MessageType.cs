namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Describes the message severity of a window message, using the LSP <c>MessageType</c> mapping.</summary>
/// <remarks>
/// The numeric values match the protocol values. A value outside the defined range stays representable as an unnamed enum value, and a missing
/// severity is treated like <see cref="Log"/>, which is the least intrusive level.
/// </remarks>
public enum MessageType
{
	Error = 1,

	Warning = 2,

	Information = 3,

	Log = 4,
}

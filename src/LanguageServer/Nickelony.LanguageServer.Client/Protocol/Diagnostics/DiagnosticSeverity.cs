namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Describes the severity of a diagnostic, using the LSP <c>DiagnosticSeverity</c> mapping.
/// </summary>
/// <remarks>
/// The numeric values match the protocol values and are serialized as numbers. The members are ordered by
/// severity, matching the protocol: <see cref="Error"/> is the most severe and <see cref="Hint"/> the least.
/// A value outside the defined range stays representable as an unnamed enum value, and hosts decide how to
/// present unknown or missing severities.
/// </remarks>
public enum DiagnosticSeverity
{
	/// <summary>
	/// An error diagnostic.
	/// </summary>
	Error = 1,

	/// <summary>
	/// A warning diagnostic.
	/// </summary>
	Warning = 2,

	/// <summary>
	/// An information diagnostic.
	/// </summary>
	Information = 3,

	/// <summary>
	/// A hint diagnostic.
	/// </summary>
	Hint = 4
}

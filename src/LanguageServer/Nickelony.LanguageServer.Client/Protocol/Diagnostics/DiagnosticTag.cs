namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Identifies a diagnostic tag as defined by the LSP specification.
/// </summary>
/// <remarks>
/// The numeric values match the protocol values and are serialized as numbers. A value outside the
/// defined range stays representable as an unnamed enum value.
/// </remarks>
public enum DiagnosticTag
{
	/// <summary>
	/// The diagnostic is unnecessary.
	/// </summary>
	Unnecessary = 1,

	/// <summary>
	/// The diagnostic is deprecated.
	/// </summary>
	Deprecated = 2
}

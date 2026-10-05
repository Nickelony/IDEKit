namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Describes the severity of a diagnostic, using the LSP <c>DiagnosticSeverity</c> mapping.</summary>
/// <remarks>
/// The numeric values match the protocol values and are serialized as numbers. A value outside the defined range stays representable
/// as an unnamed enum value, and hosts decide how to present unknown or missing severities.
/// </remarks>
public enum DiagnosticSeverity
{
	Error = 1,

	Warning = 2,

	Information = 3,

	Hint = 4,
}

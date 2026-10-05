namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Describes an annotation on a diagnostic, using the LSP <c>DiagnosticTag</c> mapping.</summary>
/// <remarks>
/// The numeric values match the protocol values and are serialized as numbers. A value outside the defined range stays representable as an unnamed
/// enum value.
/// </remarks>
public enum DiagnosticTag
{
	Unnecessary = 1,
	Deprecated = 2,
}

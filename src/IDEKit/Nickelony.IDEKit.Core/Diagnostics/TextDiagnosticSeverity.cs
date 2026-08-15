namespace Nickelony.IDEKit.Core.Diagnostics;

/// <summary>
/// Describes the severity of a text-editor diagnostic.
/// </summary>
/// <remarks>
/// The values are stable identifiers for persistence and comparison of equality, not a ranking:
/// <see cref="None"/> (the default value) sorts before <see cref="Error"/>, so severities must be
/// ranked through a semantic check rather than by comparing the underlying values. A consumer
/// whose protocol numbers its own severities (for example the Language Server Protocol) maps its
/// numbers explicitly instead of casting them into this enum.
/// </remarks>
public enum TextDiagnosticSeverity
{
	/// <summary>
	/// The diagnostic has no specified severity.
	/// </summary>
	None = 0,

	/// <summary>
	/// The diagnostic reports a problem that prevents the text from working.
	/// </summary>
	Error = 1,

	/// <summary>
	/// The diagnostic reports a likely problem.
	/// </summary>
	Warning = 2,

	/// <summary>
	/// The diagnostic reports information about the text.
	/// </summary>
	Information = 3,

	/// <summary>
	/// The diagnostic reports a minor suggestion.
	/// </summary>
	Hint = 4
}

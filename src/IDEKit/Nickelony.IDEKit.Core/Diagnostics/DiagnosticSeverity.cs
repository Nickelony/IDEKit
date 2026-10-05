namespace Nickelony.IDEKit.Core.Diagnostics;

/// <summary>Describes the severity of a text-editor diagnostic.</summary>
/// <remarks>
/// The values are stable identifiers for persistence and equality checks, not a ranking. <see cref="None"/>, the default value,
/// has the lowest underlying value, so a severity order must be defined explicitly instead of taken from the underlying values. Protocol packages map
/// their own severity numbers explicitly instead of casting them into this enum.
/// </remarks>
public enum DiagnosticSeverity
{
	/// <summary>The diagnostic has no specified severity.</summary>
	None = 0,

	/// <summary>The diagnostic reports a problem that prevents the text from working.</summary>
	Error = 1,

	/// <summary>The diagnostic reports a likely problem.</summary>
	Warning = 2,

	/// <summary>The diagnostic reports information about the text.</summary>
	Information = 3,

	/// <summary>The diagnostic reports a minor suggestion.</summary>
	Hint = 4,
}

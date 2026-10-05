namespace Nickelony.IDEKit.Core.Text;

/// <summary>Identifies the line terminator that a text uses.</summary>
/// <remarks>
/// <see cref="LineTerminatorDetector"/> reports it. The style describes the text and does not rewrite its terminators.
/// </remarks>
public enum LineTerminatorStyle
{
	CrLf,

	Lf,

	Cr,

	/// <summary>More than one line terminator style.</summary>
	Mixed,

	/// <summary>No line terminators.</summary>
	None,
}

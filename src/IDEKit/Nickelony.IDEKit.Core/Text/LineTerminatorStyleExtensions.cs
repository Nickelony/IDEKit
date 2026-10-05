namespace Nickelony.IDEKit.Core.Text;

/// <summary>Names a <see cref="LineTerminatorStyle"/> the way an editor's status bar shows it.</summary>
public static class LineTerminatorStyleExtensions
{
	/// <summary>Returns the label of a style: "LF", "CRLF", "CR" or "Mixed".</summary>
	/// <param name="style">The style to name.</param>
	/// <returns>The label; for <see cref="LineTerminatorStyle.None"/>, the label of the terminator that an editor inserts for a new line.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="style"/> is not a defined style.</exception>
	/// <remarks>
	/// A text without a line terminator has no style of its own, and the editor engines insert the platform's
	/// terminator (<see cref="Environment.NewLine"/>) for its first new line, so that terminator names the style.
	/// </remarks>
	public static string GetDisplayName(this LineTerminatorStyle style)
	{
		return style switch
		{
			LineTerminatorStyle.Lf => "LF",
			LineTerminatorStyle.CrLf => "CRLF",
			LineTerminatorStyle.Cr => "CR",
			LineTerminatorStyle.Mixed => "Mixed",
			LineTerminatorStyle.None => LineTerminators.PlatformStyle.GetDisplayName(),
			_ => throw new ArgumentOutOfRangeException(nameof(style), style, "The style is not defined."),
		};
	}
}

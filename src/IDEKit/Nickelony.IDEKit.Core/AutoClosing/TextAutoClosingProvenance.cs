namespace Nickelony.IDEKit.Core.AutoClosing;

/// <summary>
/// Determines when existing closing text is skipped (overtyped) or an auto-closing pair is removed on
/// Backspace.
/// </summary>
public enum TextAutoClosingProvenance
{
	/// <summary>
	/// Applies only when the closing text at the caret is the text an auto-closing service inserted and
	/// tracked earlier: content that was loaded or typed through other paths is never skipped or
	/// removed as a pair.
	/// </summary>
	Auto = 0,

	/// <summary>
	/// Applies whenever the surrounding text matches the pair, regardless of which path produced it.
	/// </summary>
	Always = 1,

	/// <summary>
	/// Never applies: typing the closing text is normal text input, and Backspace is left to the editor.
	/// </summary>
	Never = 2
}

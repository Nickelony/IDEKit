namespace Nickelony.IDEKit.Core.Editing;

/// <summary>Specifies whether a line-level operation scrolls its target line into view.</summary>
/// <remarks>
/// The zero value is <see cref="ScrollToLine"/>, the most common choice, so a <see langword="default"/> value scrolls; the Framework Design
/// Guidelines give zero to the most common default when no inert member fits.
/// </remarks>
public enum LineScrollingMode
{
	/// <summary>Scrolls the target line into view.</summary>
	ScrollToLine = 0,

	/// <summary>Leaves the viewport unchanged.</summary>
	LeaveUnchanged = 1,
}

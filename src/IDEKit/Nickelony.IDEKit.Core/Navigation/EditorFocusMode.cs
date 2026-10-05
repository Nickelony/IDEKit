namespace Nickelony.IDEKit.Core.Navigation;

/// <summary>Specifies whether applying a navigation location focuses the editor.</summary>
/// <remarks>
/// The zero value is <see cref="Focus"/>, the most common choice, so a <see langword="default"/> value focuses the editor; the Framework Design
/// Guidelines give zero to the most common default when no inert member fits.
/// </remarks>
public enum EditorFocusMode
{
	/// <summary>Focuses the editor before the location is applied.</summary>
	Focus = 0,
	LeaveUnchanged = 1,
}

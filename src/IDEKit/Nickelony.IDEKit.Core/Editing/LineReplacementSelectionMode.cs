namespace Nickelony.IDEKit.Core.Editing;

/// <summary>Specifies what happens to the selection after a line replacement.</summary>
/// <remarks>
/// The zero value is <see cref="Collapse"/>, the most common choice, so a <see langword="default"/> value collapses the selection;
/// the Framework Design Guidelines give zero to the most common default when no inert member fits.
/// </remarks>
public enum LineReplacementSelectionMode
{
	/// <summary>Collapses the selection: the caret is placed at the end of the replacement text.</summary>
	Collapse = 0,

	/// <summary>Keeps the replacement selected, with the caret at the end of the selection.</summary>
	Select = 1,
}

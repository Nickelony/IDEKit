using Nickelony.IDEKit.Core.Editing;

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.Editing;

#else

namespace Nickelony.IDEKit.AvalonEdit.Editing;

#endif

/// <summary>The range, texts and selection outcome of one skip-identical replacement.</summary>
/// <param name="StartOffset">The zero-based start offset of the replaced range.</param>
/// <param name="CurrentText">The range's current text, compared against <paramref name="Replacement"/>; its length is the range's length.</param>
/// <param name="Replacement">The replacement text.</param>
/// <param name="Selection">What happens to the selection after the replacement.</param>
internal readonly record struct RangeReplacementRequest(
	int StartOffset,
	string CurrentText,
	string Replacement,
	LineReplacementSelectionMode Selection
);

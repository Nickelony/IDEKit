using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.AutoClosing;

/// <summary>Carries the inputs of an auto-closing action resolution.</summary>
/// <remarks>
/// Built on the keystroke path and passed by <see langword="in"/>, so a resolver call copies nothing. The caret is clamped
/// to the snapshot bounds. A <see langword="null"/> tracking callback means nothing is tracked, so no closing text
/// qualifies under <see cref="AutoClosingTrackingMode.TrackedOnly"/>; a wrapping request resolves only an insert, never a skip.
/// </remarks>
public readonly record struct AutoClosingRequest(
	ITextSnapshot Snapshot,
	int CaretOffset,
	string InputText,
	AutoClosingOptions Options,
	bool IsWrappingSelection = false,
	Func<int, bool>? IsTrackedClosingText = null
);

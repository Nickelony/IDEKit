using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.AutoClosing;

/// <summary>Carries the inputs of a pair-deletion resolution.</summary>
/// <remarks>
/// The deletion path consumes no typed input. The caret is clamped to the snapshot bounds; a <see langword="null"/> tracking callback means
/// nothing is tracked, so no closing text qualifies under <see cref="AutoClosingTrackingMode.TrackedOnly"/>.
/// </remarks>
public readonly record struct AutoClosingDeletionRequest(
	ITextSnapshot Snapshot,
	int CaretOffset,
	AutoClosingOptions Options,
	Func<int, bool>? IsTrackedClosingText = null
);

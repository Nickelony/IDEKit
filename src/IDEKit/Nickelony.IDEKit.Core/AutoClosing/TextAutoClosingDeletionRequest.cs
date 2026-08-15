using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.AutoClosing;

/// <summary>
/// Carries the inputs of a pair-deletion resolution.
/// </summary>
/// <remarks>
/// The deletion path consumes no typed input, so the request carries the snapshot, the caret, the
/// configuration, and the optional provenance callback only. A <see langword="default"/> instance
/// carries a <see langword="null"/> snapshot and options, which
/// <see cref="TextAutoClosingResolver.TryResolvePairDeletion(in TextAutoClosingDeletionRequest, out TextAutoClosingPair?)"/>
/// rejects.
/// </remarks>
/// <param name="Snapshot">The snapshot of the text containing the caret.</param>
/// <param name="CaretOffset">
/// The zero-based caret offset. Offsets outside the snapshot are clamped to its bounds.
/// </param>
/// <param name="Options">The auto-closing configuration.</param>
/// <param name="IsTrackedClosingText">
/// The insertion-tracking callback that reports whether tracked auto-closing inserted the closing
/// text starting at the supplied offset, or <see langword="null"/> when nothing is tracked.
/// </param>
public readonly record struct TextAutoClosingDeletionRequest(
	ITextSnapshot Snapshot,
	int CaretOffset,
	TextAutoClosingOptions Options,
	Func<int, bool>? IsTrackedClosingText = null);

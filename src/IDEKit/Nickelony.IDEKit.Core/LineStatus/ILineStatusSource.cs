using Nickelony.IDEKit.Core.Notifications;

namespace Nickelony.IDEKit.Core.LineStatus;

/// <summary>
/// Provides the document lines a line-status consumer marks.
/// </summary>
/// <remarks>
/// <para>
/// Marked lines are addressed by one-based line number, which is this assembly's line-marker convention:
/// document content is addressed by zero-based UTF-16 offset (<see cref="Nickelony.IDEKit.Core.Text.TextRange"/>
/// and its siblings), and a one-based line number belongs to the numbered-line surface (line status,
/// bookmarks, unsaved-change tracking).
/// </para>
/// <para>
/// A consumer queries the source during its read pass (for example while a margin draws its
/// markers), so the returned one-based line numbers must refer to the document currently shown
/// and be sorted ascending; the consumer walks them forward once per pass and ignores numbers
/// outside the document.
/// </para>
/// <para>
/// The source is queried inside the consumer's read pass, so <see cref="GetMarkedLineNumbers"/> must
/// not throw: an exception from the source surfaces inside the consumer's drawing pipeline and can
/// abort a whole pass. A source that cannot produce lines must return an empty list instead of
/// <see langword="null"/>. The read can run on every pass, so an implementation must cache its
/// result and recompute it only when its inputs actually change.
/// </para>
/// <para>
/// A source that also raises <see cref="IChangeNotificationSource.Changed"/> is followed by a consumer
/// that subscribes to it, which refreshes itself when the notification arrives, so no manual
/// invalidation call is required. A source without notifications requires the host to invalidate the
/// consumer after its marked lines change.
/// </para>
/// </remarks>
public interface ILineStatusSource
{
	/// <summary>
	/// Gets the one-based document line numbers to mark, sorted ascending.
	/// </summary>
	IReadOnlyList<int> GetMarkedLineNumbers();
}

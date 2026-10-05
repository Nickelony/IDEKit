namespace Nickelony.IDEKit.Core.Editing;

/// <summary>Applies validated text operations to a host-owned document target.</summary>
/// <remarks>
/// The target holds the caller's document content when the call is made, applies synchronously, publishes the result before returning,
/// and applies the whole batch as one user-visible undo step (or documents its own granularity). A failure such as an out-of-range offset may surface
/// after earlier operations were applied. Offsets are valid only for the content they were prepared against, so compare the captured version
/// before applying or use <see cref="ITextEditVersionTarget.TryApply(PreparedTextEdits, long)"/> to check and apply as one step.
/// </remarks>
public interface ITextEditTarget
{
	string Text { get; }

	/// <exception cref="ArgumentNullException"><paramref name="edits"/> is <see langword="null"/>.</exception>
	void Apply(PreparedTextEdits edits);
}

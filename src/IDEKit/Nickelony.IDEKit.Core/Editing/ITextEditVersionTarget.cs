namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Applies validated text-edit batches atomically against a target that exposes a change stamp: the batch is applied only when the stamp
/// still matches the one it was prepared against.
/// </summary>
/// <remarks>
/// The version check and the application happen as one step inside the target's own synchronization (or on its owning thread), so a batch prepared
/// against earlier content is never applied to changed content.
/// </remarks>
public interface ITextEditVersionTarget : ITextEditTarget, ITextEditVersionSource
{
	/// <summary>Applies the batch only when the target's current change stamp still equals <paramref name="expectedVersion"/>.</summary>
	/// <returns>
	/// <see langword="true"/> when the batch was applied; <see langword="false"/> when the stamp does not match and nothing was applied,
	/// so the content must be read and the batch prepared again.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="edits"/> is <see langword="null"/>.</exception>
	bool TryApply(PreparedTextEdits edits, long expectedVersion);
}

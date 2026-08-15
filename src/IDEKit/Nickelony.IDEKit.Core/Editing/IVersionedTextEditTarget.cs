namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Applies validated text-edit batches atomically against a target that exposes a change stamp:
/// the batch is applied only when the stamp still matches the one it was prepared against.
/// </summary>
/// <remarks>
/// <para>
/// The version check and the application must happen as one step inside the target's own
/// synchronization (or on the target's owning thread), so a batch prepared against earlier content
/// can never be applied to changed content. Capture <see cref="ITextEditTargetVersion.Version"/>,
/// read <see cref="ITextEditTarget.Text"/>, prepare the batch, and pass both to
/// <see cref="TryApply(PreparedTextEdits, long)"/>.
/// </para>
/// <para>
/// <see cref="ITextEditTarget"/> remains the contract for targets without a change stamp; hosts
/// whose target tracks a stamp should implement this interface so callers get the race-free apply.
/// </para>
/// </remarks>
public interface IVersionedTextEditTarget : ITextEditTarget, ITextEditTargetVersion
{
	/// <summary>
	/// Applies the batch only when the target's current change stamp still equals
	/// <paramref name="expectedVersion"/>, so a stale batch is never applied.
	/// </summary>
	/// <param name="edits">The prepared, validated edits to apply.</param>
	/// <param name="expectedVersion">The change stamp the batch was prepared against.</param>
	/// <returns>
	/// <see langword="true"/> when the batch was applied; <see langword="false"/> when the stamp no
	/// longer matches and nothing was applied (read the content and prepare the batch again).
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="edits"/> is <see langword="null"/>.</exception>
	bool TryApply(PreparedTextEdits edits, long expectedVersion);
}

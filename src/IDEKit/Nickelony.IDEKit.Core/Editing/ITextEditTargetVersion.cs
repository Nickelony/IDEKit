namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Exposes an opaque change stamp for a text edit target so callers can detect whether the
/// target changed between two observations.
/// </summary>
/// <remarks>
/// Capture the stamp before a multi-step operation (for example an edit preflight) and compare it
/// again before publishing the result; a difference means the target changed in between, so a batch
/// prepared against the earlier content is stale (see <see cref="ITextEditTarget"/> for the full
/// workflow). Treat the value as opaque: compare two captures for equality instead of interpreting
/// the number. An implementation decides what advances the stamp (an edit counter or a document
/// content version), and documents the exact behavior it guarantees.
/// </remarks>
public interface ITextEditTargetVersion
{
	/// <summary>
	/// Gets the current change stamp of the target.
	/// </summary>
	long Version { get; }
}

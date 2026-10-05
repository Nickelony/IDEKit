namespace Nickelony.IDEKit.Core.Editing;

/// <summary>
/// Exposes an opaque change stamp for a text edit target so callers can detect whether the target changed between two observations.
/// </summary>
/// <remarks>
/// Treat the stamp as opaque and compare two captures for equality instead of interpreting the number. Capture through <see cref="CaptureVersion"/>,
/// which observes the target's current state, then compare before publishing a result prepared against the earlier content. An implementation decides
/// what advances the stamp and documents the behavior it guarantees.
/// </remarks>
public interface ITextEditVersionSource
{
	/// <summary>The last observed change stamp; reading it does not observe the target.</summary>
	/// <remarks>
	/// Comparing two reads without an intervening <see cref="CaptureVersion"/> cannot detect a change; call that member to observe the current state.
	/// </remarks>
	long Version { get; }

	/// <summary>
	/// Observes the target's current state, advances the change stamp when the state changed since the last observation, and returns the stamp.
	/// </summary>
	/// <remarks>
	/// The first observation initializes the target's state without advancing the stamp. An implementation with no change subscription reads
	/// the current state here; an implementation that advances the stamp as each edit happens returns it unchanged.
	/// </remarks>
	long CaptureVersion();
}

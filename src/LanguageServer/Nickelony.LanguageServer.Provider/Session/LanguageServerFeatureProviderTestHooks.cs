namespace Nickelony.LanguageServer.Provider;

/// <summary>Groups the internal observers of the nondeterministic points in a provider's startup path and of the signals it announces.</summary>
/// <remarks>
/// <para>
/// The hooks add no public surface. Production leaves every hook unset and the hook-guarded branches are skipped.
/// </para>
/// <para>
/// A test assigns an observer after construction, before the first operation reaches it.
/// </para>
/// </remarks>
internal sealed class LanguageServerFeatureProviderTestHooks
{
	/// <summary>The observer invoked as a caller reaches the provider's shared startup lock to wait for it, or <see langword="null"/>.</summary>
	/// <remarks>
	/// A caller parked on the shared startup lock has no behavior-level oracle, so the observer provides a deterministic synchronization point
	/// for a contended startup.
	/// </remarks>
	internal Action? StartupLockWaitObserver { get; set; }

	/// <summary>
	/// The observer invoked as a document slot reaches its local-commit decision, before it tries the startup lock, or <see langword="null"/>.
	/// </summary>
	/// <remarks>
	/// The decision runs concurrently with a recovering start, and the observer provides a deterministic point to run the start
	/// before the decision resolves, so a test can pin each ordering of the two.
	/// </remarks>
	internal Action? LocalCommitObserver { get; set; }

	/// <summary>
	/// The observer invoked after the features reacted to a document the server now holds in its current state, or <see langword="null"/>.
	/// </summary>
	/// <remarks>
	/// The features learn of the same moment through the synchronized signal; the observer lets a test see which versions were announced.
	/// </remarks>
	internal Action<DocumentSnapshot>? DocumentSynchronizedObserver { get; set; }

	/// <summary>The observer invoked after the features reacted to an invalidated document, or <see langword="null"/>.</summary>
	/// <remarks>
	/// The features learn of the same moment through the invalidated signal; the observer lets a test see which paths were announced.
	/// </remarks>
	internal Action<string>? DocumentInvalidatedObserver { get; set; }

	/// <summary>The observer invoked after the features reacted to a document renamed to a new path, or <see langword="null"/>.</summary>
	/// <remarks>
	/// The features learn of the same moment through the renamed signal; the observer lets a test see which paths were announced.
	/// </remarks>
	internal Action<string>? DocumentRenamedObserver { get; set; }
}

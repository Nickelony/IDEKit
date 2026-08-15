namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Groups the internal hooks that expose the nondeterministic points in the <see cref="LanguageServerClient"/>
/// lifetime.
/// </summary>
/// <remarks>
/// The hooks are immutable after construction, so the seam adds no mutable state. A production client installs
/// <see cref="None"/>, so every hook-guarded branch is skipped.
/// </remarks>
internal sealed class ClientTestHooks
{
	/// <summary>
	/// Gets the shared hooks instance that installs no hook.
	/// </summary>
	public static ClientTestHooks None { get; } = new();

	/// <summary>
	/// Gets the hook invoked after the transport session is activated but before the initialization handshake
	/// completes, or <see langword="null"/>.
	/// </summary>
	public Func<CancellationToken, Task>? SessionActivated { get; init; }

	/// <summary>
	/// Gets the hook invoked after the handshake timeout starts but before the initialize request is sent, or
	/// <see langword="null"/>.
	/// </summary>
	public Func<CancellationToken, Task>? BeforeInitializeRequest { get; init; }

	/// <summary>
	/// Gets the hook awaited as teardown's first step, before the active session is detached, or
	/// <see langword="null"/>.
	/// </summary>
	public Func<Task>? BeforeTeardown { get; init; }

	/// <summary>
	/// Gets the predicate that replaces the transport-generation callback gate, so a test can queue diagnostics
	/// payloads without an active session, or <see langword="null"/> to use the production gate.
	/// </summary>
	public Func<long, bool>? CanAcceptServerCallbacksForGeneration { get; init; }

	/// <summary>
	/// Gets the hook invoked on the diagnostics pump just before a newer pending payload replaces an older one for
	/// one subscriber, or <see langword="null"/>. It lets a concurrency test hold a replacement mid-flight
	/// deterministically. Production leaves it <see langword="null"/> and the call is skipped.
	/// </summary>
	public Action<PublishDiagnosticsParams>? BeforePendingPayloadReplacement { get; init; }
}

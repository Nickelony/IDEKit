using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>Groups the internal hooks that expose the nondeterministic points in the <see cref="ILanguageServerClient"/> lifetime.</summary>
/// <remarks>
/// The hooks are immutable after construction, so the seam adds no mutable state. A production client installs <see cref="None"/>,
/// so every hook-guarded branch is skipped.
/// </remarks>
internal sealed class LanguageServerClientTestHooks
{
	/// <summary>The shared hooks instance that installs no hook.</summary>
	internal static LanguageServerClientTestHooks None { get; } = new();

	/// <summary>
	/// The hook invoked after the connection session is activated but before the initialization handshake completes, or <see langword="null"/>.
	/// </summary>
	internal Func<CancellationToken, Task>? SessionActivated { get; init; }

	/// <summary>The hook invoked after the handshake timeout starts but before the initialize request is sent, or <see langword="null"/>.</summary>
	internal Func<CancellationToken, Task>? BeforeInitializeRequest { get; init; }

	/// <summary>The hook awaited as teardown's first step, before the active session is detached, or <see langword="null"/>.</summary>
	internal Func<Task>? BeforeTeardown { get; init; }

	/// <summary>
	/// The hook invoked immediately before a newer pending payload replaces an older one for a subscriber, or <see langword="null"/>.
	/// </summary>
	internal Action<PublishDiagnosticsParams>? BeforePendingPayloadReplacement { get; init; }
}

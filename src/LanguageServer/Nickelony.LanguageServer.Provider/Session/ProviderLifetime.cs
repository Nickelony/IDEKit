using System.Diagnostics.CodeAnalysis;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Claims a provider's one teardown, tells its collaborators that disposal has begun, and releases the callers that wait for the teardown.
/// </summary>
/// <remarks>
/// <para>
/// The first disposal caller claims the teardown with <see cref="TryBegin"/>. A later caller waits on <see cref="Completed"/> instead of returning
/// while the teardown continues, so a returned dispose call means disposal finished.
/// </para>
/// <para>
/// <see cref="Signal"/> cancels <see cref="Token"/>, and <see cref="IsDisposed"/> reads that token, so a collaborator that observes
/// one observes the other. The token source is never disposed on purpose: linking a request token against the token of a disposed source throws
/// <see cref="ObjectDisposedException"/> where the documented disposal fallback should surface instead.
/// </para>
/// <para>
/// <c>ClientLifetime</c> of the Client package claims a disposal the same way, and the two stay separate copies on purpose.
/// They differ in who runs the teardown: <c>ClientLifetime</c> runs it and releases its token source afterwards, whereas this type leaves
/// the teardown to its caller and never disposes its source. The Client package also references no Core project, so sharing the one interlocked flag
/// and the completion source would take a source file that the Client and the Provider package link, as <c>LocalPathRules</c> is.
/// </para>
/// </remarks>
[SuppressMessage(
	"Design",
	"CA1001:Types that own disposable fields should be disposable",
	Justification = "The token source is never disposed on purpose; see the remarks."
)]
internal sealed class ProviderLifetime
{
	private readonly CancellationTokenSource _cancellation = new();

	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

	private int _started;

	/// <summary>The token that is canceled once disposal starts; it stays linkable after the cancellation.</summary>
	internal CancellationToken Token => _cancellation.Token;

	/// <summary>Indicates whether disposal has started, which is also whether the token was canceled.</summary>
	internal bool IsDisposed => _cancellation.IsCancellationRequested;

	/// <summary>The task that completes when the teardown the first caller started has finished.</summary>
	internal Task Completed => _completion.Task;

	/// <summary>Claims the one-time right to tear the provider down.</summary>
	/// <returns><see langword="true"/> when the caller should perform the teardown; otherwise, <see langword="false"/>.</returns>
	internal bool TryBegin() => Interlocked.Exchange(ref _started, 1) == 0;

	/// <summary>Cancels <see cref="Token"/> so every operation linked to it observes disposal.</summary>
	internal void Signal() => _cancellation.Cancel();

	/// <summary>Completes <see cref="Completed"/> once the first caller's teardown has finished, releasing every later caller.</summary>
	internal void Complete() => _completion.TrySetResult();
}

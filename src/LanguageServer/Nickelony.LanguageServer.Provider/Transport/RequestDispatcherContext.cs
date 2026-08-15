namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Groups the provider-side services a <see cref="RequestDispatcher"/> needs from its owner, so the
/// dispatcher constructor stays readable and positional mistakes between the several delegate parameters are
/// impossible.
/// </summary>
/// <param name="IsDisposedAccessor">Returns whether the owning provider has started disposing.</param>
/// <param name="EnsureTransportStartedAsync">Ensures the transport is running before the single retry attempt.</param>
/// <param name="Logger">The logger instance.</param>
/// <param name="DisposeToken">The token that is canceled when the owning provider is disposed.</param>
internal sealed record RequestDispatcherContext(
	Func<bool> IsDisposedAccessor,
	Func<CancellationToken, Task<bool>> EnsureTransportStartedAsync,
	ILogger Logger,
	CancellationToken DisposeToken);

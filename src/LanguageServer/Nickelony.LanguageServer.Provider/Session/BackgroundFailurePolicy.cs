namespace Nickelony.LanguageServer.Provider;

/// <summary>Decides the log level of a failure in the provider's background work, so every place that logs one applies the same rule.</summary>
/// <remarks>
/// A disposal race is silent, because the provider is going away and nobody reads the outcome.
/// A connection failure is a debug diagnostic, because the connection restarts and the next call recovers or falls back. Anything else is a warning,
/// because nothing expected it. A caller classifies cancellation before it asks, because only the caller knows who owns the token.
/// </remarks>
internal static class BackgroundFailurePolicy
{
	/// <summary>Returns the level at which a failure of the provider's background work is logged.</summary>
	/// <param name="exception">The failure to classify.</param>
	/// <param name="disposal">The disposal of the provider whose work failed.</param>
	/// <returns>
	/// <see langword="null"/> for an <see cref="ObjectDisposedException"/> after the disposal started;
	/// <see cref="LogLevel.Debug"/> for an <see cref="IOException"/>, or for an <see cref="ObjectDisposedException"/> while the provider is live,
	/// because a client disposed under a live provider is a lost connection; otherwise, <see cref="LogLevel.Warning"/>.
	/// </returns>
	internal static LogLevel? LevelOf(Exception exception, ProviderLifetime disposal)
	{
		if (exception is ObjectDisposedException)
			return disposal.IsDisposed ? null : LogLevel.Debug;

		return exception is IOException ? LogLevel.Debug : LogLevel.Warning;
	}
}

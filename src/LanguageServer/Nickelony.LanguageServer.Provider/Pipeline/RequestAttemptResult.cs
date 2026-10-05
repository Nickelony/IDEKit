namespace Nickelony.LanguageServer.Provider;

/// <summary>Describes how one request attempt settled under the provider's failure policy.</summary>
internal enum RequestAttemptOutcome
{
	/// <summary>The attempt completed and produced a response.</summary>
	Succeeded,

	/// <summary>A connection boundary was observed on the primary attempt; the runner may retry once.</summary>
	RetryConnection,

	/// <summary>The attempt settled with the documented fallback value.</summary>
	Fallback,
}

/// <summary>Carries the outcome of one request attempt and, when it succeeded, the response.</summary>
/// <typeparam name="TResponse">The expected response type.</typeparam>
/// <param name="Outcome">The attempt outcome.</param>
/// <param name="Response">The response when <paramref name="Outcome"/> is <see cref="RequestAttemptOutcome.Succeeded"/>.</param>
internal readonly record struct RequestAttemptResult<TResponse>(RequestAttemptOutcome Outcome, TResponse? Response);

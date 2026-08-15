namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Provides the data for the <see cref="ILanguageServerIntelliSenseProvider.StartupFailed"/> event.
/// </summary>
public sealed class StartupFailedEventArgs : EventArgs
{
	/// <summary>
	/// Initializes a new instance of the <see cref="StartupFailedEventArgs"/> class.
	/// </summary>
	/// <param name="failure">The reported startup failure.</param>
	/// <exception cref="ArgumentNullException"><paramref name="failure"/> is <see langword="null"/>.</exception>
	public StartupFailedEventArgs(LanguageServerStartupFailure failure)
	{
		ArgumentNullException.ThrowIfNull(failure);

		Failure = failure;
	}

	/// <summary>
	/// Gets the reported startup failure.
	/// </summary>
	public LanguageServerStartupFailure Failure { get; }
}

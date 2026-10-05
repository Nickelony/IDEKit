namespace Nickelony.LanguageServer.Provider.Abstractions;

/// <summary>Provides the data for the <c>ILanguageServerFeatureProvider.StartupFailed</c> event.</summary>
public sealed class StartupFailedEventArgs : EventArgs
{
	/// <exception cref="ArgumentNullException"><paramref name="failure"/> is <see langword="null"/>.</exception>
	public StartupFailedEventArgs(StartupFailure failure)
	{
		ArgumentNullException.ThrowIfNull(failure);
		Failure = failure;
	}

	public StartupFailure Failure { get; }
}

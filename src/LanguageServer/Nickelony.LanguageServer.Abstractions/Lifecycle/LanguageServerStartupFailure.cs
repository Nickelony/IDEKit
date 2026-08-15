namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Describes a failure to start the language server.
/// </summary>
/// <remarks>
/// The failure is reported to the consumer through <see cref="ILanguageServerIntelliSenseProvider.StartupFailed"/>.
/// The provider is the sender; <see cref="Message"/> is a human-readable description for display or logging, and
/// its exact text is provider-defined.
/// </remarks>
public sealed record LanguageServerStartupFailure
{
	/// <summary>
	/// Initializes a new instance of the <see cref="LanguageServerStartupFailure"/> record.
	/// </summary>
	/// <param name="message">The human-readable description of the failure for display or logging.</param>
	/// <param name="isPersistent">
	/// <see langword="true"/> when the provider considers this failure terminal for the current instance and stops
	/// attempting automatic starts; the host must intervene (for example by reconfiguring the provider) before the
	/// language server can run. Providers without automatic restart always report <see langword="false"/>.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
	public LanguageServerStartupFailure(string message, bool isPersistent)
	{
		ArgumentNullException.ThrowIfNull(message);

		Message = message;
		IsPersistent = isPersistent;
	}

	/// <summary>
	/// Gets the human-readable description of the failure for display or logging.
	/// </summary>
	public string Message { get; }

	/// <summary>
	/// Gets a value indicating whether the provider considers this failure terminal for the current instance and
	/// stops attempting automatic starts.
	/// </summary>
	public bool IsPersistent { get; }
}

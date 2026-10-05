namespace Nickelony.LanguageServer.Provider.Abstractions;

/// <summary>Describes a failure to start the language server.</summary>
public sealed record StartupFailure
{
	/// <param name="code">One of the <see cref="ProviderFailureCodes"/> values, which a host branches on.</param>
	/// <param name="message">The human-readable description of the failure for display or logging.</param>
	/// <param name="isPersistent">
	/// <see langword="true"/> when the provider considers this failure terminal for the current instance and stops attempting automatic starts,
	/// so the host must intervene before the language server can run. A provider without automatic restart always reports <see langword="false"/>.
	/// </param>
	/// <param name="exception">The exception the failed start attempt raised, or <see langword="null"/> when the failure came without one.</param>
	/// <exception cref="ArgumentNullException"><paramref name="code"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="code"/> or <paramref name="message"/> is blank.</exception>
	public StartupFailure(string code, string message, bool isPersistent, Exception? exception = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(code);
		ArgumentException.ThrowIfNullOrWhiteSpace(message);

		Code = code;
		Message = message;
		IsPersistent = isPersistent;
		Exception = exception;
	}

	/// <summary>The stable code of the failure, one of the <see cref="ProviderFailureCodes"/> values.</summary>
	public string Code { get; }

	/// <summary>The failure description, whose exact wording is provider-defined.</summary>
	public string Message { get; }

	/// <summary>Whether the provider considers this failure terminal for the current instance and stops attempting automatic starts.</summary>
	public bool IsPersistent { get; }

	/// <summary>The exception the failed start attempt raised, or <see langword="null"/> when the failure came without one.</summary>
	/// <remarks>
	/// Use <see cref="Message"/> for display or logging rather than depending on this exception being present; it keeps the evidence for a host
	/// that wants the stack or the type.
	/// </remarks>
	public Exception? Exception { get; }
}

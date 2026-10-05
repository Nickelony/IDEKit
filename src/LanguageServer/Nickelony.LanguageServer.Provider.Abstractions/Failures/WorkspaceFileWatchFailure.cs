namespace Nickelony.LanguageServer.Provider.Abstractions;

/// <summary>Describes a workspace file watcher that could not be started or recovered.</summary>
/// <remarks>
/// While external workspace changes are not forwarded, language features for open editor files may remain available.
/// </remarks>
public sealed record WorkspaceFileWatchFailure
{
	/// <param name="code">One of the <see cref="ProviderFailureCodes"/> values, which a host branches on.</param>
	/// <param name="message">The human-readable description of the failure for display or logging.</param>
	/// <param name="workspaceRootDirectoryPath">
	/// The workspace root directory path the failure applies to, or <see langword="null"/> when the failure is not tied to a specific root.
	/// </param>
	/// <param name="exception">The exception the watcher raised, or <see langword="null"/> when the failure came without one.</param>
	/// <exception cref="ArgumentNullException"><paramref name="code"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="code"/> or <paramref name="message"/> is blank.</exception>
	public WorkspaceFileWatchFailure(string code, string message, string? workspaceRootDirectoryPath = null, Exception? exception = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(code);
		ArgumentException.ThrowIfNullOrWhiteSpace(message);

		Code = code;
		Message = message;
		WorkspaceRootDirectoryPath = workspaceRootDirectoryPath;
		Exception = exception;
	}

	/// <summary>The stable code of the failure, one of the <see cref="ProviderFailureCodes"/> values.</summary>
	public string Code { get; }

	/// <summary>The failure description, whose exact wording is provider-defined.</summary>
	public string Message { get; }

	/// <summary>
	/// The workspace root directory path the failure applies to, or <see langword="null"/> when the failure is not tied to a specific root.
	/// </summary>
	public string? WorkspaceRootDirectoryPath { get; }

	/// <summary>The exception the watcher raised, or <see langword="null"/> when the failure came without one.</summary>
	/// <remarks>
	/// Use <see cref="Message"/> for display or logging rather than depending on this exception being present; it keeps the evidence for a host
	/// that wants the stack or the type.
	/// </remarks>
	public Exception? Exception { get; }
}

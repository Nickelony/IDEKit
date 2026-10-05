using System.Diagnostics.CodeAnalysis;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Carries what a provider's features and collaborators share: the client, the tracked documents, the disposal and the logger.</summary>
/// <param name="client">The language-server client, or <see langword="null"/> when no server is configured.</param>
/// <param name="documents">The tracker of the documents mirrored to the server, with the payloads cached for them.</param>
/// <param name="disposal">The disposal of the provider, whose start ends the work of its collaborators.</param>
/// <param name="providerDisplayName">The provider display name used in log and diagnostic text.</param>
/// <param name="options">The provider tunables, whose path comparison decides whether two local paths identify one document.</param>
/// <param name="logger">The logger instance.</param>
/// <remarks>
/// The provider builds one context and hands it to every feature, so a feature reads no field of the provider
/// that composes it. The context also holds the helpers that several collaborators need identically: the path check at the document entry points,
/// the background-task observation and the log of a best-effort failure.
/// </remarks>
internal sealed class ProviderContext(
	ILanguageServerClient? client,
	DocumentTracker documents,
	ProviderLifetime disposal,
	string providerDisplayName,
	LanguageServerFeatureProviderOptions options,
	ILogger logger
)
{
	/// <summary>The language-server client, or <see langword="null"/> when no server is configured.</summary>
	internal ILanguageServerClient? Client { get; } = client;

	/// <summary>The tracker of the documents mirrored to the server, with the payloads cached for them.</summary>
	internal DocumentTracker Documents { get; } = documents;

	/// <summary>The disposal of the provider; its start ends the work of every collaborator.</summary>
	internal ProviderLifetime Disposal { get; } = disposal;

	/// <summary>The provider tunables, validated by the provider before it built this context.</summary>
	internal LanguageServerFeatureProviderOptions Options { get; } = options;

	/// <summary>The logger instance supplied to the provider, or a no-op logger when none was supplied.</summary>
	internal ILogger Logger { get; } = logger;

	/// <summary>The provider display name used in log and diagnostic text.</summary>
	internal string ProviderDisplayName { get; } = providerDisplayName;

	/// <summary>Tries to normalize a caller-supplied document path for one entry point, logging the rejection when the path cannot be used.</summary>
	/// <param name="filePath">The caller-supplied path.</param>
	/// <param name="operation">The operation description used in log text.</param>
	/// <param name="normalizedFilePath">Receives the normalized path when the path is usable.</param>
	/// <returns><see langword="true"/> when the path was normalized; otherwise, <see langword="false"/>.</returns>
	internal bool TryNormalizeDocumentEntryPath(string filePath, string operation, [NotNullWhen(true)] out string? normalizedFilePath)
	{
		if (LocalPath.TryNormalize(filePath, out normalizedFilePath))
			return true;

		Logger.LogDebug(
			"{DisplayName} {Operation} ignored the path '{FilePath}' because it cannot be normalized to an absolute local file path.",
			ProviderDisplayName,
			operation,
			filePath
		);

		return false;
	}

	/// <summary>Observes a provider-owned background task so faults are logged instead of surfacing as unobserved task exceptions.</summary>
	/// <param name="task">The background task to observe.</param>
	/// <param name="operation">The operation description used in log text.</param>
	/// <remarks>
	/// Cancellation is an expected outcome, and <see cref="BackgroundFailurePolicy"/> decides the level of any other fault, so a fault
	/// on a provider-owned background task is observed and logged unless the policy calls it expected.
	/// </remarks>
	internal void ObserveBackgroundTask(Task task, string operation)
	{
		_ = ObserveAsync(Logger, observerDisposal: Disposal, observedTask: task, observedOperation: operation);

		static async Task ObserveAsync(ILogger logger, ProviderLifetime observerDisposal, Task observedTask, string observedOperation)
		{
			try
			{
				await observedTask.ConfigureAwait(false);
			}
			catch (OperationCanceledException) { }
			catch (Exception exception)
			{
				if (BackgroundFailurePolicy.LevelOf(exception, observerDisposal) is { } level)
					logger.Log(level, exception, "Background operation '{Operation}' failed.", observedOperation);
			}
		}
	}

	/// <summary>Logs a best-effort notification failure at the level that <see cref="BackgroundFailurePolicy"/> gives it.</summary>
	/// <param name="operation">The operation description used in log text.</param>
	/// <param name="filePath">The document path the failed operation targeted.</param>
	/// <param name="exception">The failure to log.</param>
	internal void LogBestEffortFailure(string operation, string filePath, Exception exception)
	{
		if (BackgroundFailurePolicy.LevelOf(exception, Disposal) is not { } level)
			return;

		Logger.Log(level, exception, "{DisplayName} best-effort {Operation} failed for '{FilePath}'.", ProviderDisplayName, operation, filePath);
	}
}

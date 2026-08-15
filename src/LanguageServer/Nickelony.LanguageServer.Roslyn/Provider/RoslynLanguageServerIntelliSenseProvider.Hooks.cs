using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.LanguageServer.Roslyn;

public abstract partial class RoslynLanguageServerIntelliSenseProvider
{
	/// <inheritdoc/>
	/// <remarks>
	/// Always <see langword="false"/>: the Roslyn language server reads its options from the configuration push,
	/// which this provider derives from its own options rather than from a workspace file, so no workspace file
	/// change can invalidate the settings payload. A change to a watched build input is still forwarded to the
	/// server, which reloads the affected projects itself.
	/// </remarks>
	protected override bool IsConfigurationPath(string normalizedPath)
		=> false;

	/// <inheritdoc/>
	protected override object CreateSettingsPayload()
		=> SettingsFactory.Create(_options);

	/// <inheritdoc/>
	protected override string CreateStartupFailureMessage(bool isPersistentFailure)
	{
		return isPersistentFailure
			? $"The Roslyn language server for {ProviderDisplayName} failed to start repeatedly, so {ProviderDisplayName} IntelliSense is now disabled until the provider is recreated."
			: $"The Roslyn language server for {ProviderDisplayName} failed to start, so {ProviderDisplayName} IntelliSense stays unavailable until a start succeeds. The provider retries automatically when {ProviderDisplayName} IntelliSense is requested again.";
	}

	/// <inheritdoc/>
	protected override string CreateMissingClientFailureMessage()
		=> $"The Roslyn language server executable for {ProviderDisplayName} is unavailable, so {ProviderDisplayName} IntelliSense is disabled until the provider is recreated with a valid executable path.";

	/// <inheritdoc/>
	protected override IReadOnlyList<TextDiagnostic> GetTrackedDiagnostics(string normalizedFilePath)
		=> DocumentStore.GetDiagnostics(normalizedFilePath);

	/// <inheritdoc/>
	protected override void InvalidateTrackedDocumentSynchronization(string filePath)
		=> DocumentStore.InvalidateServerSynchronization(filePath);

	/// <inheritdoc/>
	protected override IReadOnlyList<SemanticToken> GetTrackedSemanticTokens(string normalizedFilePath)
		=> DocumentStore.GetSemanticTokens(normalizedFilePath);

	/// <inheritdoc/>
	protected override bool TryStoreSemanticTokens(string normalizedFilePath, int documentVersion, IReadOnlyList<SemanticToken> semanticTokens)
		=> DocumentStore.TryStoreSemanticTokens(normalizedFilePath, documentVersion, semanticTokens);
}

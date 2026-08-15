using Nickelony.IDEKit.IntelliSense.Diagnostics;

namespace Nickelony.LanguageServer.Lua;

public sealed partial class LuaLanguageServerIntelliSenseProvider
{
	/// <inheritdoc/>
	protected override string ProviderDisplayName => "Lua";

	/// <inheritdoc/>
	protected override string LanguageId => "lua";

	/// <inheritdoc/>
	protected override bool IsConfigurationPath(string normalizedPath)
		=> LuaWorkspaceConventions.IsConfigurationPath(normalizedPath);

	/// <inheritdoc/>
	protected override object CreateSettingsPayload()
		=> SettingsFactory.Create(_options);

	/// <inheritdoc/>
	protected override string CreateStartupFailureMessage(bool isPersistentFailure)
	{
		return isPersistentFailure
			? "The configured Lua language server failed to start repeatedly, so Lua IntelliSense is now disabled until the provider is recreated."
			: "The configured Lua language server failed to start, so Lua IntelliSense stays unavailable until a start succeeds. The provider retries automatically when Lua IntelliSense is requested again.";
	}

	/// <inheritdoc/>
	protected override string CreateMissingClientFailureMessage()
		=> "The Lua language server executable is unavailable, so Lua IntelliSense is disabled until the provider is recreated with a valid executable path.";

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

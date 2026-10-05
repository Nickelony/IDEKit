namespace Nickelony.LanguageServer.Provider;

/// <summary>Lists the codes of the causes that make a workspace change batch fail to reach the language server.</summary>
internal static class WorkspaceChangeForwardingFailureCodes
{
	/// <summary>The connection could not be started for the batch, so the batch waits for a later replay.</summary>
	internal const string EnsureStartFailed = "EnsureStartFailed";

	/// <summary>The connection failed while the batch was written, so the batch waits for a later replay.</summary>
	internal const string ConnectionFailed = "ConnectionFailed";

	/// <summary>An unexpected exception ended the forward, so the batch is dropped on purpose.</summary>
	internal const string Unexpected = "Unexpected";
}

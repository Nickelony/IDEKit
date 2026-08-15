namespace Nickelony.LanguageServer.Abstractions;

/// <summary>
/// Describes the lifecycle state of a language-server-backed provider.
/// </summary>
public enum LanguageServerProviderState
{
	/// <summary>
	/// The provider has no ready language-server session. This includes the initial lazy-start state and transient recovery.
	/// </summary>
	Unavailable,

	/// <summary>
	/// The provider is starting or restarting its language-server session.
	/// </summary>
	Starting,

	/// <summary>
	/// The provider has a ready language-server session and reports its capabilities.
	/// </summary>
	Ready,

	/// <summary>
	/// A terminal startup failure has disabled the provider for this instance, so it will not retry automatically.
	/// </summary>
	Failed,

	/// <summary>
	/// Disposal has started or completed and the provider cannot accept new work.
	/// </summary>
	Disposed
}

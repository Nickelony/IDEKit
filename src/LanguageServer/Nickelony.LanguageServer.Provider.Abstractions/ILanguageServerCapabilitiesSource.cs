namespace Nickelony.LanguageServer.Provider.Abstractions;

/// <summary>Publishes the capabilities of a language-server provider as one snapshot, and announces each change.</summary>
public interface ILanguageServerCapabilitiesSource
{
	/// <summary>The capabilities the provider currently advertises.</summary>
	/// <remarks>
	/// A read returns the whole snapshot, so a consumer that combines several flags never reads half of an update. Read it once per decision,
	/// and again after <see cref="CapabilitiesChanged"/>, instead of caching a flag.
	/// </remarks>
	LanguageServerFeatureProviderCapabilities Capabilities { get; }

	/// <summary>Raised when lazy startup, restart, or connection loss may have changed the session's capabilities.</summary>
	event EventHandler? CapabilitiesChanged;
}

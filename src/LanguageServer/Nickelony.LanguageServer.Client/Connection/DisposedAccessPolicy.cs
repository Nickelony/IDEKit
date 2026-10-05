namespace Nickelony.LanguageServer.Client;

/// <summary>Selects whether a connection operation enforces the owning client's disposed-state guard.</summary>
internal enum DisposedAccessPolicy
{
	/// <summary>The operation throws when the client has started disposal.</summary>
	Enforce,

	/// <summary>
	/// The operation proceeds while the client is disposing, so teardown can still reach the server with its shutdown and exit messages.
	/// </summary>
	Allow,
}

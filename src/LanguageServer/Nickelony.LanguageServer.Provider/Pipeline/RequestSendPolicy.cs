namespace Nickelony.LanguageServer.Provider;

/// <summary>Chooses whether a request may be timed out and sent again, which depends on whether sending it twice is harmless.</summary>
internal enum RequestSendPolicy
{
	/// <summary>
	/// The request only reads or computes, so a second send is harmless: each attempt has the request timeout, a timeout counts
	/// toward the restart threshold, and a connection boundary earns one retry.
	/// </summary>
	Repeatable,

	/// <summary>
	/// The request can change state on the server, so a second send could repeat the change: no attempt times out, only the caller's
	/// token and the disposal of the provider cancel it, a wait never counts toward the restart threshold, and a connection boundary ends the request
	/// with its fallback.
	/// </summary>
	Once,
}

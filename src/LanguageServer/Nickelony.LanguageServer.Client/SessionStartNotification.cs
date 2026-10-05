namespace Nickelony.LanguageServer.Client;

/// <summary>A notification the client sends to the language server right after <c>initialized</c> on each session start.</summary>
/// <remarks>
/// <see cref="LanguageServerClientOptions.SessionStartNotificationsProvider"/> supplies them, for example the <c>solution/open</c> notification
/// that asks the Roslyn language server to load a solution.
/// </remarks>
public sealed record SessionStartNotification
{
	/// <param name="method">The JSON-RPC method name, for example <c>"project/open"</c>.</param>
	/// <param name="parameters">
	/// The notification parameters, or <see langword="null"/> for none. The client serializes them with the protocol's lower-camel naming policy.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="method"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="method"/> is empty or whitespace-only.</exception>
	public SessionStartNotification(string method, object? parameters)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(method);

		Method = method;
		Parameters = parameters;
	}

	/// <summary>The JSON-RPC method name.</summary>
	public string Method { get; }

	/// <summary>The notification parameters, or <see langword="null"/> for none.</summary>
	public object? Parameters { get; }
}

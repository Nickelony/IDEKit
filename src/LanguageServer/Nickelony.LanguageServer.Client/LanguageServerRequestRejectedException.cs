namespace Nickelony.LanguageServer.Client;

/// <summary>Reports a request that the language server rejected with a JSON-RPC error response.</summary>
/// <remarks>
/// A rejection is a server-side outcome (for example an unsupported method or invalid parameters), not a connection failure: the active connection
/// stays ready and usable. Hosts that treat a request as best-effort can map this exception to their documented fallback value.
/// </remarks>
public sealed class LanguageServerRequestRejectedException : Exception
{
	private const string DefaultMessage = "The language server rejected the request.";

	/// <param name="errorCode">The JSON-RPC error code reported by the language server.</param>
	/// <param name="message">The error message reported by the language server, or <see langword="null"/> for a generic message.</param>
	/// <param name="innerException">The exception raised by the JSON-RPC instance for the rejected request, or <see langword="null"/>.</param>
	public LanguageServerRequestRejectedException(int errorCode, string? message, Exception? innerException)
		: base(message ?? DefaultMessage, innerException) => ErrorCode = errorCode;

	/// <summary>The JSON-RPC error code reported by the language server.</summary>
	public int ErrorCode { get; }
}

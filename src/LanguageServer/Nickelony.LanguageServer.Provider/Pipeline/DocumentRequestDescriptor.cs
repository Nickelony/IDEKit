using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Describes one language-server request.</summary>
/// <typeparam name="TResponse">The expected response payload type.</typeparam>
/// <typeparam name="TResult">The parsed result type.</typeparam>
/// <remarks>
/// A document-scoped request leaves <see cref="Offset"/> unset and a position-scoped request sets it; the request pipeline converts
/// the offset against the document text, which clamps it to the document, before it passes the protocol position to <see cref="BuildParameters"/>.
/// A <see langword="null"/> <see cref="SupportsRequest"/> means the request is always attempted.
/// </remarks>
internal sealed class DocumentRequestDescriptor<TResponse, TResult>
{
	/// <param name="method">The LSP method name.</param>
	/// <param name="buildParameters">Builds the request parameters from the normalized document identifier and the clamped protocol position.</param>
	/// <param name="parseResponse">Parses the response payload against the requested document.</param>
	/// <param name="fallbackValue">
	/// The fallback value returned when the request cannot be issued, the server rejects it, or the response is unusable.
	/// </param>
	/// <param name="supportsRequest">
	/// Reports whether the connected client supports the request, or <see langword="null"/> to always attempt it.
	/// </param>
	/// <param name="offset">
	/// The zero-based caret offset into the document text for a position-scoped request, or <see langword="null"/> for a document-scoped request.
	/// An offset outside the document is clamped to it.
	/// </param>
	internal DocumentRequestDescriptor(
		string method,
		Func<TextDocumentIdentifier, PositionPayload, object> buildParameters,
		Func<TResponse, RequestedDocument, TResult> parseResponse,
		TResult fallbackValue,
		Func<ILanguageServerClient, bool>? supportsRequest = null,
		int? offset = null
	)
	{
		Method = method;
		BuildParameters = buildParameters;
		ParseResponse = parseResponse;
		FallbackValue = fallbackValue;
		SupportsRequest = supportsRequest;
		Offset = offset;
	}

	internal string Method { get; }

	/// <summary>The builder that maps the normalized document identifier and clamped position to the request parameters.</summary>
	internal Func<TextDocumentIdentifier, PositionPayload, object> BuildParameters { get; }

	/// <summary>The parser that maps the response payload and the requested document to the result.</summary>
	internal Func<TResponse, RequestedDocument, TResult> ParseResponse { get; }

	/// <summary>The fallback value returned when the request cannot be issued, the server rejects it, or the response is unusable.</summary>
	internal TResult FallbackValue { get; }

	/// <summary>The capability gate, or <see langword="null"/> when the request is always attempted.</summary>
	internal Func<ILanguageServerClient, bool>? SupportsRequest { get; }

	/// <summary>The zero-based caret offset for a position-scoped request, or <see langword="null"/> for a document-scoped request.</summary>
	internal int? Offset { get; }
}

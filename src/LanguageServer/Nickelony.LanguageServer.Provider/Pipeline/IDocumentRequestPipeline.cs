namespace Nickelony.LanguageServer.Provider;

/// <summary>Sends a document-scoped language-server request through the provider's synchronization, capability gate and fallback policy.</summary>
/// <remarks>
/// Each request-and-response feature builds a <see cref="DocumentRequestDescriptor{TResponse, TResult}"/> and hands it to this pipeline, so a feature
/// reads none of the provider's lifecycle or synchronization state.
/// </remarks>
internal interface IDocumentRequestPipeline
{
	/// <summary>
	/// Synchronizes the requested document, executes the language-server request described by <paramref name="request"/>, and releases the temporary
	/// request lease afterwards.
	/// </summary>
	/// <typeparam name="TResponse">
	/// The expected response payload type. A reference type may arrive as a JSON <see langword="null"/> and then returns the descriptor's
	/// fallback value instead of reaching its parser; a value type is classified through the request outcome, never a <see langword="null"/> check,
	/// so either shape is supported.
	/// </typeparam>
	/// <typeparam name="TResult">The parsed result type.</typeparam>
	/// <param name="filePath">The absolute local file path of the document.</param>
	/// <param name="documentText">The current document content.</param>
	/// <param name="request">The description of the request to issue.</param>
	/// <param name="cancellationToken">A token that can cancel the request.</param>
	/// <returns>The parsed result, or the descriptor's fallback value.</returns>
	/// <exception cref="OperationCanceledException">
	/// <paramref name="cancellationToken"/> is canceled. The temporary request lease is still released before the exception propagates.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The capability gate is evaluated after the connection is ensured but before the document is synchronized, so a request
	/// the connected client does not support is neither synchronized to the server nor temporarily tracked. The gate cannot run before startup:
	/// negotiated capabilities only exist after the initialize handshake. A response payload that cannot be deserialized
	/// as <typeparamref name="TResponse"/> is treated like any other unusable response and returns the fallback value.
	/// </para>
	/// <para>
	/// The outcome is classified from the request itself, not from the response value, so a value-type response - for which a <see langword="null"/>
	/// check can never fire - still returns the fallback value when the request settles with the fallback outcome. The offset the descriptor
	/// carries is converted against <paramref name="documentText"/>, which clamps it to the document, before its parameter builder receives
	/// the protocol position, and its response parser receives the requested document, whose line map is built once for the request.
	/// </para>
	/// <para>
	/// A throwing parameter builder or response parser propagates unchanged: those delegates are caller-supplied code rather than framework policy.
	/// </para>
	/// </remarks>
	Task<TResult> SendAsync<TResponse, TResult>(
		string filePath,
		string documentText,
		DocumentRequestDescriptor<TResponse, TResult> request,
		CancellationToken cancellationToken
	);
}

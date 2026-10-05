using System.Text.Json;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>
/// Records the descriptor of each request a feature provider sends, and answers it with a configured response or the descriptor's fallback.
/// </summary>
/// <remarks>
/// A feature test reads the recorded method, offset, position, capability gate and serialized parameters instead of running a language server.
/// </remarks>
internal sealed class RecordingDocumentRequestPipeline : IDocumentRequestPipeline
{
	private const string DocumentUri = "file:///recorded/document.lua";

	private readonly List<RecordedRequest> _requests = [];

	/// <summary>The requests sent so far, in order.</summary>
	internal IReadOnlyList<RecordedRequest> Requests => _requests;

	/// <summary>The response the next request is answered with, or <see langword="null"/> to answer with the fallback value.</summary>
	/// <remarks>
	/// A response whose type is not the request's response type is treated as no response.
	/// </remarks>
	internal object? Response { get; set; }

	/// <summary>The only request sent, which fails the test when the provider sent none or several.</summary>
	internal RecordedRequest SingleRequest => Assert.ContainsSingle(_requests);

	/// <inheritdoc/>
	public Task<TResult> SendAsync<TResponse, TResult>(
		string filePath,
		string documentText,
		DocumentRequestDescriptor<TResponse, TResult> request,
		CancellationToken cancellationToken
	)
	{
		var document = new RequestedDocument(
			LocalPath.TryNormalize(filePath, out string normalizedFilePath) ? normalizedFilePath : filePath,
			documentText
		);

		TextPosition position = request.Offset is { } offset ? document.LineMap.GetPosition(offset) : default;

		_requests.Add(
			new RecordedRequest(
				filePath,
				documentText,
				Method: request.Method,
				Offset: request.Offset,
				position,
				SupportsRequest: request.SupportsRequest,

				SerializeParameters: () =>
					JsonSerializer.SerializeToElement(
						request.BuildParameters(new TextDocumentIdentifier(DocumentUri), ProtocolPositionConversion.ToProtocolPosition(position))
					),

				FallbackValue: request.FallbackValue
			)
		);

		return Task.FromResult(Response is TResponse response ? request.ParseResponse(response, document) : request.FallbackValue);
	}

	/// <summary>One request a feature provider sent through the pipeline.</summary>
	/// <param name="FilePath">The document path the provider passed.</param>
	/// <param name="DocumentText">The document text the provider passed.</param>
	/// <param name="Method">The LSP method name of the request.</param>
	/// <param name="Offset">The caret offset the descriptor carries, or <see langword="null"/> for a document-scoped request.</param>
	/// <param name="Position">The protocol position the pipeline derives from the offset, or the default for a document-scoped request.</param>
	/// <param name="SupportsRequest">The capability gate, or <see langword="null"/> when the request is always attempted.</param>
	/// <param name="SerializeParameters">Builds and serializes the request parameters for the recorded position.</param>
	/// <param name="FallbackValue">The value returned when the request cannot be answered.</param>
	internal sealed record RecordedRequest(
		string FilePath,
		string DocumentText,
		string Method,
		int? Offset,
		TextPosition Position,
		Func<ILanguageServerClient, bool>? SupportsRequest,
		Func<JsonElement> SerializeParameters,
		object? FallbackValue
	)
	{
		/// <summary>The serialized request parameters.</summary>
		internal JsonElement Parameters => SerializeParameters();

		/// <summary>The document URI the recorded pipeline gave the parameter builder.</summary>
		internal static string Uri => DocumentUri;
	}
}

using System.Text;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>
/// Builds and parses JSON-RPC frames for the client tests, so the framing contract (a <c>Content-Length</c> header followed by the UTF-8 payload)
/// is defined once.
/// </summary>
internal static class JsonRpcFrameTestHelpers
{
	/// <summary>Builds the frame for a JSON-RPC success response.</summary>
	/// <param name="id">The request id the response answers.</param>
	/// <param name="resultJson">The raw JSON of the <c>result</c> member.</param>
	internal static string BuildResultResponse(int id, string resultJson) =>
		BuildFrame("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":" + resultJson + "}");

	/// <summary>Builds the frame for a JSON-RPC error response.</summary>
	/// <param name="id">The request id the response answers.</param>
	/// <param name="message">The error message; it is JSON-escaped.</param>
	internal static string BuildErrorResponse(int id, int code, string message) =>
		BuildFrame("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"error\":{\"code\":" + code + ",\"message\":" + JsonSerializer.Serialize(message) + "}}");

	/// <summary>Reads the integer request id from a frame whose payload has fully arrived.</summary>
	/// <param name="requestId">The parsed request id when a complete frame is present.</param>
	/// <returns><see langword="true"/> when a complete frame with an integer id was read.</returns>
	/// <exception cref="AssertFailedException">A header or the id of a complete frame is malformed.</exception>
	internal static bool TryExtractRequestId(byte[] writtenPayload, out int requestId)
	{
		requestId = 0;

		if (writtenPayload.Length == 0)
			return false;

		string payloadText = Encoding.UTF8.GetString(writtenPayload);
		int bodySeparatorIndex = payloadText.IndexOf("\r\n\r\n", StringComparison.Ordinal);

		if (bodySeparatorIndex < 0)
			return false;

		string headerText = payloadText[..bodySeparatorIndex];
		const string contentLengthPrefix = "Content-Length:";
		int contentLengthLineIndex = headerText.IndexOf(contentLengthPrefix, StringComparison.OrdinalIgnoreCase);

		if (contentLengthLineIndex < 0)
			throw new AssertFailedException("The JSON-RPC request payload did not contain a Content-Length header.");

		int contentLengthValueStart = contentLengthLineIndex + contentLengthPrefix.Length;
		int contentLengthValueEnd = headerText.IndexOf("\r\n", contentLengthValueStart, StringComparison.Ordinal);

		string contentLengthText = (
			contentLengthValueEnd >= 0 ? headerText[contentLengthValueStart..contentLengthValueEnd] : headerText[contentLengthValueStart..]
		).Trim();

		if (!int.TryParse(contentLengthText, out int contentLength) || contentLength < 0)
			throw new AssertFailedException("The JSON-RPC request payload contained an invalid Content-Length header.");

		int bodyStartIndex = bodySeparatorIndex + 4;

		if (writtenPayload.Length < bodyStartIndex + contentLength)
			return false;

		string jsonPayload = Encoding.UTF8.GetString(writtenPayload, bodyStartIndex, contentLength);
		using var document = JsonDocument.Parse(jsonPayload);

		if (!document.RootElement.TryGetProperty("id", out JsonElement idElement) || !idElement.TryGetInt32(out requestId))
			throw new AssertFailedException("The JSON-RPC request payload did not contain an integer request id.");

		return true;
	}

	/// <summary>Splits a written payload into the messages it contains, reading each message's <c>method</c>.</summary>
	/// <returns>One entry per complete frame, in the order they appear.</returns>
	internal static List<(string? Method, string BodyJson)> ExtractWrittenMessages(string writtenText)
	{
		var messages = new List<(string? Method, string BodyJson)>();
		int searchIndex = 0;

		while (true)
		{
			int contentLengthIndex = writtenText.IndexOf("Content-Length:", searchIndex, StringComparison.OrdinalIgnoreCase);

			if (contentLengthIndex < 0)
				break;

			int headerEnd = writtenText.IndexOf("\r\n\r\n", contentLengthIndex, StringComparison.Ordinal);

			if (headerEnd < 0)
				break;

			int contentLengthValueEnd = writtenText.IndexOf("\r\n", contentLengthIndex, StringComparison.Ordinal);
			string contentLengthText = writtenText[(contentLengthIndex + "Content-Length:".Length)..contentLengthValueEnd].Trim();

			if (!int.TryParse(contentLengthText, out int contentLength) || contentLength < 0)
				break;

			int bodyStartIndex = headerEnd + 4;

			if (writtenText.Length < bodyStartIndex + contentLength)
				break;

			string bodyJson = writtenText.Substring(bodyStartIndex, contentLength);
			string? method = null;

			using (var document = JsonDocument.Parse(bodyJson))
			{
				if (document.RootElement.TryGetProperty("method", out JsonElement methodElement))
					method = methodElement.GetString();
			}

			messages.Add((method, bodyJson));
			searchIndex = bodyStartIndex + contentLength;
		}

		return messages;
	}

	/// <summary>Builds the frame for a JSON-RPC request.</summary>
	/// <param name="method">The JSON-RPC method name; it is JSON-escaped.</param>
	/// <param name="paramsJson">The raw JSON of the <c>params</c> member.</param>
	internal static string BuildRequest(int id, string method, string paramsJson) =>
		BuildFrame("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":" + JsonSerializer.Serialize(method) + ",\"params\":" + paramsJson + "}");

	/// <summary>Builds the frame for a JSON-RPC notification.</summary>
	/// <param name="method">The JSON-RPC method name; it is JSON-escaped.</param>
	/// <param name="paramsJson">The raw JSON of the <c>params</c> member.</param>
	internal static string BuildNotification(string method, string paramsJson) =>
		BuildFrame("{\"jsonrpc\":\"2.0\",\"method\":" + JsonSerializer.Serialize(method) + ",\"params\":" + paramsJson + "}");

	private static string BuildFrame(string payload) => "Content-Length: " + Encoding.UTF8.GetByteCount(payload) + "\r\n\r\n" + payload;
}

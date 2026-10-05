using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>A System.Text.Json formatter that reads a message whose <c>params</c> member is <c>null</c> as a message without parameters.</summary>
/// <remarks>
/// The base formatter rejects a <c>null</c> <c>params</c>, which servers such as LuaLS
/// send for a parameterless request like <c>workspace/semanticTokens/refresh</c>. JSON-RPC allows the member to be omitted, so the frame is decoded
/// again without it and the request reaches its RPC method and gets a response. The interface members are implemented again because the base
/// implements them as non-virtual.
/// </remarks>
internal sealed class TolerantSystemTextJsonFormatter : SystemTextJsonFormatter, IJsonRpcMessageTextFormatter
{
	JsonRpcMessage IJsonRpcMessageFormatter.Deserialize(ReadOnlySequence<byte> contentBuffer) =>
		((IJsonRpcMessageTextFormatter)this).Deserialize(contentBuffer, Encoding);

	JsonRpcMessage IJsonRpcMessageTextFormatter.Deserialize(ReadOnlySequence<byte> contentBuffer, Encoding encoding)
	{
		// The rewrite runs only for a frame the base formatter rejected, so a well-formed message costs nothing extra.
		try
		{
			return Deserialize(contentBuffer, encoding);
		}
		catch (Exception) when (TryRemoveNullParams(contentBuffer, encoding, out byte[]? rewritten))
		{
			return Deserialize(new ReadOnlySequence<byte>(rewritten), encoding);
		}
	}

	private static bool TryRemoveNullParams(ReadOnlySequence<byte> contentBuffer, Encoding encoding, [NotNullWhen(true)] out byte[]? rewritten)
	{
		rewritten = null;
		JsonNode? root;

		try
		{
			root = JsonNode.Parse(encoding.GetString(contentBuffer));
		}
		catch (JsonException)
		{
			return false;
		}

		// A present member whose value is null reads as a null node, which tells it apart from a missing member.
		if (root is not JsonObject message || !message.TryGetPropertyValue("params", out JsonNode? parameters) || parameters is not null)
			return false;

		message.Remove("params");
		rewritten = encoding.GetBytes(message.ToJsonString());
		return true;
	}
}

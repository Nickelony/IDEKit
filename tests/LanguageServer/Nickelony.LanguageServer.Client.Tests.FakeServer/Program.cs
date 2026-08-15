using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// Minimal LSP fake server over stdio used by the real-process transport tests: it answers initialize and
// shutdown, echoes example/echo requests so a round trip beyond the initialize handshake can be exercised,
// answers every other request with a JSON-RPC method-not-found error so its scope fails fast instead of leaving a
// caller to time out, ignores notifications, writes one stderr marker, and exits on the exit notification. The
// optional --exit-immediately argument makes it crash before the handshake for the crash-path test, and the
// optional --capabilities <json> argument scripts the capability object it advertises from initialize.

Console.Error.WriteLine("fake-server stderr marker");

if (args.Contains("--exit-immediately", StringComparer.Ordinal))
{
	Console.Error.WriteLine("fake-server exiting immediately by request");
	return;
}

// The capability payload the server advertises in its initialize result. A host passes a JSON object after
// --capabilities so a real-process handshake can satisfy the client's default synchronization requirement
// without a test-only client option; the empty object keeps the no-synchronization and crash-path tests
// unchanged.
string capabilitiesJson = "{}";
int capabilitiesArgumentIndex = Array.IndexOf(args, "--capabilities");

if (capabilitiesArgumentIndex >= 0 && capabilitiesArgumentIndex + 1 < args.Length)
	capabilitiesJson = args[capabilitiesArgumentIndex + 1];

using Stream stdin = Console.OpenStandardInput();
using Stream stdout = Console.OpenStandardOutput();

while (true)
{
	string? message = await ReadMessageAsync(stdin);

	if (message is null)
		break;

	string? method;
	int? id;
	JsonNode? parameters = null;

	try
	{
		JsonNode? node = JsonNode.Parse(message);
		method = node?["method"]?.GetValue<string>();
		id = node?["id"]?.GetValue<int>();
		parameters = node?["params"];
	}
	catch (JsonException)
	{
		continue;
	}
	catch (InvalidOperationException)
	{
		continue;
	}

	switch (method)
	{
		case "initialize":
			if (id is int initializeId)
				await WriteMessageAsync(stdout, $"{{\"jsonrpc\":\"2.0\",\"id\":{initializeId},\"result\":{{\"capabilities\":{capabilitiesJson}}}}}");
			break;

		case "shutdown":
			if (id is int shutdownId)
				await WriteMessageAsync(stdout, $"{{\"jsonrpc\":\"2.0\",\"id\":{shutdownId},\"result\":null}}");
			break;

		case "exit":
			return;

		case "example/echo":
			// Echoes the request parameters back so a real-process test can prove a request/response round trip
			// beyond the initialize handshake carries the caller payload over the stdio pipes.
			if (id is int echoId)
				await WriteMessageAsync(stdout, $"{{\"jsonrpc\":\"2.0\",\"id\":{echoId},\"result\":{{\"echoed\":{parameters?.ToJsonString() ?? "null"}}}}}");
			break;

		default:
			// Requests outside the initialize/shutdown scope are answered explicitly so a caller fails fast
			// instead of waiting for a response that never arrives; notifications (no id) stay ignored.
			if (id is int unknownMethodId)
			{
				await WriteMessageAsync(stdout, $"{{\"jsonrpc\":\"2.0\",\"id\":{unknownMethodId},\"error\":{{\"code\":-32601,\"message\":\"Method not found\"}}}}");
			}

			break;
	}
}

static async Task<string?> ReadMessageAsync(Stream stream)
{
	int contentLength = -1;
	var header = new StringBuilder();

	while (true)
	{
		int value = stream.ReadByte();

		if (value < 0)
			return null;

		header.Append((char)value);

		if (header.Length >= 4
			&& header[^4] == '\r'
			&& header[^3] == '\n'
			&& header[^2] == '\r'
			&& header[^1] == '\n')
		{
			break;
		}
	}

	foreach (string line in header.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
	{
		if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
			contentLength = int.Parse(line["Content-Length:".Length..].Trim(), CultureInfo.InvariantCulture);
	}

	if (contentLength < 0)
		return null;

	byte[] buffer = new byte[contentLength];
	int offset = 0;

	while (offset < contentLength)
	{
		int read = await stream.ReadAsync(buffer.AsMemory(offset, contentLength - offset));

		if (read <= 0)
			return null;

		offset += read;
	}

	return Encoding.UTF8.GetString(buffer);
}

static async Task WriteMessageAsync(Stream stream, string json)
{
	byte[] payload = Encoding.UTF8.GetBytes(json);
	byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");

	await stream.WriteAsync(header);
	await stream.WriteAsync(payload);
	await stream.FlushAsync();
}

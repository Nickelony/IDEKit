using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents a <c>workspace/executeCommand</c> request that asks the server to run one of its commands.</summary>
/// <param name="Command">The identifier of the command, one of the commands the server advertised.</param>
/// <param name="Arguments">The command arguments in wire order, or <see langword="null"/> when the command takes none.</param>
public readonly record struct ExecuteCommandParams(
	[property: JsonPropertyName("command")] string Command,

	[property: JsonPropertyName("arguments")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		IReadOnlyList<JsonElement>? Arguments = null
);

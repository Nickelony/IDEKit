using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents the typed top-level code-action payload returned by a language server.</summary>
/// <remarks>
/// Contains every entry with a usable title, in response order. An entry without one is skipped with a warning, and a payload that is not an array
/// yields an empty response.
/// </remarks>
[JsonConverter(typeof(CodeActionResponseJsonConverter))]
public sealed class CodeActionResponse
{
	public CodeActionResponse()
		: this(null) { }

	/// <param name="codeActions">The usable code-action entries returned by the server.</param>
	public CodeActionResponse(IReadOnlyList<CodeActionPayload>? codeActions) =>
		CodeActions = codeActions is null ? [] : Array.AsReadOnly([.. codeActions]);

	/// <summary>The usable code-action entries returned by the server, in response order.</summary>
	public IReadOnlyList<CodeActionPayload> CodeActions { get; }
}

using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents the typed top-level code-action payload returned by a language server.
/// </summary>
/// <remarks>
/// <para>
/// A JSON <see langword="null"/> response deserializes to a <see langword="null"/> response, and a payload that is
/// not an array leaves <see cref="CodeActions"/> empty. Malformed entries and entries without a usable title are
/// skipped with a warning instead of failing the whole response, and the surviving entries keep their response
/// order.
/// </para>
/// <para>
/// Every entry with a usable title is kept, including edit-less and command-only entries: the protocol requires
/// only the title, and the host owns the <c>workspace/executeCommand</c> and <c>codeAction/resolve</c> policy.
/// A bare <c>Command</c> literal is kept through <see cref="CodeActionPayload.Command"/>, which carries the
/// identifier and arguments from the element itself.
/// </para>
/// </remarks>
[JsonConverter(typeof(CodeActionsResponseJsonConverter))]
public sealed class CodeActionsResponse
{
	/// <summary>
	/// Initializes a new instance of the <see cref="CodeActionsResponse"/> class.
	/// </summary>
	public CodeActionsResponse()
		: this(null)
	{ }

	/// <summary>
	/// Initializes a new instance of the <see cref="CodeActionsResponse"/> class.
	/// </summary>
	/// <param name="codeActions">The usable code-action entries returned by the server.</param>
	public CodeActionsResponse(IReadOnlyList<CodeActionPayload>? codeActions)
		=> CodeActions = codeActions is null ? [] : Array.AsReadOnly([.. codeActions]);

	/// <summary>
	/// Gets the usable code-action entries returned by the server, in response order.
	/// </summary>
	public IReadOnlyList<CodeActionPayload> CodeActions { get; }
}

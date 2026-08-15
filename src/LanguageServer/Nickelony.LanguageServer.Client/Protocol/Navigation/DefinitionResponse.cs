using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents one or more usable definition targets returned by a language server.
/// </summary>
/// <remarks>
/// Serialization writes one location object per target with its full start and end range, and targets parsed from
/// location links keep their distinct selection range. Targets stay in zero-based protocol coordinates; hosts
/// convert them to their editor range model through <c>ProtocolRangeConversion</c>. Malformed target entries are
/// skipped; a JSON <see langword="null"/> response deserializes to a <see langword="null"/> response.
/// </remarks>
[JsonConverter(typeof(DefinitionResponseJsonConverter))]
public sealed class DefinitionResponse
{
	/// <summary>
	/// Initializes a new instance of the <see cref="DefinitionResponse"/> class.
	/// </summary>
	/// <param name="targets">The usable definition targets returned by the server.</param>
	public DefinitionResponse(IReadOnlyList<DefinitionTargetPayload>? targets)
		=> Targets = targets is null ? [] : Array.AsReadOnly([.. targets]);

	/// <summary>
	/// Gets the usable definition targets returned by the server, in response order.
	/// </summary>
	public IReadOnlyList<DefinitionTargetPayload> Targets { get; }

	/// <summary>
	/// Gets the first usable definition target, when any were returned.
	/// </summary>
	public DefinitionTargetPayload? FirstTarget => Targets.Count > 0 ? Targets[0] : null;
}

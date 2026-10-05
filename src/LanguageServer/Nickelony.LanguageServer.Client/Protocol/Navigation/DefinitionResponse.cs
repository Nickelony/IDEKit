using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents the usable definition targets returned by a language server, which may be none.</summary>
/// <remarks>
/// Serialization writes a location object for a target without a selection range and a location link for a target
/// that has one, so a target parsed from a location link keeps its distinct selection range. Targets stay in zero-based protocol coordinates,
/// so a host maps them into its own range model. Malformed target entries are skipped; a JSON <see langword="null"/> response deserializes
/// to a <see langword="null"/> response.
/// </remarks>
[JsonConverter(typeof(DefinitionResponseJsonConverter))]
public sealed class DefinitionResponse
{
	/// <param name="targets">The usable definition targets returned by the server.</param>
	public DefinitionResponse(IReadOnlyList<DefinitionTargetPayload>? targets) => Targets = targets is null ? [] : Array.AsReadOnly([.. targets]);

	/// <summary>The usable definition targets returned by the server, in response order.</summary>
	public IReadOnlyList<DefinitionTargetPayload> Targets { get; }

	/// <summary>The first usable definition target, or <see langword="null"/> when none was returned.</summary>
	public DefinitionTargetPayload? FirstTarget => Targets.Count > 0 ? Targets[0] : null;
}

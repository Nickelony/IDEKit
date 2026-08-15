using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Represents a completion text edit in one of the two legal protocol shapes.
/// </summary>
/// <remarks>
/// <para>
/// LSP defines <c>textEdit</c> as a union of a classic text edit (a replacement text plus a single
/// replace range) and an insert/replace edit (a replacement text plus separate insert and replace
/// ranges). The payload models that union as this abstract base class of
/// <see cref="CompletionRangeTextEditPayload"/> and <see cref="CompletionInsertReplaceTextEditPayload"/>,
/// so an illegal combination - a range together with insert or replace ranges, or insert without
/// replace - is not representable; <see cref="CompletionTextEditPayloadJsonConverter"/> rejects such
/// payloads with <see cref="System.Text.Json.JsonException"/>.
/// </para>
/// <para>
/// <see cref="NewText"/> is optional in both shapes: when it is <see langword="null"/>, a commit path
/// falls back to the completion item's insert text.
/// </para>
/// </remarks>
[JsonConverter(typeof(CompletionTextEditPayloadJsonConverter))]
public abstract record CompletionTextEditPayload
{
	/// <summary>
	/// Gets the replacement text the edit commits, or <see langword="null"/> when the commit uses the
	/// completion item's insert text.
	/// </summary>
	[JsonPropertyName("newText")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? NewText { get; init; }
}

/// <summary>
/// Represents a classic completion text edit that replaces one document range.
/// </summary>
public sealed record CompletionRangeTextEditPayload : CompletionTextEditPayload
{
	/// <summary>
	/// Gets the replaced document range.
	/// </summary>
	[JsonPropertyName("range")]
	public required ProtocolRangePayload Range { get; init; }
}

/// <summary>
/// Represents an insert/replace completion edit with separate insert and replace ranges.
/// </summary>
public sealed record CompletionInsertReplaceTextEditPayload : CompletionTextEditPayload
{
	/// <summary>
	/// Gets the range that receives the inserted text when the completion is committed.
	/// </summary>
	[JsonPropertyName("insert")]
	public required ProtocolRangePayload Insert { get; init; }

	/// <summary>
	/// Gets the range that is replaced when the completion is committed.
	/// </summary>
	[JsonPropertyName("replace")]
	public required ProtocolRangePayload Replace { get; init; }
}

using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents a completion text edit in either legal protocol shape.</summary>
/// <remarks>
/// <para>
/// LSP defines <c>textEdit</c> as a union of a classic text edit (a replacement text plus a single replace range)
/// and an insert/replace edit (a replacement text plus separate insert and replace ranges). The payload models that union as this abstract
/// base class of <see cref="CompletionRangeTextEditPayload"/> and <see cref="CompletionInsertReplaceTextEditPayload"/>. An illegal combination
/// (a range together with insert or replace ranges, or insert without replace) is not representable, and deserialization rejects such payloads
/// with <see cref="System.Text.Json.JsonException"/>.
/// </para>
/// <para>
/// <see cref="NewText"/> is optional: when it is <see langword="null"/>, a commit path falls back to the completion item's insert text.
/// </para>
/// </remarks>
[JsonConverter(typeof(CompletionTextEditPayloadJsonConverter))]
public abstract record CompletionTextEditPayload
{
	/// <summary>The replacement text the edit commits, or <see langword="null"/> when the commit uses the completion item's insert text.</summary>
	[JsonPropertyName("newText")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? NewText { get; init; }
}

/// <summary>Represents a classic completion text edit that replaces one document range.</summary>
public sealed record CompletionRangeTextEditPayload : CompletionTextEditPayload
{
	/// <summary>The replaced document range.</summary>
	[JsonPropertyName("range")]
	public required RangePayload Range { get; init; }
}

/// <summary>Represents an insert/replace completion edit with separate insert and replace ranges.</summary>
public sealed record CompletionInsertReplaceTextEditPayload : CompletionTextEditPayload
{
	/// <summary>The range that receives the inserted text when the completion is committed.</summary>
	[JsonPropertyName("insert")]
	public required RangePayload Insert { get; init; }

	/// <summary>The range that is replaced when the completion is committed.</summary>
	[JsonPropertyName("replace")]
	public required RangePayload Replace { get; init; }
}
